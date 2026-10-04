using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Weixin.Protocol;

internal static partial class CliSuite
{
    private const string TypingTicket = "fixture-typing-ticket";
    private static async Task TypingVoiceCasesAsync()
    {
        await Case("typing_timed_start_cancel_accepts_empty_and_non_json_success_bodies", TypingTimedAsync);
        await Case("typing_timed_cancel_disconnect_then_fresh_explicit_stop_preserves_state", TypingCancelDisconnectThenStopAsync);
        await Case("typing_explicit_stop_does_not_send_chat_or_persist_ticket", TypingStopAsync);
        await Case("typing_requires_explicit_ret_zero_and_nonempty_ticket", TypingMissingAsync);
        await Case("typing_start_http403_does_not_send_cancel", TypingAuthAsync);
        await Case("typing_timed_start_transient_failure_still_sends_cancel", TypingStartFailureAsync);
        await Case("typing_listen_echo_start_send_cancel_and_memory_config_cache", TypingEchoAsync);
        await Case("typing_listen_start_cancelled_still_cancels_and_keeps_inbox", TypingCancelledAsync);
        await Case("typing_listen_start_auth_rejection_keeps_inbox_without_stop", TypingListenAuthAsync);
        await Case("cli_voice_prepare_pcm_wave_decode_without_account_state", VoiceCommandsAsync);
        await Case("cli_voice_conversion_input_output_guards_preserve_existing_files", VoiceCommandGuardsAsync);
        await Case("cli_voice_received_silk_automatically_decodes_and_preserves_original", VoiceIncomingAsync);
        await Case("cli_voice_silk_encode_type_four_decodes_actual_payload", () => VoiceIncomingForEncodingAsync(4));
        await Case("cli_voice_silk_missing_encode_type_decodes_actual_payload", () => VoiceIncomingForEncodingAsync(null));
        await Case("cli_voice_non_silk_preserves_raw_without_creating_wave", VoiceIncomingNonSilkAsync);
        await Case("cli_native_voice_explicit_sample_rate_bits_are_wire_fields", VoiceExplicitMetadataAsync);
        await Case("cli_native_voice_metadata_invalid_combinations_stop_before_http", VoiceMetadataGuardsAsync);
        await Case("cli_native_voice_metadata_business_key_compatibility_and_no_resend", VoiceMetadataReplayAsync);
    }
    private static Step TypingConfigStep(object? response = null) => new()
    {
        Path = "/ilink/bot/getconfig", RequireBearer = true,
        BodySubset = new { ilink_user_id = User, context_token = Context },
        Body = response ?? new { ret = 0, typing_ticket = TypingTicket }
    };
    private static Step TypingStep(bool started, int status = 200) => new()
    {
        Path = "/ilink/bot/sendtyping", RequireBearer = true,
        BodySubset = new { ilink_user_id = User, typing_ticket = TypingTicket, status = started ? 1 : 2 },
        Status = status, RawBody = started ? "fixture-non-json-success-body" : ""
    };
    private static async Task TypingTimedAsync()
    {
        var t = await TestDirectory.CreateAsync([TypingConfigStep(), TypingStep(true), TypingStep(false)]);
        await t.SaveAsync(State()); var before = await HashAsync(t.State);
        var result = await RunAsync(t, "typing", ["--typing-status", "start", "--run-for", "0.2"], 0);
        Assert(result.Stdout.Contains("取消", StringComparison.Ordinal), "Timed start did not report cancellation.");
        Assert(!result.Stdout.Contains(TypingTicket, StringComparison.Ordinal) && !result.Stderr.Contains(TypingTicket, StringComparison.Ordinal), "Typing ticket leaked to process output.");
        Assert(await HashAsync(t.State) == before && (await t.LoadAsync()).Outbox.Count == 0, "Typing changed durable state/chat outbox.");
        t.AssertRequests("getconfig", "sendtyping", "sendtyping");
    }
    private static async Task TypingStopAsync()
    {
        var t = await TestDirectory.CreateAsync([TypingConfigStep(), TypingStep(false)]); await t.SaveAsync(State());
        await RunAsync(t, "typing", ["--typing-status", "stop"], 0);
        Assert((await t.LoadAsync()).Outbox.Count == 0, "Explicit typing stop sent chat.");
        t.AssertRequests("getconfig", "sendtyping");
    }
    private static async Task TypingCancelDisconnectThenStopAsync()
    {
        // Reproduce the observed timed-cancel connection failure without a socket or real binding.
        // The short window is independent of the official five-second keepalive behavior.
        var cancel = TypingStep(false); cancel.Fault = "disconnect";
        var t = await TestDirectory.CreateAsync([TypingConfigStep(), TypingStep(true), cancel]);
        await t.SaveAsync(State()); var before = await HashAsync(t.State);
        var timed = await RunAsync(t, "typing", ["--typing-status", "start", "--run-for", "0.2"], 2);
        Assert(timed.Stdout.Contains("打字开始请求已被 HTTP 接受", StringComparison.Ordinal), "Timed typing start was not accepted.");
        Assert(timed.Stderr.Trim() == "微信打字状态连接失败。", "Cancel disconnect did not produce the safe connection-failure diagnostic.");
        Assert(!timed.Stdout.Contains("已发送打字取消请求", StringComparison.Ordinal) &&
            !timed.Stdout.Contains("打字取消请求已被 HTTP 接受", StringComparison.Ordinal), "Failed timed cancel was reported as accepted.");
        t.AssertRequests("getconfig", "sendtyping", "sendtyping");
        var timedRequests = t.Requests();
        Assert(timedRequests.All(x => x.GetProperty("processId").GetInt32() == timed.ProcessId) &&
            timedRequests.Where(x => Endpoint(x) == "sendtyping").Select(x => x.GetProperty("body").GetProperty("status").GetInt32()).SequenceEqual([1, 2]),
            "Timed typing retried, refreshed, or sent an unexpected status.");
        var timedTrace = t.Trace();
        Assert(timedTrace.Count(x => x.GetProperty("phase").GetString() == "disconnect") == 1 &&
            !timedTrace.Any(x => x.GetProperty("phase").GetString() == "response" && x.GetProperty("step").GetInt32() == 2),
            "Cancel fault was not observed exactly once or fabricated a success response.");
        await AssertUnchangedAndPrivateAsync(timed);

        // A new CLI process obtains its own in-memory ticket and explicitly stops the same fake binding.
        await t.RewriteAsync([TypingConfigStep(), TypingStep(false)]);
        var stopped = await RunAsync(t, "typing", ["--typing-status", "stop"], 0);
        Assert(stopped.ProcessId != timed.ProcessId && stopped.Stderr.Length == 0 &&
            stopped.Stdout.Contains("打字取消请求已被 HTTP 接受", StringComparison.Ordinal), "Fresh explicit stop was not accepted cleanly.");
        t.AssertRequests("getconfig", "sendtyping", "sendtyping", "getconfig", "sendtyping");
        var stopRequests = t.Requests().Skip(timedRequests.Count).ToArray();
        Assert(stopRequests.All(x => x.GetProperty("processId").GetInt32() == stopped.ProcessId) &&
            stopRequests.Where(x => Endpoint(x) == "sendtyping").Select(x => x.GetProperty("body").GetProperty("status").GetInt32()).SequenceEqual([2]),
            "Fresh explicit stop retried, started typing, or sent chat.");
        await AssertUnchangedAndPrivateAsync(stopped);

        async Task AssertUnchangedAndPrivateAsync(ProcessEvidence result)
        {
            var state = await t.LoadAsync();
            Assert(await HashAsync(t.State) == before && state.Outbox.Count == 0 &&
                !JsonSerializer.Serialize(state, Json).Contains(TypingTicket, StringComparison.Ordinal), "Typing changed durable state/outbox or persisted its ticket.");
            Assert(new[] { TypingTicket, Token, Context }.All(value =>
                !result.Stdout.Contains(value, StringComparison.Ordinal) && !result.Stderr.Contains(value, StringComparison.Ordinal)), "Typing exposed its ticket or binding secrets in output.");
            // Fixture request traces intentionally contain the declared mock wire contract;
            // application stdout/stderr logs must never contain even that mock ticket.
            foreach (var log in Directory.GetFiles(t.Directory, "process-*.log"))
                Assert(!(await File.ReadAllTextAsync(log)).Contains(TypingTicket, StringComparison.Ordinal), "Application process log persisted the typing ticket.");
        }
    }
    private static async Task TypingMissingAsync()
    {
        foreach (var response in new object[] { new { ret = 0 }, new { typing_ticket = TypingTicket } })
        {
            var t = await TestDirectory.CreateAsync([TypingConfigStep(response)]); await t.SaveAsync(State());
            await RunAsync(t, "typing", ["--typing-status", "start"], response.GetType().GetProperty("ret") is null ? 2 : 1);
            t.AssertRequests("getconfig");
        }
        var guard = await TestDirectory.CreateAsync([TypingConfigStep(new { ret = 0, typing_ticket = "not-a-real-account-test-placeholder" })]);
        await guard.SaveAsync(State()); await RunAsync(guard, "typing", ["--typing-status", "start"], 1);
        Assert(guard.Requests().Count == 0, "Non-fixture typing ticket guard reached transport.");
    }
    private static async Task TypingAuthAsync()
    {
        var t = await TestDirectory.CreateAsync([TypingConfigStep(), TypingStep(true, 403)]); await t.SaveAsync(State());
        await RunAsync(t, "typing", ["--typing-status", "start", "--run-for", "0.2"], 2);
        t.AssertRequests("getconfig", "sendtyping");
    }
    private static async Task TypingStartFailureAsync()
    {
        var t = await TestDirectory.CreateAsync([TypingConfigStep(), TypingStep(true, 500), TypingStep(false)]); await t.SaveAsync(State());
        await RunAsync(t, "typing", ["--typing-status", "start", "--run-for", "0.2"], 2);
        t.AssertRequests("getconfig", "sendtyping", "sendtyping");
    }
    private static async Task TypingEchoAsync()
    {
        var a = Message("fixture-typing-a", "fixture-typing-first", Context);
        var b = Message("fixture-typing-b", "fixture-typing-second", Context);
        var t = await TestDirectory.CreateAsync([Notify(true), Updates([a, b], "fixture-typing-cursor"), TypingConfigStep(),
            TypingStep(true), Send("已收到：fixture-typing-first"), TypingStep(false),
            TypingStep(true), Send("已收到：fixture-typing-second"), TypingStep(false), LongPoll(), Notify(false)]);
        // The production runner enforces a three-second minimum between sends.
        // Allow both replies to finish before cancelling the final long poll.
        await t.SaveAsync(State()); await RunAsync(t, "listen", ["--echo", "--typing", "--run-for", "5"], 0);
        var state = await t.LoadAsync(); Assert(state.Outbox.Count == 2 && state.Outbox.All(r => r.Status == "Sent") && state.Inbox.Count == 0, "Typing echo did not complete two replies.");
        t.AssertRequests("notifystart", "getupdates", "getconfig", "sendtyping", "sendmessage", "sendtyping", "sendtyping", "sendmessage", "sendtyping", "getupdates", "notifystop");
    }
    private static async Task TypingCancelledAsync()
    {
        var started = TypingStep(true); started.DelayMs = -1; started.WaitMarker = "typing-start-in-flight.marker";
        var message = Message("fixture-typing-cancel", "fixture-cancel", Context);
        var t = await TestDirectory.CreateAsync([Notify(true), Updates([message], "fixture-cancel-cursor"), TypingConfigStep(), started, TypingStep(false), Notify(false)]);
        await t.SaveAsync(State());
        using (var child = Start(t, "listen", ["--echo", "--typing", "--run-for", "1.5"]))
        {
            await WaitFileAsync(Path.Combine(t.Directory, "typing-start-in-flight.marker"), child, TimeSpan.FromSeconds(5));
            var result = await child.CompleteAsync(TimeSpan.FromSeconds(10)); Assert(result.ExitCode == 0, "Cancelled typing listener did not exit cleanly.");
        }
        var state = await t.LoadAsync(); Assert(state.Inbox.Count == 1 && state.Outbox.Count == 0, "Cancellation during typing lost pending input or sent chat.");
        t.AssertRequests("notifystart", "getupdates", "getconfig", "sendtyping", "sendtyping", "notifystop");
    }
    private static async Task TypingListenAuthAsync()
    {
        var message = Message("fixture-typing-denied", "fixture-denied", Context);
        var t = await TestDirectory.CreateAsync([Notify(true), Updates([message], "fixture-denied-cursor"), TypingConfigStep(), TypingStep(true, 403)]);
        await t.SaveAsync(State()); await RunAsync(t, "listen", ["--echo", "--typing", "--run-for", "2"], 2);
        var state = await t.LoadAsync(); Assert(state.Inbox.Count == 1 && state.Outbox.Count == 0, "Typing auth denial changed pending/outbox state.");
        t.AssertRequests("notifystart", "getupdates", "getconfig", "sendtyping");
    }
    private static async Task<ProcessEvidence> RunCodecAsync(TestDirectory t, string command, string[] arguments, int exit)
    {
        // Codec commands have an account-free branch. No state/fixture argument is supplied.
        using var child = Start(t, command, arguments, includeState: false, includeFixture: false);
        var result = await child.CompleteAsync(TimeSpan.FromSeconds(35));
        Assert(result.ExitCode == exit && (!string.IsNullOrEmpty(result.Stdout) || !string.IsNullOrEmpty(result.Stderr)), "Codec CLI exit/output differs: " + result.Stderr);
        Assert(!File.Exists(t.State) && t.Requests().Count == 0, "Codec operation accessed test account state or transport.");
        return result;
    }
    private static async Task VoiceCommandsAsync()
    {
        var t = await TestDirectory.CreateAsync([]); var pcm = SyntheticVoicePcm();
        var p = Path.Combine(t.Directory, "synthetic.pcm"); var w = Path.Combine(t.Directory, "synthetic.wav");
        var s1 = Path.Combine(t.Directory, "from-pcm.silk"); var s2 = Path.Combine(t.Directory, "from-wave.silk"); var decoded = Path.Combine(t.Directory, "decoded.wav");
        await File.WriteAllBytesAsync(p, pcm); await File.WriteAllBytesAsync(w, SyntheticVoiceWave(pcm)); var before = await HashAsync(p);
        var encoded = await RunCodecAsync(t, "prepare-voice", ["--file", p, "--input-format", "pcm", "--output", s1], 0);
        await RunCodecAsync(t, "prepare-voice", ["--file", w, "--input-format", "wav", "--output", s2], 0);
        Assert(encoded.Stdout.Contains("2000 毫秒", StringComparison.Ordinal) && await HashAsync(s1) == await HashAsync(s2), "Published WAV/PCM conversion differs.");
        await RunCodecAsync(t, "decode-voice", ["--file", s1, "--output", decoded], 0);
        var bytes = await File.ReadAllBytesAsync(decoded);
        Assert(bytes.Length == pcm.Length + 44 && bytes.AsSpan(0, 44).SequenceEqual(SyntheticVoiceWave(new byte[pcm.Length]).AsSpan(0, 44)), "Published decode WAV format differs.");
        Assert(await HashAsync(p) == before && !Directory.GetFiles(t.Directory, "*.dpapi*").Any(), "Account-free codec touched state or original PCM.");
    }
    private static async Task VoiceCommandGuardsAsync()
    {
        var t = await TestDirectory.CreateAsync([]); var pcm = SyntheticVoicePcm(); var input = Path.Combine(t.Directory, "input.wav");
        var target = Path.Combine(t.Directory, "existing.silk"); await File.WriteAllBytesAsync(input, SyntheticVoiceWave(pcm));
        await File.WriteAllTextAsync(target, "fixture-preserve-existing-output"); var before = await HashAsync(target);
        await RunCodecAsync(t, "prepare-voice", ["--file", input, "--input-format", "wav", "--output", target], 4);
        Assert(await HashAsync(target) == before, "Existing codec output was overwritten.");
        var bad = SyntheticVoiceWave(pcm); bad[22] = 2; await File.WriteAllBytesAsync(input, bad);
        var fresh = Path.Combine(t.Directory, "fresh.silk");
        await RunCodecAsync(t, "prepare-voice", ["--file", input, "--input-format", "wav", "--output", fresh], 1);
        await RunCodecAsync(t, "prepare-voice", ["--file", input, "--input-format", "mp3", "--output", fresh], 1);
        await RunCodecAsync(t, "prepare-voice", ["--file", input, "--output", fresh], 1);
        await File.WriteAllTextAsync(input, "fixture-not-a-silk");
        await RunCodecAsync(t, "decode-voice", ["--file", input, "--output", fresh], 4);
        Assert(!File.Exists(fresh), "Rejected codec input created output.");
    }
    private static Task VoiceIncomingAsync() => VoiceIncomingForEncodingAsync(6);
    private static async Task VoiceIncomingForEncodingAsync(int? encoding)
    {
        var setup = await TestDirectory.CreateAsync([]); var pcm = SyntheticVoicePcm();
        var input = Path.Combine(setup.Directory, "synthetic.pcm"); var silkPath = Path.Combine(setup.Directory, "synthetic.silk");
        await File.WriteAllBytesAsync(input, pcm); await RunCodecAsync(setup, "prepare-voice", ["--file", input, "--input-format", "pcm", "--output", silkPath], 0);
        var expectedWave = Path.Combine(setup.Directory, "expected.wav");
        await RunCodecAsync(setup, "decode-voice", ["--file", silkPath, "--output", expectedWave], 0);
        var payload = await File.ReadAllBytesAsync(silkPath);
        var message = IncomingMedia("fixture-real-codec-voice", 3, "fixture-real-codec-param");
        message.Items![0].VoiceItem!.EncodeType = encoding;
        message.Items[0].VoiceItem!.Playtime = 1; // untrusted metadata does not control actual decode duration.
        var t = await TestDirectory.CreateAsync([Notify(true), Updates([message], "fixture-real-codec-cursor"), DownloadStep("fixture-real-codec-param", payload), LongPoll(), Notify(false)]);
        using (var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(t.Fixture)))
        {
            var voice = fixture.RootElement.GetProperty("steps")[1].GetProperty("body").GetProperty("msgs")[0]
                .GetProperty("item_list")[0].GetProperty("voice_item");
            var hasEncoding = voice.TryGetProperty("encode_type", out var actualEncoding);
            Assert(encoding.HasValue ? hasEncoding && actualEncoding.GetInt32() == encoding.Value : !hasEncoding,
                "Voice fixture did not preserve the intended explicit or omitted encode_type.");
        }
        await t.SaveAsync(State()); var downloads = Path.Combine(t.Directory, "fixture-downloads");
        var result = await RunAsync(t, "listen", ["--download-dir", downloads, "--run-for", "2"], 0);
        var raw = Path.Combine(downloads, DownloadName(message, encoding == 6 ? ".silk" : ".voice"));
        Assert(File.Exists(raw) && (await File.ReadAllBytesAsync(raw)).AsSpan().SequenceEqual(payload), "Inbound SILK original was not preserved.");
        var playable = Path.ChangeExtension(raw, ".wav");
        Assert(File.Exists(playable) && new FileInfo(playable).Length == pcm.Length + 44, "Inbound SILK did not decode to actual 2-second WAV.");
        Assert(await HashAsync(playable) == await HashAsync(expectedWave), "Automatic decode WAV differs from the actual published codec output.");
        Assert(result.Stdout.Contains("语音已解码", StringComparison.Ordinal) && !result.Stderr.Contains("语音解码未完成", StringComparison.Ordinal), "Automatic decode was not reported as completed.");
        Assert(Directory.GetFiles(downloads).Length == 2 && !Directory.GetFiles(downloads, "*.tmp").Any(), "Automatic decode left extra or temporary files.");
        Assert((await t.LoadAsync()).Inbox.Count == 0, "Decoded input was not acknowledged.");
        t.AssertRequests("notifystart", "getupdates", "download", "getupdates", "notifystop");
    }
    private static async Task VoiceIncomingNonSilkAsync()
    {
        // Invented opaque voice fixture; no private phone recording is used.
        var payload = "fixture-non-silk-voice-test-bytes"u8.ToArray();
        var message = IncomingMedia("fixture-non-silk-voice", 3, "fixture-non-silk-param");
        message.Items![0].VoiceItem!.EncodeType = 4;
        var t = await TestDirectory.CreateAsync([Notify(true), Updates([message], "fixture-non-silk-cursor"), DownloadStep("fixture-non-silk-param", payload), LongPoll(), Notify(false)]);
        await t.SaveAsync(State()); var downloads = Path.Combine(t.Directory, "fixture-downloads");
        var result = await RunAsync(t, "listen", ["--download-dir", downloads, "--run-for", "2"], 0);
        var raw = Path.Combine(downloads, DownloadName(message, ".voice"));
        Assert(File.Exists(raw) && (await File.ReadAllBytesAsync(raw)).AsSpan().SequenceEqual(payload), "Non-SILK raw voice was not preserved exactly.");
        Assert(!File.Exists(Path.ChangeExtension(raw, ".wav")) && Directory.GetFiles(downloads).Length == 1, "Non-SILK payload was presented as WAV or created extra files.");
        Assert(!result.Stdout.Contains("语音已解码", StringComparison.Ordinal) && result.Stderr.Contains("语音解码未完成", StringComparison.Ordinal), "Non-SILK decode failure was not reported honestly.");
        Assert((await t.LoadAsync()).Inbox.Count == 0 && !Directory.GetFiles(downloads, "*.tmp").Any(), "Preserved raw voice was not acknowledged or left temporary files.");
        t.AssertRequests("notifystart", "getupdates", "download", "getupdates", "notifystop");
    }
    private static Step ExplicitVoiceSendStep(int? sampleRate, int? bitsPerSample)
    {
        var step = MediaSendStep(MediaKind.Voice, SilkFixture.Length, "fixture-metadata.silk", "silk");
        var voice = new Dictionary<string, object> { ["encode_type"] = 6, ["playtime"] = 1234 };
        if (sampleRate is { } rate) voice["sample_rate"] = rate;
        if (bitsPerSample is { } bits) voice["bits_per_sample"] = bits;
        step.BodySubset = new
        {
            msg = new
            {
                from_user_id = "", to_user_id = User, message_type = 2, message_state = 2, context_token = Context,
                item_list = new[] { new { type = 3, voice_item = voice } }
            }
        };
        return step;
    }
    private static async Task VoiceExplicitMetadataAsync()
    {
        // These are local accepted metadata guards, not a claim that Tencent accepts
        // every rate for native sending. Mock fixture acceptance is not real delivery.
        foreach (int rate in new[] { 8000, 12000, 16000, 24000, 32000, 44100, 48000 })
        {
            var t = await TestDirectory.CreateAsync([UploadUrlStep(SilkFixture, MediaKind.Voice), UploadStep(SilkFixture), ExplicitVoiceSendStep(rate, 16)]);
            await t.SaveAsync(State());
            var file = Path.Combine(t.Directory, "fixture-metadata.silk"); await File.WriteAllBytesAsync(file, SilkFixture);
            await RunAsync(t, "send-media", ["--file", file, "--kind", "voice", "--duration-ms", "1234", "--voice-encoding", "silk",
                "--voice-sample-rate", rate.ToString(System.Globalization.CultureInfo.InvariantCulture), "--voice-bits-per-sample", "16"], 0);
            var voice = SentVoice(t);
            Assert(voice.GetProperty("encode_type").GetInt32() == 6 && voice.GetProperty("sample_rate").GetInt32() == rate &&
                voice.GetProperty("bits_per_sample").GetInt32() == 16, "Explicit metadata wire values differ.");
            AssertActualVoiceMetadata(t, rate, 16);
            Assert((await t.LoadAsync()).Outbox.Single().Status == "Sent", "Explicit metadata did not record one accepted intent.");
            t.AssertRequests("getuploadurl", "upload", "sendmessage");
        }
        // Each optional field is independent; absent information must stay absent.
        foreach (var metadata in new (int? Rate, int? Bits)[] { (24000, null), (null, 16), (null, null) })
        {
            var t = await TestDirectory.CreateAsync([UploadUrlStep(SilkFixture, MediaKind.Voice), UploadStep(SilkFixture), ExplicitVoiceSendStep(metadata.Rate, metadata.Bits)]);
            await t.SaveAsync(State()); var file = Path.Combine(t.Directory, "fixture-metadata.silk"); await File.WriteAllBytesAsync(file, SilkFixture);
            var arguments = new List<string> { "--file", file, "--kind", "voice", "--duration-ms", "1234", "--voice-encoding", "silk" };
            if (metadata.Rate is { } rate) arguments.AddRange(["--voice-sample-rate", rate.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            if (metadata.Bits is { } bits) arguments.AddRange(["--voice-bits-per-sample", bits.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            await RunAsync(t, "send-media", arguments.ToArray(), 0);
            var voice = SentVoice(t);
            Assert(voice.TryGetProperty("sample_rate", out var actualRate) == metadata.Rate.HasValue &&
                (!metadata.Rate.HasValue || actualRate.GetInt32() == metadata.Rate.Value), "Omitted sample rate was invented or explicit rate was lost.");
            Assert(voice.TryGetProperty("bits_per_sample", out var actualBits) == metadata.Bits.HasValue &&
                (!metadata.Bits.HasValue || actualBits.GetInt32() == metadata.Bits.Value), "Omitted bit depth was invented or explicit depth was lost.");
            AssertActualVoiceMetadata(t, metadata.Rate, metadata.Bits);
            t.AssertRequests("getuploadurl", "upload", "sendmessage");
        }
    }
    private static JsonElement SentVoice(TestDirectory t) => t.Requests().Single(x => Endpoint(x) == "sendmessage")
        .GetProperty("body").GetProperty("msg").GetProperty("item_list")[0].GetProperty("voice_item");
    private static void AssertActualVoiceMetadata(TestDirectory t, int? sampleRate, int? bitsPerSample)
    {
        // BodySubset request traces only show declared expected fields. This separate
        // assertion was captured directly from the actual sent descriptor before scrubbing.
        var actual = t.Trace().Single(x => x.GetProperty("phase").GetString() == "fixture-assertion" &&
            x.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object).GetProperty("media");
        Assert(actual.GetProperty("voiceEncoding").GetInt32() == 6 &&
            actual.GetProperty("voiceSampleRatePresent").GetBoolean() == sampleRate.HasValue &&
            actual.GetProperty("voiceBitsPerSamplePresent").GetBoolean() == bitsPerSample.HasValue, "Actual voice descriptor encoding or field presence differs.");
        var observedRate = actual.GetProperty("voiceSampleRate"); var observedBits = actual.GetProperty("voiceBitsPerSample");
        Assert(sampleRate.HasValue ? observedRate.GetInt32() == sampleRate.Value : observedRate.ValueKind == JsonValueKind.Null,
            "Actual voice sample rate was invented or changed.");
        Assert(bitsPerSample.HasValue ? observedBits.GetInt32() == bitsPerSample.Value : observedBits.ValueKind == JsonValueKind.Null,
            "Actual voice bit depth was invented or changed.");
    }
    private static async Task VoiceMetadataGuardsAsync()
    {
        var t = await TestDirectory.CreateAsync([]); await t.SaveAsync(State()); var before = await HashAsync(t.State);
        var file = Path.Combine(t.Directory, "fixture-metadata.silk"); await File.WriteAllBytesAsync(file, SilkFixture);
        string[] valid = ["--file", file, "--kind", "voice", "--duration-ms", "1234", "--voice-encoding", "silk"];
        foreach (string rate in new[] { "0", "-1", "11025", "48001", "24000.0", "24k" })
            await RunAsync(t, "send-media", [.. valid, "--voice-sample-rate", rate], 1);
        foreach (string bits in new[] { "0", "-1", "8", "24", "32", "16.0" })
            await RunAsync(t, "send-media", [.. valid, "--voice-bits-per-sample", bits], 1);
        foreach (string kind in new[] { "file", "audio", "image", "video" })
        {
            await RunAsync(t, "send-media", ["--file", file, "--kind", kind, "--voice-sample-rate", "24000"], 1);
            await RunAsync(t, "send-media", ["--file", file, "--kind", kind, "--voice-bits-per-sample", "16"], 1);
        }
        await RunAsync(t, "status", ["--voice-sample-rate", "24000"], 1);
        await RunAsync(t, "status", ["--voice-bits-per-sample", "16"], 1);
        await RunAsync(t, "send-media", [.. valid, "--voice-sample-rate", "24000", "--voice-sample-rate", "48000"], 1);
        await RunAsync(t, "send-media", [.. valid, "--voice-bits-per-sample", "16", "--voice-bits-per-sample", "16"], 1);
        await RunAsync(t, "send-media", [.. valid, "--voice-sample-rate"], 1);
        await RunAsync(t, "send-media", [.. valid, "--voice-bits-per-sample"], 1);
        Assert(t.Requests().Count == 0 && await HashAsync(t.State) == before, "Invalid metadata accessed HTTP or changed account state.");
    }
    private static async Task VoiceMetadataReplayAsync()
    {
        const string sourceKey = "B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3B3";
        foreach (bool explicitMetadata in new[] { false, true })
        {
            var t = await TestDirectory.CreateAsync([UploadUrlStep(SilkFixture, MediaKind.Voice), UploadStep(SilkFixture),
                ExplicitVoiceSendStep(explicitMetadata ? 24000 : null, explicitMetadata ? 16 : null)]);
            await t.SaveAsync(State()); var file = Path.Combine(t.Directory, "fixture-metadata.silk"); await File.WriteAllBytesAsync(file, SilkFixture);
            string[] basic = ["--file", file, "--kind", "voice", "--duration-ms", "1234", "--voice-encoding", "silk", "--source-key", sourceKey];
            string[] arguments = explicitMetadata ? [.. basic, "--voice-sample-rate", "24000", "--voice-bits-per-sample", "16"] : basic;
            await RunAsync(t, "send-media", arguments, 0);
            AssertActualVoiceMetadata(t, explicitMetadata ? 24000 : null, explicitMetadata ? 16 : null);
            var first = (await t.LoadAsync()).Outbox.Single(); var before = await HashAsync(t.State);
            string originalFingerprint = $"{Convert.ToHexString(SHA256.HashData(SilkFixture))}\n{(int)MediaKind.Voice}\n1234\nsilk\nfixture-metadata.silk";
            string declaredFingerprint = originalFingerprint + "\nvoice_sample_rate:24000\nvoice_bits_per_sample:16";
            string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(explicitMetadata ? declaredFingerprint : originalFingerprint)));
            Assert(first.PayloadSha256 == expected, "Optional metadata changed the legacy fingerprint or was not included in the new fingerprint.");
            await RunAsync(t, "send-media", arguments, 0);
            await RunAsync(t, "send-media", [.. basic, "--voice-sample-rate", "48000", "--voice-bits-per-sample", "16"], 1);
            await RunAsync(t, "send-media", [.. basic, "--voice-sample-rate", "24000"], 1);
            await RunAsync(t, "send-media", explicitMetadata ? basic : [.. basic, "--voice-sample-rate", "24000", "--voice-bits-per-sample", "16"], 1);
            Assert(t.Requests().Count == 3 && await HashAsync(t.State) == before && (await t.LoadAsync()).Outbox.Single().ClientId == first.ClientId,
                "A replay or changed metadata uploaded, resent or changed the durable accepted record.");
            var unknown = await t.LoadAsync(); unknown.Outbox.Single().Status = "Unknown"; await t.SaveAsync(unknown); var unknownBefore = await HashAsync(t.State);
            await RunAsync(t, "send-media", arguments, 1);
            Assert(t.Requests().Count == 3 && await HashAsync(t.State) == unknownBefore, "Unknown voice intent was repeated or altered.");
            t.AssertRequests("getuploadurl", "upload", "sendmessage");
        }
    }
    private static byte[] SyntheticVoicePcm()
    {
        var data = new byte[96000];
        for (var i = 0; i < data.Length / 2; i++) BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2), (short)(Math.Sin(i * Math.Tau * 440 / 24000) * 9000));
        return data;
    }
    private static byte[] SyntheticVoiceWave(byte[] data)
    {
        var wave = new byte[data.Length + 44]; "RIFF"u8.CopyTo(wave); BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(4), (uint)wave.Length - 8);
        "WAVEfmt "u8.CopyTo(wave.AsSpan(8)); BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(20), 1); BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(24), 24000); BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(28), 48000);
        BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(32), 2); BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(34), 16);
        "data"u8.CopyTo(wave.AsSpan(36)); BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(40), (uint)data.Length); data.CopyTo(wave, 44); return wave;
    }
}
