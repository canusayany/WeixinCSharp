using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Weixin.Protocol;

namespace Weixin.Protocol.Tests;

/// <summary>Offline HTTP contract tests; no WeChat account or network is used.</summary>
public static class ILinkClientTests
{
    public static async Task RunAsync()
    {
        int passed = 0;
        await CheckAsync(QrCreationAsync);
        await CheckAsync(QrStatusAsync);
        await CheckAsync(GetUpdatesAsync);
        await CheckAsync(SendTextAsync);
        await CheckAsync(OptionalResultCodesAsync);
        await CheckAsync(BusinessAndAuthenticationErrorsAsync);
        await CheckAsync(ServerFailureDoesNotRetryAsync);
        await CheckAsync(PollTimeoutIsIdleAsync);
        await CheckAsync(ExternalCancellationPropagatesAsync);
        await CheckAsync(MalformedSendIsUnknownAsync);
        await CheckAsync(IdentifiersRetainPrecisionAsync);
        await CheckAsync(HostValidationAsync);
        Console.WriteLine($"ILinkClientTests: {passed} test groups passed.");

        async Task CheckAsync(Func<Task> test)
        {
            await test();
            passed++;
        }
    }

    private static async Task QrCreationAsync()
    {
        using var handler = new RecordingHandler((_, _) => Reply("{\"ret\":0,\"qrcode\":\"fixture-qr\",\"qrcode_img_content\":\"fixture-image\"}"));
        using var http = new HttpClient(handler);
        using var client = new ILinkClient(httpClient: http);
        var qr = await client.GetQrCodeAsync("fixture-existing-binding");
        Assert(qr.Code == "fixture-qr" && qr.Content == "fixture-image", "QR fields must deserialize.");
        var request = handler.Requests.Single();
        Assert(request.Method == HttpMethod.Post, "QR creation must POST.");
        Assert(request.Uri.AbsolutePath == "/ilink/bot/get_bot_qrcode" && request.Uri.Query == "?bot_type=3", "QR bot_type must be 3.");
        AssertCommonHeaders(request);
        Assert(!request.Headers.ContainsKey("Authorization"), "QR creation must not carry a Bearer token.");
        AssertPostMetadata(request);
        using (var body = JsonDocument.Parse(request.Body!))
        {
            Assert(!body.RootElement.TryGetProperty("base_info", out _), "QR creation must not include base_info.");
            var tokens = body.RootElement.GetProperty("local_token_list");
            Assert(tokens.GetArrayLength() == 1 && tokens[0].GetString() == "fixture-existing-binding", "QR should carry only the supplied local binding.");
        }
        await client.GetQrCodeAsync();
        using var fresh = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert(fresh.RootElement.GetProperty("local_token_list").GetArrayLength() == 0, "New QR binding must use an empty token list.");
        Assert(!handler.Requests[1].Headers.ContainsKey("Authorization"), "Fresh QR must not authenticate with Bearer.");
    }

    private static async Task QrStatusAsync()
    {
        using var handler = new RecordingHandler((_, _) => Reply("{\"ret\":0,\"status\":\"wait\"}"));
        using var http = new HttpClient(handler);
        using var client = new ILinkClient(httpClient: http);
        const string qr = "qr +/&?=汉字#";
        const string verify = "verify +/&?=汉字#";
        Assert((await client.GetQrStatusAsync(ILinkClient.DefaultBaseUrl, qr, verify)).Status == "wait", "QR status must deserialize.");
        var request = handler.Requests.Single();
        Assert(request.Method == HttpMethod.Get, "QR status must GET.");
        Assert(request.Uri.Query == "?qrcode=" + Uri.EscapeDataString(qr) + "&verify_code=" + Uri.EscapeDataString(verify), "Both QR values must be encoded as query values.");
        AssertCommonHeaders(request);
        Assert(!request.Headers.ContainsKey("Authorization") && !request.Headers.ContainsKey("AuthorizationType")
            && !request.Headers.ContainsKey("X-WECHAT-UIN"), "QR GET must not contain POST authentication metadata.");
        Assert(request.Body is null, "QR GET must have no body.");
    }

    private static async Task GetUpdatesAsync()
    {
        using var handler = new RecordingHandler((_, _) => Reply("{\"ret\":0,\"get_updates_buf\":\"cursor-after\",\"msgs\":[]}"));
        using var http = new HttpClient(handler);
        using var client = new ILinkClient(httpClient: http);
        var session = Session();
        var updates = await client.GetUpdatesAsync(session, TimeSpan.FromSeconds(1));
        Assert(updates.Cursor == "cursor-after", "Update response cursor must be retained.");
        var request = handler.Requests.Single();
        Assert(request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/ilink/bot/getupdates", "Updates must POST to getupdates.");
        AssertAuthenticated(request, session);
        using var body = JsonDocument.Parse(request.Body!);
        Assert(body.RootElement.GetProperty("get_updates_buf").GetString() == session.Cursor, "Updates must submit the saved cursor.");
        AssertBaseInfo(body.RootElement);
    }

    private static async Task SendTextAsync()
    {
        using var handler = new RecordingHandler((_, _) => Reply("{\"ret\":0}"));
        using var http = new HttpClient(handler);
        using var client = new ILinkClient(httpClient: http);
        var session = Session();
        const string text = "你好，fixture 👋";
        const string clientId = "fixture-stable-client-id";
        await client.SendTextAsync(session, text, clientId);
        var request = handler.Requests.Single();
        Assert(request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/ilink/bot/sendmessage", "Text must POST to sendmessage.");
        AssertAuthenticated(request, session);
        using var body = JsonDocument.Parse(request.Body!);
        AssertBaseInfo(body.RootElement);
        var msg = body.RootElement.GetProperty("msg");
        Assert(msg.GetProperty("from_user_id").GetString() == "", "Outbound from_user_id must be empty.");
        Assert(msg.GetProperty("to_user_id").GetString() == session.UserId, "Text may address only the supplied binding peer.");
        Assert(msg.GetProperty("client_id").GetString() == clientId, "Stable client_id must be sent unchanged.");
        Assert(msg.GetProperty("context_token").GetString() == session.ContextToken, "Text must use the bound context token.");
        Assert(msg.GetProperty("message_type").GetInt32() == 2 && msg.GetProperty("message_state").GetInt32() == 2, "Outbound type/state must match the text schema.");
        var items = msg.GetProperty("item_list");
        Assert(items.GetArrayLength() == 1 && items[0].GetProperty("type").GetInt32() == 1
            && items[0].GetProperty("text_item").GetProperty("text").GetString() == text, "Text must use exactly one text item.");
    }

    private static async Task BusinessAndAuthenticationErrorsAsync()
    {
        var cases = new[]
        {
            (Json: "{\"ret\":7,\"errmsg\":\"fixture-server-secret\"}", Status: HttpStatusCode.OK, Ret: (int?)7, Error: (int?)null, Expired: false),
            (Json: "{\"ret\":-14}", Status: HttpStatusCode.OK, Ret: (int?)-14, Error: (int?)null, Expired: true),
            (Json: "{\"ret\":0,\"errcode\":-14}", Status: HttpStatusCode.OK, Ret: (int?)0, Error: (int?)-14, Expired: true),
            (Json: "{\"ret\":0}", Status: HttpStatusCode.Unauthorized, Ret: (int?)null, Error: (int?)null, Expired: true)
        };
        foreach (var item in cases)
        {
            using var handler = new RecordingHandler((_, _) => Reply(item.Json, item.Status));
            using var http = new HttpClient(handler);
            using var client = new ILinkClient(httpClient: http);
            var error = await ThrowsAsync<ApiException>(() => client.SendTextAsync(Session(), "fixture", "id"));
            Assert(error.Ret == item.Ret && error.ErrorCode == item.Error, "Business result codes must remain available.");
            Assert(!error.IsTransient && error.SessionExpired == item.Expired, "Business/auth errors must not be treated as retryable; expired binding must request a stop.");
            Assert(item.Status != HttpStatusCode.Unauthorized || error.HttpStatus == 401, "HTTP 401 must remain identifiable.");
            Assert(!error.Message.Contains("fixture-server-secret", StringComparison.Ordinal), "API exceptions must not expose server messages.");
            Assert(handler.Requests.Count == 1, "Business/auth rejection must not retry.");
        }
    }

    private static async Task OptionalResultCodesAsync()
    {
        // Tencent's optional protobuf scalars omit success (zero) from JSON.
        foreach (var json in new[] { "{}", "{\"message_id\":18446744073709551615}", "{\"ret\":0}" })
        {
            using var handler = new RecordingHandler((_, _) => Reply(json));
            using var http = new HttpClient(handler);
            using var client = new ILinkClient(httpClient: http);
            await client.SendTextAsync(Session(), "fixture", "stable-id");
            await client.NotifyAsync(Session(), true);
            await client.NotifyAsync(Session(), false);
            Assert(handler.Requests.Count == 3, "Optional success codes must accept each operation exactly once.");
        }
        foreach (var json in new[] { "{}", "{\"msgs\":[],\"get_updates_buf\":\"next\"}" })
        {
            using var handler = new RecordingHandler((_, _) => Reply(json));
            using var http = new HttpClient(handler);
            using var client = new ILinkClient(httpClient: http);
            var updates = await client.GetUpdatesAsync(Session(), TimeSpan.FromSeconds(1));
            Assert(updates.Messages.Count == 0 && (updates.Cursor is null or "next"), "Missing update codes must allow empty updates.");
        }
    }

    private static async Task ServerFailureDoesNotRetryAsync()
    {
        foreach (var status in new[] { HttpStatusCode.RequestTimeout, HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable })
        {
            using var handler = new RecordingHandler((_, _) => Reply("{\"ret\":0}", status));
            using var http = new HttpClient(handler);
            using var client = new ILinkClient(httpClient: http);
            await ThrowsAsync<DeliveryUnknownException>(() => client.SendTextAsync(Session(), "fixture", "stable-id"));
            Assert(handler.Requests.Count == 1, "An HTTP 408/5xx send has unknown delivery and must make only one attempt.");
        }
    }

    private static async Task PollTimeoutIsIdleAsync()
    {
        using var handler = new RecordingHandler(async (_, cancellation) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            return await Reply("{}");
        });
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var client = new ILinkClient(httpClient: http);
        var session = Session();
        var updates = await client.GetUpdatesAsync(session, TimeSpan.FromMilliseconds(30));
        Assert(updates.Messages.Count == 0 && updates.Cursor == session.Cursor, "Long poll timeout must preserve cursor and return idle.");
        Assert(handler.Requests.Count == 1, "An idle poll must make one HTTP attempt.");
    }

    private static async Task ExternalCancellationPropagatesAsync()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new RecordingHandler(async (_, requestCancellation) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, requestCancellation);
            return await Reply("{}");
        });
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var client = new ILinkClient(httpClient: http);
        await ThrowsAsync<OperationCanceledException>(() => client.GetUpdatesAsync(Session(), TimeSpan.FromSeconds(1), cancellation.Token));
        Assert(handler.Requests.Count == 1, "Cancellation during an active poll must propagate without retry.");
    }

    private static async Task MalformedSendIsUnknownAsync()
    {
        foreach (var json in new[] { "{", "", "[]", "{\"ret\":\"0\"}", "{\"errcode\":null}" })
        {
            using var handler = new RecordingHandler((_, _) => Reply(json));
            using var http = new HttpClient(handler);
            using var client = new ILinkClient(httpClient: http);
            await ThrowsAsync<DeliveryUnknownException>(() => client.SendTextAsync(Session(), "fixture", "stable-id"));
            Assert(handler.Requests.Count == 1, "Malformed send acknowledgment must not be retried.");
        }
    }

    private static async Task IdentifiersRetainPrecisionAsync()
    {
        const string json = """
            {"ret":0,"msgs":[{"message_id":18446744073709551615,"seq":900719925474099312345,"create_time_ms":9223372036854775800,"item_list":[{"type":1,"text_item":{"text":"fixture"}},{"type":2,"image_item":{"key":"media-fixture"}}],"future_id":18446744073709551615},{"message_id":"00018446744073709551615","seq":-9007199254740993}]}
            """;
        using var handler = new RecordingHandler((_, _) => Reply(json));
        using var http = new HttpClient(handler);
        using var client = new ILinkClient(httpClient: http);
        var messages = (await client.GetUpdatesAsync(Session(), TimeSpan.FromSeconds(1))).Messages;
        Assert(messages[0].MessageId == "18446744073709551615", "UInt64-sized numeric ID must retain every digit.");
        Assert(messages[0].Sequence == "900719925474099312345", "IDs larger than UInt64 must remain exact strings.");
        Assert(messages[0].CreatedMilliseconds == 9223372036854775800L, "Int64 timestamps must retain every digit.");
        Assert(messages[1].MessageId == "00018446744073709551615" && messages[1].Sequence == "-9007199254740993", "String leading zeros and signed numeric IDs must remain exact.");
        Assert(messages[0].OtherFields!["future_id"].GetRawText() == "18446744073709551615", "Unknown numeric fields must remain lossless JSON.");
        Assert(messages[0].Text == "fixture" && messages[0].Items![1].ImageItem!.OtherFields!["key"].GetString() == "media-fixture",
            "Typed media and unknown descriptor fields must be preserved without treating them as text.");
        var roundTrip = JsonSerializer.Deserialize<InboundMessage>(JsonSerializer.Serialize(messages[0], ILinkClient.Json), ILinkClient.Json)!;
        Assert(roundTrip.MessageId == messages[0].MessageId && roundTrip.Sequence == messages[0].Sequence, "Persisted identifiers must remain exact.");
    }

    private static Task HostValidationAsync()
    {
        using var handler = new RecordingHandler((_, _) => Reply("{}"));
        using var http = new HttpClient(handler);
        using var client = new ILinkClient(httpClient: http);
        Assert(client.ValidateBaseUrl(ILinkClient.DefaultBaseUrl).Host == "ilinkai.weixin.qq.com", "Official default host must validate.");
        foreach (var url in new[]
        {
            "http://ilinkai.weixin.qq.com", "https://unknown.invalid", "https://ilinkai.weixin.qq.com:444",
            "https://ilinkai.weixin.qq.com/other", "https://user:password@ilinkai.weixin.qq.com",
            "https://ilinkai.weixin.qq.com?query=1", "https://ilinkai.weixin.qq.com#fragment",
            "https://ilinkai.weixin.qq.com.attacker.invalid", "https://child.ilinkai.weixin.qq.com", "https://*.weixin.qq.com"
        })
            Throws<InvalidOperationException>(() => client.ValidateBaseUrl(url));

        foreach (var host in new[] { "*.example.invalid", "https://example.invalid", "example.invalid:443", "example.invalid/path", "user@example.invalid", "127.0.0.1" })
            Throws<ArgumentException>(() => { using var rejected = new ILinkClient([host], http); });
        using var allowed = new ILinkClient(["explicit.example.invalid"], http);
        Assert(allowed.ValidateBaseUrl("https://explicit.example.invalid").Host == "explicit.example.invalid", "Only an explicitly allowed exact extra host may validate.");
        Throws<InvalidOperationException>(() => allowed.ValidateBaseUrl("https://child.explicit.example.invalid"));
        Throws<InvalidOperationException>(() => allowed.ValidateBaseUrl("https://explicit.example.invalid.attacker.invalid"));
        Assert(handler.Requests.Count == 0, "Host checks must not require or trigger a network request.");
        return Task.CompletedTask;
    }

    private static BotSession Session() => new()
    {
        BotToken = "fixture-bot-token", BotId = "fixture-bot-id", UserId = "fixture-bound-peer",
        BaseUrl = ILinkClient.DefaultBaseUrl, Cursor = "fixture-before-cursor", ContextToken = "fixture-context-token"
    };

    private static void AssertCommonHeaders(ObservedRequest request)
    {
        Assert(Header(request, "iLink-App-Id") == "bot", "App ID header must be present.");
        Assert(Header(request, "iLink-App-ClientVersion") == "132105", "Protocol client version header must be present.");
        Assert(!request.Headers.ContainsKey("User-Agent"), "The default official request has no custom User-Agent.");
    }

    private static void AssertPostMetadata(ObservedRequest request)
    {
        Assert(Header(request, "AuthorizationType") == "ilink_bot_token", "POST authorization type must match the bot protocol.");
        var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(Header(request, "X-WECHAT-UIN")));
        Assert(uint.TryParse(decoded, NumberStyles.None, CultureInfo.InvariantCulture, out _), "X-WECHAT-UIN must encode a decimal UInt32.");
        Assert(request.ContentType == "application/json", "POST content must be JSON.");
    }

    private static void AssertAuthenticated(ObservedRequest request, BotSession session)
    {
        AssertCommonHeaders(request);
        AssertPostMetadata(request);
        Assert(Header(request, "Authorization") == "Bearer " + session.BotToken, "Bound requests must carry the supplied bot token.");
    }

    private static void AssertBaseInfo(JsonElement root)
    {
        var metadata = root.GetProperty("base_info");
        Assert(metadata.GetProperty("channel_version").GetString() == ILinkClient.ProtocolVersion, "Requests must identify the pinned protocol version.");
        Assert(metadata.GetProperty("bot_agent").GetString() == "OpenClaw", "Requests must match the official default bot_agent.");
        Assert(metadata.EnumerateObject().Count() == 2, "No additional product marker may be added to base_info.");
    }

    private static string Header(ObservedRequest request, string name) => request.Headers.TryGetValue(name, out var values)
        ? string.Join(",", values) : throw new InvalidOperationException("Required request header is absent.");

    private static Task<HttpResponseMessage> Reply(string json, HttpStatusCode status = HttpStatusCode.OK) => Task.FromResult(new HttpResponseMessage(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    });

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed record ObservedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string[]> Headers, string? Body, string? ContentType);

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public List<ObservedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new ObservedRequest(request.Method, request.RequestUri!,
                request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Content?.Headers.ContentType?.MediaType));
            return await response(request, cancellationToken);
        }
    }
}
