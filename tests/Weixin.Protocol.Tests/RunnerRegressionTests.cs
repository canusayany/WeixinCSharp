using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Weixin.Protocol;

namespace Weixin.Protocol.Tests;

/// <summary>Fail-closed and protocol-compatibility regressions; never connect to a live endpoint.</summary>
public static class RunnerRegressionTests
{
    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Real Windows DPAPI is required.");
        var tests = new Func<Task>[]
        {
            MalformedBatchNeverCommitsAsync,
            BoundRouteAndFinalStateFilteringAsync,
            CorruptStateFailsBeforeRecoveryAndSendAsync,
            CorruptInboxStopsBeforeApplicationDeliveryAsync,
            CancelledRecoveryPreservesIntentAsync,
            SentSourceChecksTextAndContentHashAsync,
            LocalSendConfigurationFailsBeforeIntentAsync,
            NotificationFailureDoesNotLoseUpdatesAsync,
            OfficialClientIdsRemainDurableAndUniqueAsync,
            TextModelSupportsOptionalAndUnknownItemsAsync
        };
        foreach (var test in tests) await test();
        Console.WriteLine($"RunnerRegressionTests: {tests.Length} test groups passed (offline HTTP + real Windows DPAPI).");
    }

    private static async Task MalformedBatchNeverCommitsAsync()
    {
        var badItem = Message("bad-item"); badItem.Items = [new() { Type = 1, TextItem = new() { Text = "first" } }, null!];
        var filteredBadItem = Message("filtered-bad-item"); filteredBadItem.FromUserId = "other-peer"; filteredBadItem.Items = [null!];
        foreach (var messages in new List<InboundMessage>?[]
        {
            null, [null!], [Message("first-valid"), null!], [null!, Message("last-valid")],
            [Message("first-valid"), badItem], [Message("first-valid"), filteredBadItem]
        })
        {
            using var fixture = new Fixture();
            await fixture.PersistAsync();
            var originalFile = await fixture.FileBytesAsync();
            var originalState = Snapshot(fixture.Runner.State);
            await ThrowsAsync<InvalidDataException>(() => fixture.Runner.StageUpdatesAsync(new()
            {
                Cursor = "must-not-commit", Messages = messages!
            }));
            Assert(Enumerable.SequenceEqual(originalFile, await fixture.FileBytesAsync()), "Malformed batches must preserve the encrypted file byte-for-byte.");
            Assert(Snapshot(fixture.Runner.State) == originalState, "Malformed batches must preserve live cursor, context and inbox.");
            Assert(fixture.Requests.Count == 0, "Batch validation must not use the network.");
        }
        using var nullFixture = new Fixture();
        await nullFixture.PersistAsync();
        await ThrowsAsync<InvalidDataException>(() => nullFixture.Runner.StageUpdatesAsync(null!));
    }

    private static async Task BoundRouteAndFinalStateFilteringAsync()
    {
        using var fixture = new Fixture();
        var missingTo = Message("missing-to"); missingTo.ToUserId = null;
        var emptyTo = Message("empty-to"); emptyTo.ToUserId = " ";
        var wrongTo = Message("wrong-to"); wrongTo.ToUserId = "other-bot";
        var missingFrom = Message("missing-from"); missingFrom.FromUserId = null;
        var unknownStates = new[] { -1, 0, 1, 3, int.MaxValue }.Select(state =>
        {
            var message = Message("state-" + state); message.MessageState = state; return message;
        });
        await fixture.Runner.StageUpdatesAsync(new()
        {
            Cursor = "filtered-cursor", Messages = [missingTo, emptyTo, wrongTo, missingFrom, .. unknownStates]
        });
        Assert(fixture.Runner.State.Inbox.Count == 0 && fixture.Runner.State.Session.ContextToken == "original-context",
            "Unroutable and unknown-state messages must not stage or poison context.");
        Assert(fixture.Runner.State.Session.Cursor == "filtered-cursor", "A valid filtered batch may still advance its cursor.");
        var optionalState = Message("optional-state"); optionalState.MessageState = null; optionalState.Items = null;
        await fixture.Runner.StageUpdatesAsync(new() { Messages = [optionalState] });
        Assert(fixture.Runner.State.Inbox.Single().Message.Text == "" && fixture.Runner.State.Session.ContextToken == optionalState.ContextToken,
            "Official optional message_state/item_list must remain compatible for a bound message.");
        fixture.Runner.ValidateState();
    }

    private static async Task CorruptStateFailsBeforeRecoveryAndSendAsync()
    {
        var corruptions = new (string Name, Action<BotState> Corrupt)[]
        {
            ("version", s => s.FormatVersion = 2),
            ("transport", s => s.TransportMode = "unrecognized"),
            ("null session", s => s.Session = null!),
            ("token", s => s.Session.BotToken = " "),
            ("header token", s => s.Session.BotToken = "invalid\r\nvalue"),
            ("bot", s => s.Session.BotId = null!),
            ("peer", s => s.Session.UserId = ""),
            ("host", s => s.Session.BaseUrl = "http://ilinkai.weixin.qq.com"),
            ("cursor", s => s.Session.Cursor = null!),
            ("null inbox", s => s.Inbox = null!),
            ("null seen", s => s.SeenKeys = null!),
            ("null outbox", s => s.Outbox = null!),
            ("null inbox entry", s => s.Inbox.Add(null!)),
            ("null message", s => s.Inbox[0].Message = null!),
            ("bad inbox key", s => s.Inbox[0].Key = Hash("wrong-key")),
            ("wrong recipient", s => s.Inbox[0].Message.ToUserId = "other-bot"),
            ("unknown inbound state", s => s.Inbox[0].Message.MessageState = 99),
            ("null item", s => s.Inbox[0].Message.Items!.Add(null!)),
            ("missing dedupe key", s => s.SeenKeys.Clear()),
            ("null dedupe key", s => s.SeenKeys.Add(null!)),
            ("duplicate dedupe key", s => s.SeenKeys.Add(s.SeenKeys[0])),
            ("null receipt", s => s.Outbox.Add(null!)),
            ("unknown receipt status", s => s.Outbox[0].Status = "not-a-status"),
            ("null receipt status", s => s.Outbox[0].Status = null!),
            ("missing client ID", s => s.Outbox[0].ClientId = " "),
            ("bad content hash", s => s.Outbox[0].ContentSha256 = "invalid"),
            ("missing content hash", s => s.Outbox[0].ContentSha256 = null!),
            ("bad source key", s => s.Outbox[0].SourceKey = ""),
            ("missing attempt time", s => s.Outbox[0].AttemptedAt = default),
            ("bad last send time", s => s.LastSendAttempt = default(DateTimeOffset)),
            ("bad binding time", s => s.BoundAt = default(DateTimeOffset)),
            ("duplicate receipt ID", s => s.Outbox.Add(Receipt(s.Outbox[0].ClientId, null))),
            ("duplicate receipt source", s => s.Outbox.Add(Receipt("another-attempt", s.Outbox[0].SourceKey)))
        };
        foreach (var (name, corrupt) in corruptions)
        {
            using var fixture = new Fixture();
            await fixture.Runner.StageUpdatesAsync(new() { Messages = [Message("persisted-source")] });
            fixture.Runner.State.Outbox = [Receipt("interrupted", fixture.Runner.State.Inbox[0].Key)];
            corrupt(fixture.Runner.State);
            await fixture.PersistAsync();
            var originalFile = await fixture.FileBytesAsync();
            var originalState = Snapshot(fixture.Runner.State);
            await ThrowsAsync<InvalidDataException>(() => fixture.Runner.RecoverAsync());
            await ThrowsAsync<InvalidDataException>(() => fixture.Runner.SendBoundTextAsync("new text"));
            await ThrowsAsync<InvalidDataException>(() => fixture.Runner.StageUpdatesAsync(new() { Cursor = "no-advance" }));
            Assert(Snapshot(fixture.Runner.State) == originalState, $"{name}: validation must not mutate corrupt live state.");
            Assert(Enumerable.SequenceEqual(originalFile, await fixture.FileBytesAsync()), $"{name}: validation must not rewrite corrupt disk state.");
            Assert(fixture.Requests.Count == 0, $"{name}: validation must stop before HTTP.");
        }
        using var nullState = new Fixture();
        nullState.ReplaceState(null!);
        await nullState.PersistAsync();
        await ThrowsAsync<InvalidDataException>(() => nullState.Runner.RecoverAsync());
        await ThrowsAsync<InvalidDataException>(() => nullState.Runner.SendBoundTextAsync("new text"));
        Assert(nullState.Requests.Count == 0, "A null top-level state must fail closed.");
    }

    private static async Task CorruptInboxStopsBeforeApplicationDeliveryAsync()
    {
        using var fixture = new Fixture();
        fixture.Runner.State.Inbox = [null!];
        await fixture.PersistAsync();
        int handled = 0;
        await ThrowsAsync<InvalidDataException>(() => fixture.Runner.DrainInboxAsync((_, _) => { handled++; return Task.CompletedTask; }));
        await ThrowsAsync<InvalidDataException>(() => fixture.Runner.RunAsync((_, _) => { handled++; return Task.CompletedTask; }));
        Assert(handled == 0 && fixture.Requests.Count == 0, "Corrupt state must reach neither application handlers nor lifecycle HTTP.");
    }

    private static async Task CancelledRecoveryPreservesIntentAsync()
    {
        using var fixture = new Fixture();
        fixture.Runner.State.Outbox = [Receipt("interrupted", null)];
        await fixture.PersistAsync();
        var originalFile = await fixture.FileBytesAsync();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => fixture.Runner.RecoverAsync(cancellation.Token));
        Assert(fixture.Runner.State.Outbox.Single().Status == "Sending" && Enumerable.SequenceEqual(originalFile, await fixture.FileBytesAsync()),
            "A cancelled recovery save must leave both memory and disk at the prior Sending intent.");
        await fixture.Runner.RecoverAsync();
        Assert((await fixture.LoadAsync()).Outbox.Single().Status == "Unknown", "A subsequent recovery must durably resolve the interrupted intent to Unknown.");
    }

    private static async Task SentSourceChecksTextAndContentHashAsync()
    {
        using var fixture = new Fixture();
        await fixture.Runner.StageUpdatesAsync(new() { Messages = [Message("sent-source")] });
        var key = fixture.Runner.State.Inbox.Single().Key;
        var original = await fixture.Runner.SendBoundTextAsync("original text", key);
        await fixture.Runner.DrainInboxAsync((_, _) => Task.CompletedTask);
        var originalFile = await fixture.FileBytesAsync();
        foreach (var invalid in new[] { "", " ", new string('界', 4001) })
            await ThrowsAsync<ArgumentException>(() => fixture.Runner.SendBoundTextAsync(invalid, key));
        await ThrowsAsync<InvalidOperationException>(() => fixture.Runner.SendBoundTextAsync("changed text", key));
        var identical = await fixture.Runner.SendBoundTextAsync("original text", key);
        Assert(identical.ClientId == original.ClientId && fixture.Requests.Count == 1, "Only identical valid text may reuse a Sent receipt, even after source acknowledgment.");
        Assert(Enumerable.SequenceEqual(originalFile, await fixture.FileBytesAsync()), "Duplicate or mismatched text must not rewrite receipt history.");
    }

    private static async Task LocalSendConfigurationFailsBeforeIntentAsync()
    {
        foreach (var configure in new Func<HttpClient, ILinkClient>[]
        {
            http => new(httpClient: http) { RequestTimeout = TimeSpan.FromMilliseconds(-2) },
            http => new(httpClient: http) { RequestTimeout = TimeSpan.FromMilliseconds((long)uint.MaxValue) },
            http => new(httpClient: http) { MaxResponseBytes = 0 }
        })
        {
            using var fixture = new Fixture(configureClient: configure);
            await fixture.PersistAsync();
            var originalFile = await fixture.FileBytesAsync();
            await ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Runner.SendBoundTextAsync("valid text"));
            Assert(fixture.Runner.State.Outbox.Count == 0 && fixture.Runner.State.LastSendAttempt is null && fixture.Requests.Count == 0,
                "Invalid local transport configuration must fail before durable Sending/Unknown intent or HTTP.");
            Assert(Enumerable.SequenceEqual(originalFile, await fixture.FileBytesAsync()), "Local configuration errors must preserve the file.");
        }
        using var negativeInterval = new Fixture(minimumSendInterval: TimeSpan.FromSeconds(-1));
        await ThrowsAsync<ArgumentOutOfRangeException>(() => negativeInterval.Runner.SendBoundTextAsync("valid text"));
        Assert(negativeInterval.Runner.State.Outbox.Count == 0 && negativeInterval.Requests.Count == 0, "A negative interval is also a local configuration error.");
    }

    private static async Task NotificationFailureDoesNotLoseUpdatesAsync()
    {
        foreach (var notification in new[]
        {
            (Json: "{}", Status: HttpStatusCode.OK, ExpectedWarnings: 0),
            (Json: "not-json", Status: HttpStatusCode.OK, ExpectedWarnings: 1),
            (Json: "{}", Status: HttpStatusCode.ServiceUnavailable, ExpectedWarnings: 1)
        })
        {
            using var cancellation = new CancellationTokenSource();
            using var fixture = new Fixture(request => request.Path switch
            {
                "/ilink/bot/msg/notifystart" => Reply(notification.Json, notification.Status),
                "/ilink/bot/getupdates" => Reply(JsonSerializer.Serialize(new Updates { Cursor = "lifecycle-cursor", Messages = [Message("lifecycle-source")] }, ILinkClient.Json)),
                "/ilink/bot/msg/notifystop" => Reply("{}"),
                _ => throw new InvalidOperationException("Unexpected fixture endpoint.")
            });
            var warnings = new List<string>(); fixture.Runner.Diagnostic = warnings.Add;
            int handled = 0;
            await ThrowsAsync<OperationCanceledException>(() => fixture.Runner.RunAsync((_, _) =>
            {
                handled++; cancellation.Cancel(); return Task.CompletedTask;
            }, cancellation.Token));
            Assert(handled == 1 && fixture.Requests.Select(r => r.Path).SequenceEqual(new[]
                { "/ilink/bot/msg/notifystart", "/ilink/bot/getupdates", "/ilink/bot/msg/notifystop" }),
                "Non-expired lifecycle failures must still read, handle and acknowledge messages, then stop cleanly.");
            Assert(warnings.Count == notification.ExpectedWarnings && (await fixture.LoadAsync()).Session.Cursor == "lifecycle-cursor",
                "Only failed optional notifications should warn; staged cursor must survive.");
        }
    }

    private static async Task OfficialClientIdsRemainDurableAndUniqueAsync()
    {
        Fixture? fixture = null;
        fixture = new Fixture(request =>
        {
            var state = fixture!.Vault.LoadAsync<BotState>().GetAwaiter().GetResult()!;
            var pending = state.Outbox.Last();
            using var body = JsonDocument.Parse(request.Body!);
            Assert(pending.Status == "Sending" && pending.ClientId == body.RootElement.GetProperty("msg").GetProperty("client_id").GetString(),
                "Official-format client ID must be the same durable intent and wire value.");
            return Reply("{}");
        });
        using (fixture)
        {
            var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var ids = new List<string>();
            for (int i = 0; i < 20; i++) ids.Add((await fixture.Runner.SendBoundTextAsync("id fixture " + i)).ClientId);
            var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var id in ids)
            {
                var match = Regex.Match(id, @"^openclaw-weixin:([0-9]+)-[0-9a-f]{8}$");
                Assert(match.Success, "client_id must match Tencent generateId timestamp + four random bytes.");
                var timestamp = long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                Assert(timestamp >= before && timestamp <= after, "client_id must contain the current epoch-millisecond timestamp.");
            }
            Assert(ids.Distinct(StringComparer.Ordinal).Count() == ids.Count && (await fixture.LoadAsync()).Outbox.All(r => r.Status == "Sent"),
                "Each send needs a unique durable ID and independent successful receipt.");
        }
    }

    private static Task TextModelSupportsOptionalAndUnknownItemsAsync()
    {
        var optional = JsonSerializer.Deserialize<InboundMessage>("{}", ILinkClient.Json)!;
        Assert(optional.Text == "", "An omitted official item_list is an empty text view.");
        var mixed = JsonSerializer.Deserialize<InboundMessage>("{\"item_list\":[{\"type\":99,\"custom_media\":{\"a\":1}},{\"type\":1,\"text_item\":{\"text\":\"中文 😀\"}},{\"type\":1,\"text_item\":{\"text\":\"line 2\"}}]}", ILinkClient.Json)!;
        Assert(mixed.Text == "中文 😀\nline 2" && mixed.Items![0].OtherFields!.ContainsKey("custom_media"), "Unsupported media and Unicode text must remain intact.");
        var corrupt = JsonSerializer.Deserialize<InboundMessage>("{\"item_list\":[null]}", ILinkClient.Json)!;
        try { _ = corrupt.Text; }
        catch (InvalidDataException) { return Task.CompletedTask; }
        throw new InvalidOperationException("A malformed text view must fail with InvalidDataException, never NullReferenceException.");
    }

    private static InboundMessage Message(string id) => new()
    {
        MessageId = id, FromUserId = "fixture-peer", ToUserId = "fixture-bot", MessageType = 1,
        MessageState = 2, ContextToken = "context-" + id, Items = [new() { Type = 1, TextItem = new() { Text = "test text" } }]
    };
    private static SendReceipt Receipt(string id, string? source) => new()
    {
        ClientId = id, SourceKey = source, ContentSha256 = Hash("test text"), Status = "Sending", AttemptedAt = DateTimeOffset.UtcNow
    };
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Snapshot(BotState state) => JsonSerializer.Serialize(state, ILinkClient.Json);
    private static HttpResponseMessage Reply(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private sealed record Request(string Path, string? Body);
    private sealed class Handler(Func<Request, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var observed = new Request(request.RequestUri!.AbsolutePath,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(observed);
            return response(observed);
        }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "weixin-regression-" + Guid.NewGuid().ToString("N"));
        private readonly Handler handler;
        private readonly HttpClient http;
        private readonly ILinkClient client;
        private readonly TimeSpan interval;
        private string FilePath => Path.Combine(directory, "state.bin");
        public StateVault Vault { get; }
        public PersistentBotRunner Runner { get; private set; }
        public List<Request> Requests => handler.Requests;
        public Fixture(Func<Request, HttpResponseMessage>? response = null, Func<HttpClient, ILinkClient>? configureClient = null,
            TimeSpan? minimumSendInterval = null)
        {
            handler = new Handler(response ?? (_ => Reply("{}")));
            http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client = configureClient?.Invoke(http) ?? new ILinkClient(httpClient: http);
            interval = minimumSendInterval ?? TimeSpan.Zero;
            Vault = new StateVault(FilePath);
            Runner = CreateRunner(new()
            {
                Session = new() { BotToken = "fixture-token", BotId = "fixture-bot", UserId = "fixture-peer", Cursor = "original-cursor", ContextToken = "original-context" }
            });
        }
        private PersistentBotRunner CreateRunner(BotState state) => new(client, Vault, state) { MinimumSendInterval = interval };
        public void ReplaceState(BotState state) => Runner = CreateRunner(state);
        public Task PersistAsync() => Vault.SaveAsync(Runner.State);
        public Task<byte[]> FileBytesAsync() => File.ReadAllBytesAsync(FilePath);
        public async Task<BotState> LoadAsync() => await Vault.LoadAsync<BotState>() ?? throw new InvalidOperationException("Missing fixture state.");
        public void Dispose()
        {
            Vault.Dispose(); client.Dispose(); http.Dispose();
            var fullDirectory = Path.GetFullPath(directory);
            var allowedRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullDirectory.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(fullDirectory).StartsWith("weixin-regression-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected fixture cleanup directory.");
            if (Directory.Exists(fullDirectory)) Directory.Delete(fullDirectory, recursive: true);
        }
    }
}
