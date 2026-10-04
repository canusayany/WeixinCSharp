using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Weixin.Protocol;

namespace Weixin.Protocol.Tests;

/// <summary>Durability tests use actual Windows DPAPI files and offline HTTP fixtures.</summary>
public static class PersistentBotRunnerTests
{
    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Persistent runner tests require real Windows DPAPI.");
        int passed = 0;
        await CheckAsync(StageUpdatesSurviveReopenAsync);
        await CheckAsync(DuplicateRefreshesBoundContextAsync);
        await CheckAsync(FilteringAndEmptyCursorAsync);
        await CheckAsync(FailedHandlerReplaysAfterReopenAsync);
        await CheckAsync(CompletedHandlerAcknowledgesCancellationAsync);
        await CheckAsync(SendingIntentPrecedesNetworkAsync);
        await CheckAsync(UnknownAttemptNeverResendsAsync);
        await CheckAsync(RecoveryMarksSendingUnknownAsync);
        await CheckAsync(EachReplyUsesItsSourceContextAsync);
        await CheckAsync(ExpiredSessionStopsWithoutStopNotificationAsync);
        await CheckAsync(UnresolvedOutboxCannotBeTruncatedAsync);
        await CheckAsync(SentSourceIsIdempotentAsync);
        Console.WriteLine($"PersistentBotRunnerTests: {passed} test groups passed (real Windows DPAPI).");

        async Task CheckAsync(Func<Task> test)
        {
            await test();
            passed++;
        }
    }

    private static async Task StageUpdatesSurviveReopenAsync()
    {
        using var fixture = new Fixture();
        var message = Message("durable-message", "durable-context");
        await fixture.Runner.StageUpdatesAsync(new Updates { Cursor = "cursor-after", Messages = [message] });
        var disk = await fixture.LoadAsync();
        Assert(disk.Session.Cursor == "cursor-after" && disk.Inbox.Count == 1, "Cursor and inbox must be written together before returning.");
        Assert(disk.Inbox[0].Key == PersistentBotRunner.MessageKey(message) && disk.Inbox[0].Message.Text == "fixture text", "Persisted inbox must retain its key and content.");
        await fixture.ReopenAsync();
        Assert(fixture.Runner.State.Session.Cursor == "cursor-after" && fixture.Runner.State.Inbox.Count == 1
            && fixture.Runner.State.Session.ContextToken == "durable-context", "Reopened vault must recover cursor, inbox and bound context.");
        Assert(fixture.Requests.Count == 0, "Staging must not send network requests.");
    }

    private static async Task DuplicateRefreshesBoundContextAsync()
    {
        using var fixture = new Fixture();
        await fixture.Runner.StageUpdatesAsync(new Updates { Cursor = "cursor-one", Messages = [Message("same-id", "context-one")] });
        await fixture.Runner.StageUpdatesAsync(new Updates { Cursor = "cursor-two", Messages = [Message("same-id", "context-two")] });
        var state = await fixture.LoadAsync();
        Assert(state.Inbox.Count == 1 && state.SeenKeys.Count == 1, "A duplicate message ID must create only one inbox entry.");
        Assert(state.Session.ContextToken == "context-two" && state.Session.Cursor == "cursor-two", "A bound duplicate must still refresh the latest session context and cursor.");
        Assert(state.Inbox[0].Message.ContextToken == "context-one", "The staged message must retain its own original reply context.");
    }

    private static async Task FilteringAndEmptyCursorAsync()
    {
        using var fixture = new Fixture();
        await fixture.Runner.StageUpdatesAsync(new Updates { Cursor = "known-cursor", Messages = [Message("valid", "known-context")] });
        var unbound = Message("unbound", "unbound-context"); unbound.FromUserId = "another-peer";
        var group = Message("group", "group-context"); group.GroupId = "fixture-group";
        var bot = Message("bot", "bot-context"); bot.MessageType = 2;
        var incomplete = Message("state-zero", "state-zero-context"); incomplete.MessageState = 0;
        var inProgress = Message("state-one", "state-one-context"); inProgress.MessageState = 1;
        await fixture.Runner.StageUpdatesAsync(new Updates { Cursor = "", Messages = [unbound, group, bot, incomplete, inProgress] });
        await fixture.Runner.StageUpdatesAsync(new Updates { Cursor = null, Messages = [] });
        var state = await fixture.LoadAsync();
        Assert(state.Inbox.Count == 1 && state.SeenKeys.Count == 1, "Unbound, group, bot and incomplete messages must be filtered.");
        Assert(state.Session.ContextToken == "known-context", "Filtered messages must not replace the bound context.");
        Assert(state.Session.Cursor == "known-cursor", "Empty and absent cursors must not reset a saved cursor.");
    }

    private static async Task FailedHandlerReplaysAfterReopenAsync()
    {
        using var fixture = new Fixture();
        await fixture.Runner.StageUpdatesAsync(new Updates { Messages = [Message("side-effect", "context")] });
        int sideEffects = 0;
        string key = fixture.Runner.State.Inbox.Single().Key;
        await ThrowsAsync<InvalidOperationException>(() => fixture.Runner.DrainInboxAsync((_, _) =>
        {
            sideEffects++;
            throw new InvalidOperationException("fixture application failed after a side effect");
        }));
        Assert((await fixture.LoadAsync()).Inbox.Count == 1 && fixture.Runner.State.Inbox.Count == 1, "A failed handler must leave its entry pending in memory and on disk.");
        await fixture.ReopenAsync();
        await fixture.Runner.DrainInboxAsync((entry, _) =>
        {
            Assert(entry.Key == key, "Replay must use the same idempotency key.");
            sideEffects++;
            return Task.CompletedTask;
        });
        Assert(sideEffects == 2, "A side effect before handler failure is delivered at least once and can occur again on replay.");
        var state = await fixture.LoadAsync();
        Assert(state.Inbox.Count == 0 && state.SeenKeys.Contains(key), "Only completed handlers must be acknowledged, retaining the deduplication key.");
        await fixture.Runner.StageUpdatesAsync(new Updates { Messages = [Message("side-effect", "new-context")] });
        Assert(fixture.Runner.State.Inbox.Count == 0, "An acknowledged duplicate must not be delivered again.");
    }

    private static async Task CompletedHandlerAcknowledgesCancellationAsync()
    {
        using var fixture = new Fixture();
        await fixture.Runner.StageUpdatesAsync(new Updates { Messages = [Message("cancel-after-completion", "context")] });
        using var cancellation = new CancellationTokenSource();
        await fixture.Runner.DrainInboxAsync((_, _) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        }, cancellation.Token);
        Assert((await fixture.LoadAsync()).Inbox.Count == 0, "A completed handler must be durably acknowledged even if cancellation arrives just afterwards.");
    }

    private static async Task SendingIntentPrecedesNetworkAsync()
    {
        Fixture? fixture = null;
        bool observedSending = false;
        fixture = new Fixture(async (request, _) =>
        {
            var disk = await fixture!.LoadAsync();
            var receipt = disk.Outbox.Single();
            using var body = JsonDocument.Parse(request.Body!);
            Assert(request.Path == "/ilink/bot/sendmessage" && receipt.Status == "Sending", "Sending intent must already be durable when HTTP starts.");
            Assert(body.RootElement.GetProperty("msg").GetProperty("client_id").GetString() == receipt.ClientId, "Durable intent and wire send must share client_id.");
            Assert(receipt.ContentSha256 == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("intent text"))), "Intent must retain the content hash.");
            Assert(disk.LastSendAttempt == receipt.AttemptedAt, "Durable intent must include send timing.");
            observedSending = true;
            return Reply("{\"ret\":0}");
        });
        using (fixture)
        {
            var receipt = await fixture.Runner.SendBoundTextAsync("intent text");
            Assert(observedSending && receipt.Status == "Sent", "A confirmed send must transition from durable Sending to Sent.");
            Assert((await fixture.LoadAsync()).Outbox.Single().Status == "Sent", "Sent acknowledgment must be persisted.");
        }
    }

    private static async Task UnknownAttemptNeverResendsAsync()
    {
        using var fixture = new Fixture((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("fixture connection loss")));
        await fixture.Runner.StageUpdatesAsync(new Updates { Messages = [Message("unknown-source", "source-context")] });
        string key = fixture.Runner.State.Inbox.Single().Key;
        await ThrowsAsync<DeliveryUnknownException>(() => fixture.Runner.SendBoundTextAsync("reply", key));
        Assert((await fixture.LoadAsync()).Outbox.Single().Status == "Unknown", "Uncertain network delivery must be persisted as Unknown.");
        Assert(fixture.Requests.Count == 1, "An uncertain attempt must send once.");
        await fixture.ReopenAsync();
        await fixture.Runner.RecoverAsync();
        await ThrowsAsync<InvalidOperationException>(() => fixture.Runner.SendBoundTextAsync("reply", key));
        Assert(fixture.Requests.Count == 1 && fixture.Runner.State.Outbox.Single().Status == "Unknown", "The same source key must never be retransmitted after reopening an Unknown attempt.");
    }

    private static async Task RecoveryMarksSendingUnknownAsync()
    {
        using var fixture = new Fixture();
        fixture.Runner.State.Outbox = [Receipt("crashed", "Sending"), Receipt("confirmed", "Sent"), Receipt("rejected", "Rejected"), Receipt("uncertain", "Unknown")];
        await fixture.PersistAsync();
        await fixture.ReopenAsync();
        await fixture.Runner.RecoverAsync();
        var disk = await fixture.LoadAsync();
        Assert(disk.Outbox.Select(r => r.Status).SequenceEqual(new[] { "Unknown", "Sent", "Rejected", "Unknown" }), "Recovery must change only interrupted Sending attempts to Unknown.");
        Assert(fixture.Requests.Count == 0, "Recovery must not retransmit interrupted sends.");
    }

    private static async Task EachReplyUsesItsSourceContextAsync()
    {
        using var fixture = new Fixture();
        await fixture.Runner.StageUpdatesAsync(new Updates { Messages = [Message("source-a", "context-A"), Message("source-b", "context-B")] });
        Assert(fixture.Runner.State.Session.ContextToken == "context-B", "The latest session context must be B before draining the batch.");
        await fixture.Runner.DrainInboxAsync(async (entry, ct) =>
        {
            await fixture.Runner.SendBoundTextAsync("reply", entry.Key, ct);
        });
        var contexts = fixture.Requests.Select(request =>
        {
            using var body = JsonDocument.Parse(request.Body!);
            var msg = body.RootElement.GetProperty("msg");
            Assert(msg.GetProperty("to_user_id").GetString() == "fixture-bound-peer", "Each reply must target the bound peer.");
            return msg.GetProperty("context_token").GetString();
        }).ToArray();
        Assert(contexts.SequenceEqual(new[] { "context-A", "context-B" }), "Each reply must use its source message token rather than the latest batch token.");
        var disk = await fixture.LoadAsync();
        Assert(disk.Inbox.Count == 0 && disk.Outbox.Count == 2 && disk.Outbox.All(r => r.Status == "Sent"), "Both replies and handler acknowledgments must persist.");
    }

    private static async Task ExpiredSessionStopsWithoutStopNotificationAsync()
    {
        foreach (bool expireAtStart in new[] { false, true })
        foreach (var failure in new[]
        {
            (Json: "{\"ret\":0}", Status: HttpStatusCode.Unauthorized),
            (Json: "{\"ret\":-14}", Status: HttpStatusCode.OK),
            (Json: "{\"ret\":0,\"errcode\":-14}", Status: HttpStatusCode.OK)
        })
        {
            using var fixture = new Fixture((request, _) =>
            {
                if (request.Path == "/ilink/bot/msg/notifystart")
                    return Task.FromResult(expireAtStart ? Reply(failure.Json, failure.Status) : Reply("{\"ret\":0}"));
                if (!expireAtStart && request.Path == "/ilink/bot/getupdates")
                    return Task.FromResult(Reply(failure.Json, failure.Status));
                throw new InvalidOperationException("Expired binding must not make additional requests.");
            });
            var error = await ThrowsAsync<ApiException>(() => fixture.Runner.RunAsync((_, _) => Task.CompletedTask));
            Assert(error.SessionExpired && !error.IsTransient, "Expired binding must stop the runner as a nontransient failure.");
            Assert(fixture.Requests.Count == (expireAtStart ? 1 : 2), "Expired binding must stop after the rejection without notifyStop or retry.");
            Assert(fixture.Requests.All(r => r.Path != "/ilink/bot/msg/notifystop"), "An expired token must never be used for notifyStop.");
        }
    }

    private static async Task UnresolvedOutboxCannotBeTruncatedAsync()
    {
        using var fixture = new Fixture();
        fixture.Runner.State.Outbox = Enumerable.Range(0, 128).Select(i => Receipt("unresolved-" + i, "Unknown")).ToList();
        await fixture.PersistAsync();
        var original = fixture.Runner.State.Outbox.Select(r => r.ClientId).ToArray();
        await ThrowsAsync<InvalidOperationException>(() => fixture.Runner.SendBoundTextAsync("new reply"));
        var disk = await fixture.LoadAsync();
        Assert(fixture.Requests.Count == 0, "A full unresolved outbox must reject a new send before network I/O.");
        Assert(disk.Outbox.Count == 128 && disk.Outbox.All(r => r.Status == "Unknown")
            && disk.Outbox.Select(r => r.ClientId).SequenceEqual(original), "No unresolved receipt may be truncated or replaced.");
        Assert(fixture.Runner.State.Outbox.Select(r => r.ClientId).SequenceEqual(original) && disk.LastSendAttempt is null, "Capacity rejection must not mutate live receipts or record an attempted send.");
    }

    private static async Task SentSourceIsIdempotentAsync()
    {
        using var fixture = new Fixture();
        await fixture.Runner.StageUpdatesAsync(new Updates { Messages = [Message("confirmed-source", "source-context")] });
        string key = fixture.Runner.State.Inbox.Single().Key;
        var original = await fixture.Runner.SendBoundTextAsync("reply", key);
        await fixture.ReopenAsync();
        var existing = await fixture.Runner.SendBoundTextAsync("reply", key);
        Assert(fixture.Requests.Count == 1 && existing.Status == "Sent" && existing.ClientId == original.ClientId, "A confirmed source key must return the existing receipt without another send.");
    }

    private static InboundMessage Message(string id, string context) => new()
    {
        MessageId = id, FromUserId = "fixture-bound-peer", ToUserId = "fixture-bot-id",
        MessageType = 1, MessageState = 2, ContextToken = context,
        Items = [new MessageItem { Type = 1, TextItem = new TextItem { Text = "fixture text" } }]
    };

    private static SendReceipt Receipt(string id, string status) => new()
    {
        ClientId = id, Status = status, AttemptedAt = DateTimeOffset.UtcNow,
        ContentSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("fixture text"))),
        SourceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("fixture-key-" + id)))
    };

    private static HttpResponseMessage Reply(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed record ObservedRequest(string Path, string? Body);

    private sealed class RecordingHandler(Func<ObservedRequest, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public List<ObservedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var captured = new ObservedRequest(request.RequestUri!.AbsolutePath,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(captured);
            return await response(captured, cancellationToken);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "weixin-runner-tests-" + Guid.NewGuid().ToString("N"));
        private readonly RecordingHandler handler;
        private readonly HttpClient http;
        private readonly ILinkClient client;
        private string Path => System.IO.Path.Combine(directory, "state.bin");
        public StateVault Vault { get; private set; }
        public PersistentBotRunner Runner { get; private set; }
        public List<ObservedRequest> Requests => handler.Requests;

        public Fixture(Func<ObservedRequest, CancellationToken, Task<HttpResponseMessage>>? response = null)
        {
            handler = new RecordingHandler(response ?? ((_, _) => Task.FromResult(Reply("{\"ret\":0}"))));
            http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client = new ILinkClient(httpClient: http);
            Vault = new StateVault(Path);
            Runner = CreateRunner(new BotState
            {
                Session = new BotSession
                {
                    BotToken = "fixture-bot-token", BotId = "fixture-bot-id", UserId = "fixture-bound-peer",
                    Cursor = "fixture-initial-cursor", ContextToken = "fixture-initial-context"
                }
            });
        }

        public Task PersistAsync() => Vault.SaveAsync(Runner.State);
        public async Task<BotState> LoadAsync() => await Vault.LoadAsync<BotState>()
            ?? throw new InvalidOperationException("Expected a durable test state.");

        public async Task ReopenAsync()
        {
            Vault.Dispose();
            Vault = new StateVault(Path);
            Runner = CreateRunner(await LoadAsync());
        }

        private PersistentBotRunner CreateRunner(BotState state) => new(client, Vault, state)
        {
            MinimumSendInterval = TimeSpan.Zero
        };

        public void Dispose()
        {
            Vault.Dispose();
            client.Dispose();
            http.Dispose();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
