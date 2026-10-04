using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Weixin.Protocol;

namespace Weixin.Protocol.Tests;

/// <summary>Typing/config HTTP fixtures only; no live account, network or ticket files.</summary>
public static class TypingClientTests
{
    private const string Ticket = "fixture-typing-ticket-secret";
    private const string Token = "fixture-binding-token-secret";
    public static async Task RunAsync()
    {
        var tests = new Func<Task>[]
        {
            ConfigContractAndCacheAsync, OptionalConfigContextAndMissingTicketAsync,
            ConfigRequiresExplicitZeroAsync, CacheIsBindingScopedAsync, ConcurrentConfigFetchesShareCacheAsync,
            TypingStartStopContractAsync, TypingIgnoresSuccessfulBodyAsync, HttpFailuresAreSafeAsync,
            ExternalCancellationPropagatesAsync, InternalTimeoutIsTransientAsync,
            InvalidParametersFailBeforeHttpAsync, TransportAndMalformedConfigAreSafeAsync,
            ConfigCacheIsBoundedAsync
        };
        foreach (var test in tests) await test();
        Console.WriteLine($"TypingClientTests: {tests.Length} test groups passed (offline HTTP; tickets never persisted).");
    }

    private static async Task ConfigContractAndCacheAsync()
    {
        using var fixture = new Fixture();
        var session = Session();
        var first = await fixture.Client.GetConfigAsync(session);
        var repeated = await fixture.Client.GetConfigAsync(session);
        Assert(ReferenceEquals(first, repeated) && first.TypingTicket == Ticket && first.HasTypingTicket && fixture.Requests.Count == 1,
            "Only one explicit ret=0 config is fetched for an unchanged binding.");
        var request = fixture.Requests.Single(); AssertHeaders(request);
        Assert(request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/ilink/bot/getconfig", "getConfig must POST to its official endpoint.");
        using var body = JsonDocument.Parse(request.Body);
        Assert(body.RootElement.GetProperty("ilink_user_id").GetString() == session.UserId &&
            body.RootElement.GetProperty("context_token").GetString() == session.ContextToken && body.RootElement.EnumerateObject().Count() == 3,
            "getConfig must carry the bound peer, optional context and base_info only.");
        AssertBaseInfo(body.RootElement);
        Assert(!JsonSerializer.Serialize(first).Contains(Ticket, StringComparison.Ordinal) && !first.ToString().Contains(Ticket, StringComparison.Ordinal),
            "Default serialization and diagnostics must not expose the ticket.");
        var state = new BotState { Session = session };
        Assert(!JsonSerializer.Serialize(state).Contains(Ticket, StringComparison.Ordinal), "Ticket retrieval must not modify persistent BotState.");
    }

    private static async Task OptionalConfigContextAndMissingTicketAsync()
    {
        foreach (var context in new string?[] { null, "" })
        {
            using var fixture = new Fixture((_, _) => Task.FromResult(Reply("{\"ret\":0}")));
            var session = Session(); session.ContextToken = context;
            var config = await fixture.Client.GetConfigAsync(session);
            var again = await fixture.Client.GetConfigAsync(session);
            Assert(!config.HasTypingTicket && config.TypingTicket == "" && ReferenceEquals(config, again) && fixture.Requests.Count == 1,
                "ret=0 without a ticket is a cached unavailable feature, not an invented ticket.");
            using var body = JsonDocument.Parse(fixture.Requests.Single().Body);
            Assert(body.RootElement.TryGetProperty("context_token", out var field) == (context is not null), "Absent context must be omitted like JSON.stringify(undefined).");
            if (context is not null) Assert(field.GetString() == "", "An explicit empty optional context is preserved.");
            await ThrowsAsync<ArgumentException>(() => fixture.Client.SetTypingAsync(session, true, config.TypingTicket));
            Assert(fixture.Requests.Count == 1, "Missing tickets must stop before sendtyping HTTP.");
        }
    }

    private static async Task ConfigRequiresExplicitZeroAsync()
    {
        foreach (var invalid in new[]
        {
            "{}", "{\"typing_ticket\":\"" + Ticket + "\"}",
            "{\"errcode\":0,\"typing_ticket\":\"" + Ticket + "\"}",
            "{\"ret\":7,\"errmsg\":\"" + Token + "\",\"typing_ticket\":\"" + Ticket + "\"}",
            "{\"ret\":-14,\"typing_ticket\":\"" + Ticket + "\"}",
            "{\"ret\":0,\"errcode\":-14,\"typing_ticket\":\"" + Ticket + "\"}",
            "{\"ret\":null,\"typing_ticket\":\"" + Ticket + "\"}"
        })
        {
            int calls = 0;
            using var fixture = new Fixture((_, _) => Task.FromResult(Reply(++calls == 1 ? invalid : ConfigJson())));
            var error = await ThrowsAsync<ApiException>(() => fixture.Client.GetConfigAsync(Session())); AssertSafe(error);
            Assert(error.SessionExpired == invalid.Contains("-14", StringComparison.Ordinal), "Explicit stale-token codes must stay identifiable.");
            var valid = await fixture.Client.GetConfigAsync(Session());
            Assert(valid.TypingTicket == Ticket && calls == 2, "Missing/nonzero config result codes must not poison the success cache.");
        }
    }

    private static async Task CacheIsBindingScopedAsync()
    {
        using var fixture = new Fixture();
        var first = Session(); await fixture.Client.GetConfigAsync(first);
        var changedContext = Session(); changedContext.ContextToken = "new-context"; await fixture.Client.GetConfigAsync(changedContext);
        Assert(fixture.Requests.Count == 1, "The official per-user config cache does not refetch for every context token.");
        var changedToken = Session(); changedToken.BotToken = "replacement-binding-token";
        var changedPeer = Session(); changedPeer.UserId = "another-bound-peer";
        var changedBot = Session(); changedBot.BotId = "another-bot";
        var changedHost = Session(); changedHost.BaseUrl = "https://typing-fixture.example.invalid";
        foreach (var binding in new[] { changedToken, changedPeer, changedBot, changedHost }) await fixture.Client.GetConfigAsync(binding);
        await fixture.Client.GetConfigAsync(first);
        Assert(fixture.Requests.Count == 5, "Different credentials, peers, bots and allowed hosts must never share a cached ticket.");
    }

    private static async Task ConcurrentConfigFetchesShareCacheAsync()
    {
        using var fixture = new Fixture(async (_, ct) => { await Task.Delay(30, ct); return Reply(ConfigJson()); });
        var configs = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fixture.Client.GetConfigAsync(Session())));
        Assert(fixture.Requests.Count == 1 && configs.All(c => c.TypingTicket == Ticket && ReferenceEquals(c, configs[0])),
            "Concurrent requests for the same binding must share one accepted config fetch.");
    }

    private static async Task TypingStartStopContractAsync()
    {
        using var fixture = new Fixture();
        await fixture.Client.SetTypingAsync(Session(), true, Ticket);
        await fixture.Client.SetTypingAsync(Session(), false, Ticket);
        for (int index = 0; index < fixture.Requests.Count; index++)
        {
            var request = fixture.Requests[index]; AssertHeaders(request);
            Assert(request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/ilink/bot/sendtyping", "Typing uses the official POST endpoint.");
            using var body = JsonDocument.Parse(request.Body);
            Assert(body.RootElement.GetProperty("ilink_user_id").GetString() == Session().UserId &&
                body.RootElement.GetProperty("typing_ticket").GetString() == Ticket && body.RootElement.GetProperty("status").GetInt32() == index + 1,
                "Official status values are TYPING=1 and CANCEL=2; the ticket targets only the bound peer.");
            Assert(body.RootElement.EnumerateObject().Count() == 4 && !body.RootElement.TryGetProperty("context_token", out _),
                "sendtyping has exactly the official peer/ticket/status/base_info fields.");
            AssertBaseInfo(body.RootElement);
        }
        Assert(fixture.Requests.Count == 2, "Start and cancel each make one independent request.");
    }

    private static async Task TypingIgnoresSuccessfulBodyAsync()
    {
        foreach (var body in new[] { "", "{}", "not-json", "{\"ret\":-14,\"errmsg\":\"" + Ticket + "\"}" })
        {
            using var fixture = new Fixture((_, _) => Task.FromResult(Reply(body)));
            await fixture.Client.SetTypingAsync(Session(), true, Ticket);
            Assert(fixture.Requests.Count == 1, "Official sendTyping does not parse a successful HTTP body, even for empty/malformed JSON.");
        }
    }

    private static async Task HttpFailuresAreSafeAsync()
    {
        foreach (bool config in new[] { false, true })
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.RequestTimeout, HttpStatusCode.ServiceUnavailable })
        {
            using var fixture = new Fixture((_, _) =>
            {
                var response = Reply("{\"errmsg\":\"" + Token + " " + Ticket + "\"}", status);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
                return Task.FromResult(response);
            });
            var error = await ThrowsAsync<ApiException>(() => config ? fixture.Client.GetConfigAsync(Session()) : fixture.Client.SetTypingAsync(Session(), true, Ticket));
            AssertSafe(error);
            Assert(error.HttpStatus == (int)status && error.SessionExpired == (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) &&
                error.IsTransient == (status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout or HttpStatusCode.ServiceUnavailable) && error.RetryAfter == TimeSpan.FromSeconds(2),
                "HTTP authentication/transient classification and Retry-After must remain accurate.");
            Assert(fixture.Requests.Count == 1, "Config and typing never silently retry an HTTP rejection.");
        }
    }

    private static async Task ExternalCancellationPropagatesAsync()
    {
        foreach (bool config in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            using var fixture = new Fixture(async (_, ct) =>
            {
                cancellation.Cancel(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Reply(ConfigJson());
            });
            await ThrowsAsync<OperationCanceledException>(() => config ? fixture.Client.GetConfigAsync(Session(), cancellation.Token) : fixture.Client.SetTypingAsync(Session(), true, Ticket, cancellation.Token));
            Assert(cancellation.IsCancellationRequested && fixture.Requests.Count == 1, "External cancellation must propagate, rather than become a transient API error or retry.");
        }
        using var cancelledBefore = new CancellationTokenSource(); cancelledBefore.Cancel();
        using var noHttp = new Fixture();
        await ThrowsAsync<OperationCanceledException>(() => noHttp.Client.GetConfigAsync(Session(), cancelledBefore.Token));
        await ThrowsAsync<OperationCanceledException>(() => noHttp.Client.SetTypingAsync(Session(), true, Ticket, cancelledBefore.Token));
        Assert(noHttp.Requests.Count == 0, "Pre-cancelled operations must stop before HTTP.");
    }

    private static async Task InternalTimeoutIsTransientAsync()
    {
        foreach (bool config in new[] { false, true })
        {
            using var fixture = new Fixture(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Reply(ConfigJson()); }, TimeSpan.FromMilliseconds(30));
            var error = await ThrowsAsync<ApiException>(() => config ? fixture.Client.GetConfigAsync(Session()) : fixture.Client.SetTypingAsync(Session(), true, Ticket));
            AssertSafe(error);
            Assert(error.IsTransient && !error.SessionExpired && error.HttpStatus is null && fixture.Requests.Count == 1,
                "A local lightweight timeout is transient, not a fabricated HTTP status or expired binding.");
        }
    }

    private static async Task InvalidParametersFailBeforeHttpAsync()
    {
        using var fixture = new Fixture();
        var changes = new Action<BotSession>[]
        {
            s => s.BotToken = "", s => s.BotToken = "invalid\r\n" + Token, s => s.BotId = " ",
            s => s.UserId = null!, s => s.BaseUrl = "http://ilinkai.weixin.qq.com"
        };
        foreach (var change in changes)
        {
            var invalid = Session(); change(invalid);
            var config = await ThrowsAsync<InvalidOperationException>(() => fixture.Client.GetConfigAsync(invalid)); AssertSafe(config);
            var typing = await ThrowsAsync<InvalidOperationException>(() => fixture.Client.SetTypingAsync(invalid, true, Ticket)); AssertSafe(typing);
        }
        await ThrowsAsync<ArgumentNullException>(() => fixture.Client.GetConfigAsync(null!));
        await ThrowsAsync<ArgumentNullException>(() => fixture.Client.SetTypingAsync(null!, true, Ticket));
        foreach (var ticket in new string?[] { null, "", " " }) await ThrowsAsync<ArgumentException>(() => fixture.Client.SetTypingAsync(Session(), true, ticket!));
        Assert(fixture.Requests.Count == 0, "Invalid sessions and tickets must use zero HTTP.");
        foreach (var timeout in new[] { TimeSpan.FromMilliseconds(-2), TimeSpan.FromMilliseconds((long)uint.MaxValue) })
        {
            using var badTimeout = new Fixture(timeout: timeout);
            await ThrowsAsync<ArgumentOutOfRangeException>(() => badTimeout.Client.GetConfigAsync(Session()));
            await ThrowsAsync<ArgumentOutOfRangeException>(() => badTimeout.Client.SetTypingAsync(Session(), true, Ticket));
            Assert(badTimeout.Requests.Count == 0, "Invalid local timer parameters must fail before request construction/HTTP.");
        }
    }

    private static async Task TransportAndMalformedConfigAreSafeAsync()
    {
        foreach (bool config in new[] { false, true })
        {
            using var fixture = new Fixture((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException(Token + " " + Ticket)));
            var error = await ThrowsAsync<ApiException>(() => config ? fixture.Client.GetConfigAsync(Session()) : fixture.Client.SetTypingAsync(Session(), true, Ticket));
            AssertSafe(error); Assert(error.IsTransient && fixture.Requests.Count == 1, "Transport errors must be safe transient failures without retries.");
        }
        foreach (var json in new[] { "{", "[]", "{\"ret\":\"" + Token + "\"}", "{\"ret\":0,\"typing_ticket\":123}" })
        {
            using var fixture = new Fixture((_, _) => Task.FromResult(Reply(json)));
            var error = await ThrowsAsync<ApiException>(() => fixture.Client.GetConfigAsync(Session())); AssertSafe(error);
            Assert(!error.IsTransient && fixture.Requests.Count == 1, "Malformed config must not be cached or mistaken for a transient approved ticket.");
        }
    }

    private static async Task ConfigCacheIsBoundedAsync()
    {
        using var fixture = new Fixture();
        for (int index = 0; index <= 256; index++)
        {
            var session = Session(); session.UserId = "cache-peer-" + index;
            await fixture.Client.GetConfigAsync(session);
        }
        var first = Session(); first.UserId = "cache-peer-0"; await fixture.Client.GetConfigAsync(first);
        var last = Session(); last.UserId = "cache-peer-256"; await fixture.Client.GetConfigAsync(last);
        Assert(fixture.Requests.Count == 258, "The local cache must be bounded to 256 bindings without losing a recent accepted ticket.");
    }

    private static BotSession Session() => new() { BotToken = Token, BotId = "fixture-bot", UserId = "fixture-peer", ContextToken = "fixture-context" };
    private static string ConfigJson() => JsonSerializer.Serialize(new { ret = 0, typing_ticket = Ticket });
    private static HttpResponseMessage Reply(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private static void AssertSafe(Exception error) => Assert(!error.ToString().Contains(Token, StringComparison.Ordinal) && !error.ToString().Contains(Ticket, StringComparison.Ordinal) && error.InnerException is null,
        "Exceptions must never expose credentials, response tickets or unsafe inner exceptions.");
    private static void AssertHeaders(Request request)
    {
        Assert(request.Headers["iLink-App-Id"] == "bot" && request.Headers["iLink-App-ClientVersion"] == "132105" &&
            request.Headers["AuthorizationType"] == "ilink_bot_token" && request.Headers["Authorization"] == "Bearer " + Token && request.ContentType == "application/json",
            "Typing/config must use the same official authentication and JSON headers as other bot requests.");
        Assert(!request.Headers.ContainsKey("User-Agent"), "No custom User-Agent may be inserted.");
        var uin = Encoding.ASCII.GetString(Convert.FromBase64String(request.Headers["X-WECHAT-UIN"]));
        Assert(uint.TryParse(uin, NumberStyles.None, CultureInfo.InvariantCulture, out _), "X-WECHAT-UIN must be a base64 decimal UInt32.");
    }
    private static void AssertBaseInfo(JsonElement body)
    {
        var info = body.GetProperty("base_info");
        Assert(info.GetProperty("bot_agent").GetString() == "OpenClaw" && info.GetProperty("channel_version").GetString() == ILinkClient.ProtocolVersion && info.EnumerateObject().Count() == 2,
            "The official default base_info must have no additional product marker.");
    }
    private sealed record Request(HttpMethod Method, Uri Uri, string Body, string? ContentType, Dictionary<string, string> Headers);
    private sealed class Handler(Func<Request, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var observed = new Request(request.Method, request.RequestUri!, await request.Content!.ReadAsStringAsync(cancellationToken), request.Content.Headers.ContentType?.MediaType,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase));
            Requests.Add(observed); return await response(observed, cancellationToken);
        }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly Handler handler;
        private readonly HttpClient http;
        public ILinkClient Client { get; }
        public List<Request> Requests => handler.Requests;
        public Fixture(Func<Request, CancellationToken, Task<HttpResponseMessage>>? response = null, TimeSpan? timeout = null)
        {
            handler = new Handler(response ?? ((_, _) => Task.FromResult(Reply(ConfigJson()))));
            http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            Client = new ILinkClient(["typing-fixture.example.invalid"], http) { TypingRequestTimeout = timeout ?? TimeSpan.FromSeconds(10) };
        }
        public void Dispose() { Client.Dispose(); http.Dispose(); }
    }
}
