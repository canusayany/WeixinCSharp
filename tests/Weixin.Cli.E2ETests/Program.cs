using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Weixin.Protocol;

return await CliSuite.RunAsync(args);

internal static partial class CliSuite
{
    private static readonly JsonSerializerOptions Json = new(ILinkClient.Json) { WriteIndented = true };
    private static string exe = "";
    private static string output = "";
    private static string? selectedCase;
    private static readonly List<string> AvailableCases = [];
    private static readonly List<CaseResult> Results = [];
    private static readonly List<ProcessEvidence> Processes = [];
    private const string Token = "fixture-secret-bot-token";
    private const string User = "fixture-bound-owner";
    private const string Bot = "fixture-bound-bot";
    private const string Context = "fixture-saved-context";

    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Length is not (4 or 6) || args[0] != "--exe" || args[2] != "--output" || args.Length == 6 && args[4] != "--case")
                throw new ArgumentException("Usage: --exe <published weixin.exe> --output <new report directory> [--case <exact-case-name>]");
            selectedCase = args.Length == 6 ? args[5] : null;
            exe = Path.GetFullPath(args[1]);
            output = Path.GetFullPath(args[3]);
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The process E2E suite uses real Windows DPAPI.");
            if (!File.Exists(exe) || Path.GetExtension(exe) != ".exe") throw new ArgumentException("A published executable is required.");
            Directory.CreateDirectory(output);
            if (File.Exists(Path.Combine(output, "tests-summary.json"))) throw new ArgumentException("Use a new output directory for each run.");
            await Case("published_help_and_unknown_parameter_exit", HelpAndArgumentsAsync);
            await Case("login_need_verifycode_wait_scaned_confirmed_cleanup", LoginAsync);
            await Case("login_unknown_status_cleanup_no_binding", LoginUnknownAsync);
            await Case("status_local_state_and_send_without_context", StatusAndMissingContextAsync);
            await Case("listen_echo_filters_restart_dedup_and_client_id", EchoAndRestartAsync);
            await Case("stdin_send_and_exact_client_id_persistence", StdinSendAsync);
            await Case("utf8_file_send_preserves_newline", FileSendAsync);
            await Case("disconnect_unknown_echo_never_retried_on_restart", UnknownDisconnectAsync);
            await Case("malformed_ack_unknown_send_no_resend", InvalidAckAsync);
            await Case("kill_during_send_durable_sending_recover_unknown", KillDuringSendAsync);
            await Case("exclusive_state_lock_across_processes", ExclusiveLockAsync);
            await Case("session_minus14_stops_without_notifystop", ExpiredSessionAsync);
            await Case("run_for_cancels_poll_and_confirms_notifystop", RunForAsync);
            await Case("exhausted_fixture_fails_closed_without_network_fallback", ExhaustedFixtureAsync);
            await Case("offline_transport_refuses_non_fixture_saved_credentials", RealCredentialGuardAsync);
            await Case("offline_paths_must_share_marked_uuid_test_directory", PathIsolationAsync);
            await Case("offline_fixture_refuses_non_fixture_response_credentials", ResponseCredentialGuardAsync);
            await Case("cli_exit_codes_unbound_bad_state_http_rejected", OtherExitCodesAsync);
            await Case("invalid_update_batch_never_commits_cursor_or_context", InvalidUpdateBatchAsync);
            await Case("invalid_saved_pending_message_stops_before_transport", InvalidSavedInboxAsync);
            await Case("run_for_during_send_records_unknown_then_notifystop", CancellationDuringSendAsync);
            await MediaCasesAsync();
            await TypingVoiceCasesAsync();
            await TypingLifecycleCasesAsync();
            if (Results.Count == 0) throw new ArgumentException("The requested --case does not exist.");
            var provenance = await ProvenanceAsync();
            var summary = new
            {
                schemaVersion = 1, scope = "offline-published-cli-process-e2e", realWeChatDeliveryVerified = false,
                selectedCase, availableSuiteCaseCount = AvailableCases.Count,
                completedUtc = DateTimeOffset.UtcNow, executable = exe, provenance,
                total = Results.Count, passed = Results.Count(x => x.Passed), failed = Results.Count(x => !x.Passed),
                cases = Results, processes = Processes,
                isolation = new { testDirectories = Directory.GetDirectories(output, "fixture-*").Select(Path.GetFileName),
                    defaultAccountStateRead = false, socketsCreatedByFixtureTransport = false,
                    credentials = "fixture-prefixed only; one negative guard fixture is not a real account token" }
            };
            await File.WriteAllTextAsync(Path.Combine(output, "tests-summary.json"), JsonSerializer.Serialize(summary, Json), new UTF8Encoding(false));
            Console.WriteLine($"CLI PROCESS E2E: {Results.Count(x => x.Passed)}/{Results.Count} passed; {Path.Combine(output, "tests-summary.json")}");
            return Results.All(x => x.Passed) ? 0 : 1;
        }
        catch (Exception ex)
        { Console.Error.WriteLine(ex.Message); return 1; }
    }

    private static async Task Case(string name, Func<Task> action)
    {
        AvailableCases.Add(name);
        if (selectedCase is not null && selectedCase != name) return;
        var start = Stopwatch.StartNew();
        var processIndex = Processes.Count;
        try
        {
            await action();
            Results.Add(new(name, true, start.ElapsedMilliseconds, null, Processes.Skip(processIndex).Select(p => p.Id).ToArray()));
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            Results.Add(new(name, false, start.ElapsedMilliseconds, ex.GetType().Name + ": " + ex.Message,
                Processes.Skip(processIndex).Select(p => p.Id).ToArray()));
            Console.WriteLine("FAIL " + name + ": " + ex.Message);
        }
    }

    private static async Task HelpAndArgumentsAsync()
    {
        var t = await TestDirectory.CreateAsync([]);
        using var child = Start(t, "help", [], includeFixture: false);
        var help = await child.CompleteAsync(TimeSpan.FromSeconds(10));
        Assert(help.ExitCode == 0, "Published help exited unsuccessfully.");
        Assert(help.Stdout.Contains("微信助理", StringComparison.Ordinal), "Published help did not display.");
        await RunAsync(t, "status", ["--unknown-option"], 1);
        Assert(!File.Exists(t.State), "Argument rejection created a state.");
        Assert(t.Requests().Count == 0, "Help/unknown arguments made an API request.");
    }

    private static async Task LoginAsync()
    {
        var verification = StatusStep("need_verifycode");
        verification.DelayMs = 250; verification.WaitMarker = "qr-generated.marker";
        var t = await TestDirectory.CreateAsync([
            QrStep(), verification,
            StatusStep("wait", "fixture-4242"), StatusStep("scaned", "fixture-4242"),
            StatusStep("confirmed", response: new { status = "confirmed", bot_token = Token,
                ilink_bot_id = Bot, ilink_user_id = User, baseurl = ILinkClient.DefaultBaseUrl })
        ]);
        var png = Path.Combine(t.Directory, "login-qr.png");
        ProcessEvidence result;
        using (var child = Start(t, "login", ["--qr-png", png], "fixture-4242\n"))
        {
            await WaitFileAsync(Path.Combine(t.Directory, "qr-generated.marker"), child, TimeSpan.FromSeconds(8));
            Assert(File.Exists(png) && (await File.ReadAllBytesAsync(png)).AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                "login did not produce a valid local QR PNG before polling.");
            Assert(File.ReadAllText(t.State + ".login-qr.html", Encoding.UTF8).Contains("data:image/png;base64,", StringComparison.Ordinal),
                "login did not produce the local QR HTML before polling.");
            result = await child.CompleteAsync(TimeSpan.FromSeconds(25));
            Assert(result.ExitCode == 0, "Confirmed login did not exit successfully.");
        }
        var state = await t.LoadAsync();
        Assert(state.Session.BotToken == Token && state.Session.UserId == User && state.Session.BotId == Bot, "Confirmed binding was not saved exactly.");
        Assert(state.BoundAt is not null && state.Session.ContextToken is null, "Binding metadata or initial context differs.");
        Assert(!result.Stdout.Contains(Token, StringComparison.Ordinal) && !result.Stdout.Contains("fixture-4242", StringComparison.Ordinal), "A credential was echoed to stdout.");
        Assert(!result.Stderr.Contains(Token, StringComparison.Ordinal) && !result.Stderr.Contains("fixture-4242", StringComparison.Ordinal), "A credential was echoed to stderr.");
        Assert(!File.Exists(png) && !File.Exists(t.State + ".login-qr.html"), "Successful login left QR artifacts.");
        t.AssertRequests("get_bot_qrcode", "get_qrcode_status", "get_qrcode_status", "get_qrcode_status", "get_qrcode_status");
        Assert(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(t.State)).Contains(Token, StringComparison.Ordinal), "DPAPI state contains plaintext token.");
    }

    private static async Task LoginUnknownAsync()
    {
        var t = await TestDirectory.CreateAsync([QrStep(), StatusStep("fixture-unknown-state")]);
        var png = Path.Combine(t.Directory, "login-qr.png");
        await RunAsync(t, "login", ["--qr-png", png], 1);
        Assert(!File.Exists(t.State), "Unknown QR status created a binding.");
        Assert(!File.Exists(png) && !File.Exists(t.State + ".login-qr.html"), "Failed login left QR artifacts.");
        t.AssertRequests("get_bot_qrcode", "get_qrcode_status");
    }

    private static async Task StatusAndMissingContextAsync()
    {
        var t = await TestDirectory.CreateAsync([]);
        await t.SaveAsync(State(null));
        var status = await RunAsync(t, "status", [], 0);
        Assert(status.Stdout.Contains("未取得", StringComparison.Ordinal), "Status did not show missing context.");
        await RunAsync(t, "send", [], 1, "fixture-no-context\n");
        Assert((await t.LoadAsync()).Outbox.Count == 0, "Context rejection recorded a send attempt.");
        Assert(t.Requests().Count == 0, "Local status/context rejection called the network.");
    }

    private static async Task EchoAndRestartAsync()
    {
        var valid = Message("fixture-msg-valid", "fixture-hello中文", "fixture-source-context");
        var media = Message("fixture-msg-media", "", "fixture-media-context");
        media.Items = [new MessageItem { Type = 2 }];
        var wrongOwner = Message("fixture-msg-other", "fixture-other", "fixture-other-context"); wrongOwner.FromUserId = "fixture-other-owner";
        var group = Message("fixture-msg-group", "fixture-group", "fixture-group-context"); group.GroupId = "fixture-group";
        var botMessage = Message("fixture-msg-bot", "fixture-bot", "fixture-bot-context"); botMessage.MessageType = 2;
        var partial = Message("fixture-msg-partial", "fixture-partial", "fixture-partial-context"); partial.MessageState = 1;
        var steps = new List<Step> { Notify(true), Updates([wrongOwner, group, botMessage, partial, valid, media], "fixture-cursor-1"),
            Send("已收到：fixture-hello中文", "fixture-source-context"), LongPoll(), Notify(false) };
        var t = await TestDirectory.CreateAsync(steps);
        await t.SaveAsync(State(null));
        var first = await RunAsync(t, "listen", ["--echo", "--run-for", "2"], 0);
        var state = await t.LoadAsync();
        Assert(state.Outbox.Count == 1 && state.Outbox[0].Status == "Sent", "Filtered echo count differs.");
        Assert(state.Inbox.Count == 0 && state.SeenKeys.Count == 2 && state.Session.Cursor == "fixture-cursor-1", "Inbox/cursor/dedup was not durably recorded.");
        Assert(state.Session.ContextToken == "fixture-media-context", "Non-owner context filtering failed.");
        Assert(!first.Stdout.Contains("fixture-other", StringComparison.Ordinal) && !first.Stdout.Contains("fixture-group", StringComparison.Ordinal), "Filtered input reached CLI handler.");
        var send = t.Requests().Single(x => Endpoint(x) == "sendmessage");
        var clientId = send.GetProperty("body").GetProperty("msg").GetProperty("client_id").GetString();
        Assert(clientId == state.Outbox[0].ClientId && !string.IsNullOrWhiteSpace(clientId), "Wire client_id differs from durable receipt.");
        var before = state.Outbox[0].ClientId;
        await t.RewriteAsync([Notify(true), Updates([valid, media], "fixture-cursor-2"), LongPoll(), Notify(false)]);
        await RunAsync(t, "listen", ["--echo", "--run-for", "1.5"], 0);
        state = await t.LoadAsync();
        Assert(state.Outbox.Count == 1 && state.Outbox[0].ClientId == before && state.Session.Cursor == "fixture-cursor-2", "Restart duplicated an echo or missed cursor update.");
        Assert(t.Requests().Count(x => Endpoint(x) == "sendmessage") == 1, "Restart resent a duplicate inbound message.");
        t.AssertNoRejections();
    }

    private static async Task StdinSendAsync()
    {
        var t = await TestDirectory.CreateAsync([Send("fixture-stdin输入")]);
        await t.SaveAsync(State());
        await RunAsync(t, "send", [], 0, "fixture-stdin输入\n");
        await CheckSentAsync(t, "fixture-stdin输入");
        t.AssertRequests("sendmessage");
    }

    private static async Task FileSendAsync()
    {
        const string text = "fixture-第一行\nfixture-第二行😀";
        var t = await TestDirectory.CreateAsync([Send(text)]);
        await t.SaveAsync(State());
        var file = Path.Combine(t.Directory, "fixture-message.txt");
        await File.WriteAllTextAsync(file, text, new UTF8Encoding(false));
        await RunAsync(t, "send", ["--text-file", file], 0);
        await CheckSentAsync(t, text);
        t.AssertRequests("sendmessage");
    }

    private static async Task UnknownDisconnectAsync()
    {
        var message = Message("fixture-disconnect-inbound", "fixture-disconnect", Context);
        var t = await TestDirectory.CreateAsync([Send("已收到：fixture-disconnect", fault: "disconnect")]);
        var state = State(); var entry = Entry(message); state.Inbox.Add(entry); state.SeenKeys.Add(entry.Key);
        await t.SaveAsync(state);
        await RunAsync(t, "listen", ["--echo", "--run-for", "1"], 3);
        state = await t.LoadAsync();
        Assert(state.Outbox.Count == 1 && state.Outbox[0].Status == "Unknown" && state.Inbox.Count == 1, "Disconnect did not preserve unknown receipt/inbox.");
        var id = state.Outbox[0].ClientId;
        await t.RewriteAsync([]);
        await RunAsync(t, "listen", ["--echo", "--run-for", "1"], 1);
        state = await t.LoadAsync();
        Assert(state.Outbox.Count == 1 && state.Outbox[0].ClientId == id && state.Outbox[0].Status == "Unknown", "Unknown retry created another attempt.");
        t.AssertRequests("sendmessage");
    }

    private static async Task InvalidAckAsync()
    {
        var step = Send("fixture-invalid-ack"); step.Body = null; step.RawBody = "{";
        var t = await TestDirectory.CreateAsync([step]);
        await t.SaveAsync(State());
        await RunAsync(t, "send", [], 3, "fixture-invalid-ack\n");
        Assert((await t.LoadAsync()).Outbox.Single().Status == "Unknown", "Malformed ack was not classified Unknown.");
        await t.RewriteAsync([]);
        await RunAsync(t, "status", [], 0);
        Assert((await t.LoadAsync()).Outbox.Single().Status == "Unknown", "Status altered unknown receipt.");
        t.AssertRequests("sendmessage");
    }

    private static async Task KillDuringSendAsync()
    {
        var message = Message("fixture-kill-inbound", "fixture-kill", "fixture-kill-context");
        var send = Send("已收到：fixture-kill", "fixture-kill-context"); send.DelayMs = -1; send.WaitMarker = "send-entered.marker";
        var t = await TestDirectory.CreateAsync([Notify(true), Updates([message], "fixture-kill-cursor"), send]);
        await t.SaveAsync(State(null));
        using (var child = Start(t, "listen", ["--echo"]))
        {
            await WaitFileAsync(Path.Combine(t.Directory, "send-entered.marker"), child, TimeSpan.FromSeconds(8));
            var snapshotPath = Path.Combine(t.Directory, "fixture-snapshot.dpapi");
            File.Copy(t.State, snapshotPath, overwrite: false);
            using (var snapshotVault = new StateVault(snapshotPath))
            {
                var snapshot = await snapshotVault.LoadAsync<BotState>() ?? throw new Exception("Missing crash snapshot.");
                Assert(snapshot.Outbox.Count == 1 && snapshot.Outbox[0].Status == "Sending", "Intent was not durably Sending before transport began.");
                Assert(snapshot.Inbox.Count == 1 && snapshot.Session.Cursor == "fixture-kill-cursor", "Input/cursor was not durable before send.");
            }
            await child.KillAsync();
        }
        var state = await t.LoadAsync();
        Assert(state.Outbox.Single().Status == "Sending", "Kill window was not reached while Sending.");
        var id = state.Outbox[0].ClientId;
        await t.RewriteAsync([]);
        var status = await RunAsync(t, "status", [], 0);
        state = await t.LoadAsync();
        Assert(state.Outbox.Single().Status == "Unknown" && state.Outbox[0].ClientId == id, "Restart failed to recover Sending as Unknown.");
        Assert(status.Stdout.Contains("Unknown", StringComparison.Ordinal) && !status.Stdout.Contains("Sending", StringComparison.Ordinal),
            "Status reported stale Sending after recovery persisted Unknown.");
        await RunAsync(t, "listen", ["--echo", "--run-for", "1"], 1);
        Assert((await t.LoadAsync()).Outbox.Count == 1, "Restart repeated a killed send.");
        t.AssertRequests("notifystart", "getupdates", "sendmessage");
    }

    private static async Task ExclusiveLockAsync()
    {
        var poll = LongPoll(); poll.WaitMarker = "lock-held.marker";
        var t = await TestDirectory.CreateAsync([Notify(true), poll]);
        await t.SaveAsync(State());
        using (var child = Start(t, "listen", []))
        {
            await WaitFileAsync(Path.Combine(t.Directory, "lock-held.marker"), child, TimeSpan.FromSeconds(8));
            await RunAsync(t, "status", [], 4);
            await child.KillAsync();
        }
        Assert((await t.LoadAsync()).Session.UserId == User, "Exclusive lock test damaged state.");
        t.AssertRequests("notifystart", "getupdates");
    }

    private static async Task ExpiredSessionAsync()
    {
        var updates = Updates([], "fixture-unused-cursor"); updates.Body = new { ret = -14 };
        var t = await TestDirectory.CreateAsync([Notify(true), updates]);
        await t.SaveAsync(State());
        await RunAsync(t, "listen", ["--run-for", "3"], 2);
        Assert((await t.LoadAsync()).Session.Cursor == "", "Expired response advanced cursor.");
        t.AssertRequests("notifystart", "getupdates");
    }

    private static async Task RunForAsync()
    {
        var t = await TestDirectory.CreateAsync([Notify(true), LongPoll(), Notify(false)]);
        await t.SaveAsync(State());
        var result = await RunAsync(t, "listen", ["--run-for", "1.2"], 0);
        Assert(result.DurationMs is >= 800 and < 7000, "run-for did not bound listening duration.");
        t.AssertRequests("notifystart", "getupdates", "notifystop");
        Assert(t.Trace().Any(x => x.GetProperty("phase").GetString() == "canceled"), "Poll was not canceled through transport.");
    }

    private static async Task ExhaustedFixtureAsync()
    {
        var t = await TestDirectory.CreateAsync([]);
        await t.SaveAsync(State());
        await RunAsync(t, "listen", ["--run-for", "1"], 1);
        Assert(t.Trace().Count(x => x.GetProperty("phase").GetString() == "rejected") == 1, "Exhausted fixture did not reject before network fallback.");
        Assert(t.Requests().Count == 0, "Exhausted fixture fabricated an accepted call.");
    }

    private static async Task RealCredentialGuardAsync()
    {
        var t = await TestDirectory.CreateAsync([]);
        var state = State(); state.Session.BotToken = "negative-test-not-a-real-account-token";
        await t.SaveAsync(state);
        var before = await HashAsync(t.State);
        await RunAsync(t, "send", [], 1, "fixture-guard\n");
        Assert(await HashAsync(t.State) == before, "Non-fixture binding was changed by offline mode.");
        Assert(t.Requests().Count == 0, "Non-fixture binding reached transport.");
        Assert(!File.Exists(t.State + ".previous"), "Guard copied non-fixture credentials.");
    }

    private static async Task PathIsolationAsync()
    {
        var first = await TestDirectory.CreateAsync([]);
        var second = await TestDirectory.CreateAsync([]);
        await File.WriteAllBytesAsync(second.State, "fixture-cross-directory-unreadable-state"u8.ToArray());
        var before = await HashAsync(second.State);
        await RunAsync(first, "status", ["--state", second.State], 1, includeState: false);
        Assert(first.Requests().Count == 0 && second.Requests().Count == 0, "Cross-directory fixture was used.");
        Assert(await HashAsync(second.State) == before && !File.Exists(second.State + ".lock"),
            "Cross-directory validation opened or modified the state before rejecting its path.");
        File.Delete(Path.Combine(first.Directory, ".weixin-offline-test"));
        await RunAsync(first, "status", [], 1);
        Assert(!File.Exists(first.State), "Missing sentinel created a binding.");
    }

    private static async Task ResponseCredentialGuardAsync()
    {
        var t = await TestDirectory.CreateAsync([QrStep(), StatusStep("confirmed", response: new
            { status = "confirmed", bot_token = "negative-test-non-fixture-token", ilink_bot_id = Bot, ilink_user_id = User })]);
        await RunAsync(t, "login", [], 1);
        Assert(!File.Exists(t.State) && t.Requests().Count == 0, "Fixture response accepted non-test credentials.");
    }

    private static async Task OtherExitCodesAsync()
    {
        var absent = await TestDirectory.CreateAsync([]);
        await RunAsync(absent, "status", [], 1);
        var bad = await TestDirectory.CreateAsync([]);
        await File.WriteAllBytesAsync(bad.State, "fixture-not-dpapi"u8.ToArray());
        await RunAsync(bad, "status", [], 4);
        var rejected = Send("fixture-http-rejection"); rejected.Status = 400;
        var t = await TestDirectory.CreateAsync([rejected]); await t.SaveAsync(State());
        await RunAsync(t, "send", [], 2, "fixture-http-rejection\n");
        Assert((await t.LoadAsync()).Outbox.Single().Status == "Rejected", "Definite HTTP rejection was not recorded Rejected.");
        t.AssertRequests("sendmessage");
    }

    private static async Task InvalidUpdateBatchAsync()
    {
        var updates = Updates([], "fixture-invalid-next-cursor");
        updates.Body = new { ret = 0, get_updates_buf = "fixture-invalid-next-cursor",
            msgs = new object?[] { Message("fixture-valid-before-null", "fixture-batch", "fixture-uncommitted-context"), null } };
        var t = await TestDirectory.CreateAsync([Notify(true), updates, Notify(false)]);
        await t.SaveAsync(State());
        await RunAsync(t, "listen", ["--echo", "--run-for", "3"], 4);
        var state = await t.LoadAsync();
        Assert(state.Session.Cursor == "" && state.Session.ContextToken == Context && state.Inbox.Count == 0 && state.SeenKeys.Count == 0,
            "A malformed later entry partially committed an earlier context/message or cursor.");
        t.AssertRequests("notifystart", "getupdates", "notifystop");
    }

    private static async Task InvalidSavedInboxAsync()
    {
        var t = await TestDirectory.CreateAsync([]);
        var state = State();
        var wrongOwner = Message("fixture-invalid-saved-owner", "fixture-saved-input", Context);
        wrongOwner.FromUserId = "fixture-other-owner";
        var entry = Entry(wrongOwner); state.Inbox.Add(entry); state.SeenKeys.Add(entry.Key);
        await t.SaveAsync(state);
        var before = await HashAsync(t.State);
        await RunAsync(t, "listen", ["--echo", "--run-for", "1"], 4);
        Assert(t.Requests().Count == 0 && await HashAsync(t.State) == before, "Invalid saved inbox reached network or mutated state.");
    }

    private static async Task CancellationDuringSendAsync()
    {
        var send = Send("已收到：fixture-canceled-send", "fixture-cancel-context"); send.DelayMs = -1;
        var t = await TestDirectory.CreateAsync([Notify(true),
            Updates([Message("fixture-cancel-inbound", "fixture-canceled-send", "fixture-cancel-context")], "fixture-cancel-cursor"), send, Notify(false)]);
        await t.SaveAsync(State(null));
        await RunAsync(t, "listen", ["--echo", "--run-for", "1.2"], 3);
        var state = await t.LoadAsync();
        Assert(state.Outbox.Single().Status == "Unknown" && state.Inbox.Count == 1 && state.Session.Cursor == "fixture-cancel-cursor",
            "Cancellation during send lost intent or incorrectly confirmed delivery.");
        t.AssertRequests("notifystart", "getupdates", "sendmessage", "notifystop");
        Assert(t.Trace().Any(x => x.GetProperty("phase").GetString() == "canceled" && x.GetProperty("step").GetInt32() == 2),
            "Transport send was not canceled in the requested window.");
        await t.RewriteAsync([]);
        await RunAsync(t, "listen", ["--echo", "--run-for", "1"], 1);
        Assert(t.Requests().Count(x => Endpoint(x) == "sendmessage") == 1, "Cancellation triggered another send on restart.");
    }

    private static async Task CheckSentAsync(TestDirectory t, string text)
    {
        var state = await t.LoadAsync();
        var receipt = state.Outbox.Single();
        Assert(receipt.Status == "Sent" && receipt.SourceKey is null, "Successful standalone send receipt differs.");
        Assert(receipt.ContentSha256 == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))), "Content hash differs from input.");
        var wire = t.Requests().Single().GetProperty("body").GetProperty("msg");
        Assert(wire.GetProperty("client_id").GetString() == receipt.ClientId, "Saved/wire client_id differs.");
        Assert(wire.GetProperty("to_user_id").GetString() == User, "Send target differs from bound account.");
        Assert(wire.GetProperty("context_token").GetString() == Context, "Send did not use saved context.");
    }

    private static BotState State(string? context = Context) => new()
    {
        TransportMode = "offline-fixture", BoundAt = DateTimeOffset.UtcNow, Session = new BotSession { BotToken = Token, UserId = User,
            BotId = Bot, BaseUrl = ILinkClient.DefaultBaseUrl, ContextToken = context }
    };
    private static InboundMessage Message(string id, string text, string context) => new()
    {
        MessageId = id, FromUserId = User, ToUserId = Bot, MessageType = 1, MessageState = 2,
        ContextToken = context, Items = [new MessageItem { Type = 1, TextItem = new TextItem { Text = text } }]
    };
    private static InboxEntry Entry(InboundMessage message) => new() { Key = PersistentBotRunner.MessageKey(message), Message = message };
    private static Step QrStep() => new() { Path = "/ilink/bot/get_bot_qrcode", Query = new() { ["bot_type"] = "3" },
        BodySubset = new { local_token_list = Array.Empty<string>() }, Body = new { qrcode = "fixture-qr-code", qrcode_img_content = "https://example.com/fixture-qr" } };
    private static Step StatusStep(string status, string? code = null, object? response = null)
    {
        var query = new Dictionary<string, string> { ["qrcode"] = "fixture-qr-code" };
        if (code is not null) query.Add("verify_code", code);
        return new() { Method = "GET", Path = "/ilink/bot/get_qrcode_status", Query = query, Body = response ?? new { status } };
    }
    private static Step Notify(bool started) => new() { Path = "/ilink/bot/msg/" + (started ? "notifystart" : "notifystop"),
        RequireBearer = true, Body = new { ret = 0 } };
    private static Step Updates(List<InboundMessage> messages, string cursor) => new() { Path = "/ilink/bot/getupdates", RequireBearer = true,
        Body = new { ret = 0, get_updates_buf = cursor, msgs = messages } };
    private static Step LongPoll() => new() { Path = "/ilink/bot/getupdates", RequireBearer = true, DelayMs = -1, Body = new { ret = 0, msgs = Array.Empty<object>() } };
    private static Step Send(string text, string context = Context, string? fault = null) => new() { Path = "/ilink/bot/sendmessage", RequireBearer = true,
        BodySubset = new { msg = new { from_user_id = "", to_user_id = User, message_type = 2, message_state = 2, context_token = context,
            item_list = new[] { new { type = 1, text_item = new { text } } } } }, Body = new { ret = 0 }, Fault = fault };

    private static async Task<ProcessEvidence> RunAsync(TestDirectory t, string command, string[] extra, int expectedExit,
        string? input = null, bool includeState = true)
    {
        using var child = Start(t, command, extra, input, includeState);
        var result = await child.CompleteAsync(TimeSpan.FromSeconds(25));
        Assert(result.ExitCode == expectedExit, $"Expected exit {expectedExit}, got {result.ExitCode}; stderr: {result.Stderr}");
        Assert(!string.IsNullOrEmpty(result.Stdout) || !string.IsNullOrEmpty(result.Stderr), "CLI exited without any output; command execution is unproven.");
        return result;
    }
    private static Child Start(TestDirectory t, string command, string[] extra, string? input = null, bool includeState = true, bool includeFixture = true)
    {
        var args = new List<string> { command };
        if (includeFixture) args.AddRange(["--offline-fixture", t.Fixture]);
        if (includeState) args.AddRange(["--state", t.State]);
        args.AddRange(extra);
        return new Child(t.Directory, args, input);
    }
    private static async Task WaitFileAsync(string path, Child child, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (child.HasExited) throw new Exception("Child exited before reaching the requested transport marker.");
            if (watch.Elapsed > timeout) throw new TimeoutException("Child did not reach the requested transport marker.");
            await Task.Delay(30);
        }
    }
    private static string Endpoint(JsonElement request) => request.GetProperty("path").GetString()!.Split('/')[^1];
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task<string> HashAsync(string path) => Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
    private static async Task<object> ProvenanceAsync()
    {
        var repo = FindRepository();
        var sourcePaths = new[] { "Directory.Build.props", "global.json", "src/Weixin.Cli/Weixin.Cli.csproj", "src/Weixin.Cli/Program.cs",
            "src/Weixin.Cli/OfflineFixtureTransport.cs", "src/Weixin.Protocol/Weixin.Protocol.csproj", "src/Weixin.Protocol/ILinkClient.cs",
            "src/Weixin.Protocol/Models.cs", "src/Weixin.Protocol/PersistentBotRunner.cs", "src/Weixin.Protocol/StateVault.cs",
            "src/Weixin.Protocol/MediaClient.cs", "src/Weixin.Protocol/MarkdownFormatting.cs", "src/Weixin.Protocol/TypingClient.cs",
            "src/Weixin.Protocol/TypingLifecycle.cs", "src/Weixin.Protocol/VoiceCodec.cs",
            "tests/Weixin.Cli.E2ETests/Weixin.Cli.E2ETests.csproj", "tests/Weixin.Cli.E2ETests/Program.cs", "tests/Weixin.Cli.E2ETests/MediaE2E.cs",
            "tests/Weixin.Cli.E2ETests/TypingVoiceE2E.cs", "tests/Weixin.Cli.E2ETests/TypingLifecycleE2E.cs",
            "runtime/voice/codec-bridge.mjs", "runtime/voice/provenance.json" };
        var files = new List<object>();
        foreach (var relative in sourcePaths)
        {
            var path = Path.Combine(repo, relative);
            files.Add(new { path = relative, sha256 = await HashAsync(path), sizeBytes = new FileInfo(path).Length });
        }
        var published = new List<object>();
        foreach (var path in Directory.GetFiles(Path.GetDirectoryName(exe)!).Where(p => Path.GetFileName(p) is "weixin.exe" or "weixin.dll" or "Weixin.Protocol.dll" or "weixin.deps.json" or "weixin.runtimeconfig.json"))
            published.Add(new { path, sha256 = await HashAsync(path), sizeBytes = new FileInfo(path).Length });
        var publishedVoice = Path.Combine(Path.GetDirectoryName(exe)!, "runtime", "voice");
        if (Directory.Exists(publishedVoice))
            foreach (var path in Directory.GetFiles(publishedVoice, "*", SearchOption.AllDirectories))
                published.Add(new { path, sha256 = await HashAsync(path), sizeBytes = new FileInfo(path).Length });
        return new { sourceRoot = repo, sourceFiles = files, publishedFiles = published,
            executableSha256 = await HashAsync(exe), note = "Hashes identify the exact source snapshot and launched published files; build provenance is recorded separately by the release script." };
    }
    private static string FindRepository()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")) && Directory.Exists(Path.Combine(dir.FullName, "src", "Weixin.Cli")))
                return dir.FullName;
        throw new Exception("Cannot locate source root for hash provenance.");
    }

    private sealed class TestDirectory
    {
        private int fixtureVersion;
        public string Directory { get; }
        public string Fixture => Path.Combine(Directory, "fixture-" + fixtureVersion.ToString("D3") + ".json");
        public string State => Path.Combine(Directory, "binding.dpapi");
        public string TestId => Path.GetFileName(Directory);
        private TestDirectory(string directory) { Directory = directory; }
        public static async Task<TestDirectory> CreateAsync(List<Step> steps)
        {
            var t = new TestDirectory(Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N")));
            System.IO.Directory.CreateDirectory(t.Directory);
            await File.WriteAllTextAsync(Path.Combine(t.Directory, ".weixin-offline-test"), t.TestId, new UTF8Encoding(false));
            await t.RewriteAsync(steps);
            return t;
        }
        public Task RewriteAsync(List<Step> steps)
        {
            fixtureVersion++;
            return File.WriteAllTextAsync(Fixture,
                JsonSerializer.Serialize(new { formatVersion = 1, testId = TestId, steps }, Json), new UTF8Encoding(false));
        }
        public async Task SaveAsync(BotState state) { using var vault = new StateVault(State); await vault.SaveAsync(state); }
        public async Task<BotState> LoadAsync() { using var vault = new StateVault(State); return await vault.LoadAsync<BotState>() ?? throw new Exception("Missing test state."); }
        public List<JsonElement> Trace()
        {
            var path = Path.Combine(Directory, "fixture-trace.jsonl");
            if (!File.Exists(path)) return [];
            return File.ReadAllLines(path, Encoding.UTF8).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s =>
            { using var doc = JsonDocument.Parse(s); return doc.RootElement.Clone(); }).ToList();
        }
        public List<JsonElement> Requests() => Trace().Where(x => x.GetProperty("phase").GetString() == "request").ToList();
        public void AssertNoRejections() => Assert(!Trace().Any(x => x.GetProperty("phase").GetString() == "rejected"), "A fixture request was rejected.");
        public void AssertRequests(params string[] expected)
        {
            var actual = Requests().Select(Endpoint).ToArray();
            Assert(actual.SequenceEqual(expected), "Request sequence differs: " + string.Join(",", actual));
            AssertNoRejections();
        }
    }
    private sealed class Step
    {
        public string Host { get; set; } = "ilinkai.weixin.qq.com";
        public string Method { get; set; } = "POST";
        public string Path { get; set; } = "";
        public Dictionary<string, string> Query { get; set; } = [];
        public object? BodySubset { get; set; }
        public bool RequireBearer { get; set; }
        public int Status { get; set; } = 200;
        public object? Body { get; set; }
        public string? RawBody { get; set; }
        public int DelayMs { get; set; }
        public string? Fault { get; set; }
        public string? WaitMarker { get; set; }
        public string ContentType { get; set; } = "application/json";
        public string? BodyBase64 { get; set; }
        public Dictionary<string, string>? ResponseHeaders { get; set; }
        public string? ExpectedPlaintextBase64 { get; set; }
        public int? AesKeyStep { get; set; }
        public int? AssertSentAesKeyStep { get; set; }
    }
    private sealed class Child : IDisposable
    {
        private readonly Process process;
        private readonly Task<string> stdout;
        private readonly Task<string> stderr;
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private readonly string directory;
        private readonly string[] args;
        private bool collected;
        public bool HasExited => process.HasExited;
        public Child(string directory, List<string> args, string? input)
        {
            this.directory = directory; this.args = args.ToArray();
            var start = new ProcessStartInfo(exe) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            process = Process.Start(start) ?? throw new Exception("Published CLI could not start.");
            stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync();
            if (input is not null) process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        public async Task<ProcessEvidence> CompleteAsync(TimeSpan timeout)
        {
            using var deadline = new CancellationTokenSource(timeout);
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) { await KillAsync(); throw new TimeoutException("CLI process exceeded the E2E timeout."); }
            return await CollectAsync(killed: false);
        }
        public async Task KillAsync()
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await CollectAsync(killed: true);
        }
        private async Task<ProcessEvidence> CollectAsync(bool killed)
        {
            if (collected) return Processes.Last(p => p.ProcessId == process.Id);
            collected = true;
            var result = new ProcessEvidence("process-" + (Processes.Count + 1).ToString("D3"), process.Id, args,
                process.ExitCode, watch.ElapsedMilliseconds, killed, await stdout, await stderr);
            Processes.Add(result);
            var path = Path.Combine(directory, result.Id + ".log");
            await File.WriteAllTextAsync(path, "STDOUT\n" + result.Stdout + "\nSTDERR\n" + result.Stderr, new UTF8Encoding(false));
            return result;
        }
        public void Dispose()
        {
            if (!collected)
            {
                if (!process.HasExited) KillAsync().GetAwaiter().GetResult();
                else CollectAsync(killed: false).GetAwaiter().GetResult();
            }
            process.Dispose();
        }
    }
    private sealed record CaseResult(string Name, bool Passed, long DurationMs, string? Failure, string[] ProcessEvidenceIds);
    private sealed record ProcessEvidence(string Id, int ProcessId, string[] Arguments, int ExitCode, long DurationMs, bool Killed, string Stdout, string Stderr);
}
