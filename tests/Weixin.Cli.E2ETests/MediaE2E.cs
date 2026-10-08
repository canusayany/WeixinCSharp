using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Weixin.Protocol;

internal static partial class CliSuite
{
    private const string CdnHost = "novac2c.cdn.weixin.qq.com";
    private const string UploadParameter = "fixture-upload-parameter";
    private const string DownloadParameter = "fixture-download-parameter";
    private static readonly byte[] DownloadKey = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
    private static readonly byte[] PngFixture = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6mAAAAABJRU5ErkJggg==");
    // Protocol test bytes: codec playback is a separate real-device acceptance item.
    private static readonly byte[] VideoFixture = Convert.FromHexString("000000206674797069736F6D0000020069736F6D69736F326D703431666978747572");
    private static readonly byte[] Mp3Fixture = [73, 68, 51, 4, 0, 0, 0, 0, 0, 0, 0xff, 0xfb, 0x90, 0, 0, 0, 0, 0];
    private static readonly byte[] SilkFixture = Encoding.ASCII.GetBytes("\u0002#!SILK_V3\nfixture-silk-payload");

    private static async Task MediaCasesAsync()
    {
        await Case("send_image_upload_encrypt_metadata_exact_plaintext", () => MediaUploadAsync("image", "fixture-image.png", PngFixture));
        await Case("send_video_upload_encrypt_metadata_exact_plaintext", () => MediaUploadAsync("video", "fixture-video.mp4", VideoFixture));
        await Case("send_file_upload_encrypt_metadata_exact_plaintext", () => MediaUploadAsync("file", "fixture-document.txt", Encoding.UTF8.GetBytes("fixture-file附件😀")));
        await Case("send_audio_mp3_file_attachment_preserves_filename_plaintext_and_octet_stream", () => MediaUploadAsync("audio", "fixture-audio.mp3", Mp3Fixture));
        await Case("send_audio_wave_file_attachment_preserves_original_format_and_extension", () => MediaUploadAsync("audio", "fixture-audio.wav", SyntheticVoiceWave(SyntheticVoicePcm())));
        await Case("audio_business_key_keeps_legacy_fingerprint_and_never_resends", AudioReplayAsync);
        await Case("audio_legacy_sent_unknown_and_sending_receipts_recover_without_http", AudioLegacyReceiptAsync);
        await Case("cdn_upload_retry_reuses_ciphertext_key_and_filekey", UploadRetryAsync);
        await Case("cdn_upload_4xx_never_starts_chat_send", UploadRejectedAsync);
        await Case("upload_response_unknown_cdn_host_rejected", UploadHostGuardAsync);
        await Case("media_size_guard_before_http", MediaInputGuardsAsync);
        await Case("kill_media_chat_send_preserves_durable_unknown_intent", MediaKillAsync);
        await Case("media_business_key_skips_upload_send_and_rejects_changed_file", MediaReplayAsync);
        await Case("listen_download_image_voice_file_video_exact_plaintext", DownloadAllKindsAsync);
        await Case("corrupt_cdn_download_preserves_inbox_then_recovers", DownloadFailureRecoveryAsync);
        await Case("download_existing_target_conflict_never_overwritten", DownloadConflictAsync);
        await Case("download_unknown_host_keeps_message_without_cdn_request", DownloadHostGuardAsync);
        await Case("cdn403_retains_inbox_and_notifies_stop_without_session_expiry", Download403Async);
        await Case("plaintext_image_without_aes_key_downloads_exact_bytes", PlainImageAsync);
        await Case("plaintext_image_download_still_enforces_byte_limit", PlainImageLimitAsync);
        await Case("markdown_filter_utf16_chunks_replay_no_resend", MarkdownAsync);
        await Case("markdown_partial_unknown_chunk_never_resent", MarkdownUnknownAsync);
    }

    private static async Task MediaUploadAsync(string kind, string filename, byte[] plaintext)
    {
        var mediaKind = Kind(kind);
        var t = await TestDirectory.CreateAsync([UploadUrlStep(plaintext, mediaKind), UploadStep(plaintext), MediaSendStep(mediaKind, plaintext.Length, filename)]);
        await t.SaveAsync(State());
        var path = Path.Combine(t.Directory, filename); await File.WriteAllBytesAsync(path, plaintext);
        await RunAsync(t, "send-media", ["--file", path, "--kind", kind], 0);
        Assert((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(plaintext), "Media upload changed the original file bytes.");
        var state = await t.LoadAsync();
        Assert(state.Outbox.Count == 1 && state.Outbox[0].Status == "Sent", "Media send did not record exactly one acknowledged intent.");
        var wire = t.Requests().Single(x => Endpoint(x) == "sendmessage").GetProperty("body").GetProperty("msg");
        Assert(wire.GetProperty("client_id").GetString() == state.Outbox[0].ClientId, "Media wire client_id differs from durable receipt.");
        var assertions = t.Trace().Where(x => x.GetProperty("phase").GetString() == "fixture-assertion").ToList();
        Assert(assertions.Any(x => x.TryGetProperty("binary", out var b) && b.ValueKind == JsonValueKind.Object && b.GetProperty("plaintextVerified").GetBoolean()),
            "CDN upload was not decrypted and compared against fixture plaintext.");
        Assert(assertions.Any(x => x.TryGetProperty("media", out var m) && m.ValueKind == JsonValueKind.Object && m.GetProperty("aesKeyVerified").GetBoolean()),
            "Sent descriptor AES key was not compared with the captured upload key.");
        if (kind == "audio")
        {
            Assert(t.Requests().Single(x => Endpoint(x) == "getuploadurl").GetProperty("body").GetProperty("media_type").GetInt32() == 3,
                "Audio upload did not use FILE media_type=3.");
            var item = wire.GetProperty("item_list")[0];
            Assert(item.GetProperty("type").GetInt32() == 4 && item.GetProperty("file_item").GetProperty("file_name").GetString() == filename,
                "Audio attachment did not use FILE item type=4 with the original filename and extension.");
            var binary = assertions.Single(x => x.TryGetProperty("binary", out var b) && b.ValueKind == JsonValueKind.Object).GetProperty("binary");
            Assert(binary.GetProperty("plaintextSha256").GetString() == Convert.ToHexString(SHA256.HashData(plaintext)) &&
                binary.GetProperty("plaintextBytes").GetInt32() == plaintext.Length, "Audio upload converted or replaced original bytes.");
            var descriptor = assertions.Single(x => x.TryGetProperty("media", out var m) && m.ValueKind == JsonValueKind.Object).GetProperty("media");
            Assert(descriptor.GetProperty("voiceEncoding").ValueKind == JsonValueKind.Null &&
                !descriptor.GetProperty("voiceSampleRatePresent").GetBoolean() && !descriptor.GetProperty("voiceBitsPerSamplePresent").GetBoolean(),
                "Audio attachment acquired native voice metadata.");
            Assert(descriptor.GetProperty("itemType").GetInt32() == 4 && descriptor.GetProperty("descriptorName").GetString() == "file_item" &&
                !descriptor.GetProperty("voiceItemPresent").GetBoolean() && !descriptor.GetProperty("fileMimePresent").GetBoolean(),
                "Actual audio descriptor was not a FILE or acquired native voice/audio MIME fields.");
            Assert(t.Requests().Single(x => Endpoint(x) == "upload").GetProperty("contentType").GetString() == "application/octet-stream",
                "Encrypted audio upload did not use application/octet-stream.");
            Assert(Directory.GetFiles(t.Directory, "fixture-audio.*").Select(Path.GetFileName).SequenceEqual([filename]),
                "Audio upload created a converted or renamed sibling file.");
        }
        var traceText = File.ReadAllText(Path.Combine(t.Directory, "fixture-trace.jsonl"));
        Assert(!traceText.Contains("\"aeskey\"", StringComparison.Ordinal) && !traceText.Contains("\"aes_key\"", StringComparison.Ordinal), "Trace contains dynamic upload AES keys.");
        t.AssertRequests("getuploadurl", "upload", "sendmessage");
    }

    private static MediaKind Kind(string kind) => kind switch { "image" => MediaKind.Image, "video" => MediaKind.Video,
        "file" or "audio" => MediaKind.File, _ => throw new ArgumentException("Unknown fixture media kind.") };
    private static Step UploadUrlStep(byte[] plaintext, MediaKind kind) => new()
    {
        Path = "/ilink/bot/getuploadurl", RequireBearer = true,
        BodySubset = new { media_type = (int)kind, to_user_id = User, rawsize = plaintext.Length,
            rawfilemd5 = Convert.ToHexString(MD5.HashData(plaintext)).ToLowerInvariant(),
            filesize = (plaintext.Length / 16 + 1) * 16, no_need_thumb = true },
        Body = new { ret = 0, upload_param = UploadParameter }
    };
    private static Step UploadStep(byte[] plaintext, int captureStep = 0) => new()
    {
        Host = CdnHost, Path = "/c2c/upload", ContentType = "application/octet-stream",
        Query = new() { ["encrypted_query_param"] = UploadParameter, ["filekey"] = "$uploadFileKey:" + captureStep },
        ExpectedPlaintextBase64 = Convert.ToBase64String(plaintext), AesKeyStep = captureStep,
        ResponseHeaders = new() { ["x-encrypted-param"] = DownloadParameter }, BodyBase64 = ""
    };
    private static Step MediaSendStep(MediaKind kind, int length, string filename)
    {
        var media = new { encrypt_query_param = DownloadParameter, encrypt_type = 1 };
        object item = kind switch
        {
            MediaKind.Image => new { type = 2, image_item = new { media, mid_size = (length / 16 + 1) * 16 } },
            MediaKind.Video => new { type = 5, video_item = new { media, video_size = (length / 16 + 1) * 16 } },
            MediaKind.File => new { type = 4, file_item = new { media, file_name = filename, len = length.ToString(System.Globalization.CultureInfo.InvariantCulture) } },
            _ => throw new ArgumentException("Unknown media fixture.")
        };
        return new() { Path = "/ilink/bot/sendmessage", RequireBearer = true, AssertSentAesKeyStep = 0,
            BodySubset = new { msg = new { from_user_id = "", to_user_id = User, message_type = 2, message_state = 2,
                context_token = Context, item_list = new[] { item } } }, Body = new { ret = 0 } };
    }

    private static async Task UploadRetryAsync()
    {
        var bytes = Encoding.UTF8.GetBytes("fixture-upload-retry");
        var failed = UploadStep(bytes); failed.Status = 500;
        var t = await TestDirectory.CreateAsync([UploadUrlStep(bytes, MediaKind.File), failed, UploadStep(bytes), MediaSendStep(MediaKind.File, bytes.Length, "fixture-retry.txt")]);
        await t.SaveAsync(State()); var file = Path.Combine(t.Directory, "fixture-retry.txt"); await File.WriteAllBytesAsync(file, bytes);
        await RunAsync(t, "send-media", ["--file", file, "--kind", "file"], 0);
        Assert((await t.LoadAsync()).Outbox.Single().Status == "Sent", "Upload retry prevented the one final chat send.");
        var binary = t.Trace().Where(x => x.GetProperty("phase").GetString() == "fixture-assertion" &&
            x.TryGetProperty("binary", out var b) && b.ValueKind == JsonValueKind.Object).ToList();
        Assert(binary.Count == 2 && binary.All(x => x.GetProperty("binary").GetProperty("aesKeyStep").GetInt32() == 0),
            "Upload retries did not reuse the same captured AES key.");
        Assert(binary[0].GetProperty("binary").GetProperty("ciphertextSha256").GetString() ==
            binary[1].GetProperty("binary").GetProperty("ciphertextSha256").GetString(), "Upload retries changed ciphertext bytes.");
        t.AssertRequests("getuploadurl", "upload", "upload", "sendmessage");
    }

    private static async Task UploadRejectedAsync()
    {
        var bytes = Encoding.UTF8.GetBytes("fixture-rejected-upload"); var upload = UploadStep(bytes); upload.Status = 400;
        var t = await TestDirectory.CreateAsync([UploadUrlStep(bytes, MediaKind.File), upload]);
        await t.SaveAsync(State()); var file = Path.Combine(t.Directory, "fixture-rejected.txt"); await File.WriteAllBytesAsync(file, bytes);
        await RunAsync(t, "send-media", ["--file", file, "--kind", "file"], 7);
        Assert((await t.LoadAsync()).Outbox.Count == 0, "Rejected upload started a chat send intent.");
        t.AssertRequests("getuploadurl", "upload");
    }

    private static async Task UploadHostGuardAsync()
    {
        var bytes = Encoding.UTF8.GetBytes("fixture-bad-host"); var upload = UploadUrlStep(bytes, MediaKind.File);
        upload.Body = new { ret = 0, upload_full_url = "https://example.com/c2c/upload" };
        var t = await TestDirectory.CreateAsync([upload]); await t.SaveAsync(State());
        var file = Path.Combine(t.Directory, "fixture-host.txt"); await File.WriteAllBytesAsync(file, bytes);
        await RunAsync(t, "send-media", ["--file", file, "--kind", "file"], 1);
        Assert((await t.LoadAsync()).Outbox.Count == 0, "Unexpected CDN host started a chat send.");
        t.AssertRequests("getuploadurl");
    }

    private static async Task MediaInputGuardsAsync()
    {
        var t = await TestDirectory.CreateAsync([]); await t.SaveAsync(State());
        var file = Path.Combine(t.Directory, "fixture-over-limit.bin"); await File.WriteAllBytesAsync(file, new byte[1024 * 1024 + 1]);
        await RunAsync(t, "send-media", ["--file", file, "--kind", "file", "--max-media-mib", "1"], 1);
        Assert((await t.LoadAsync()).Outbox.Count == 0 && t.Requests().Count == 0, "Invalid local media input reached HTTP or created send intent.");
    }

    private static async Task MediaKillAsync()
    {
        var bytes = Encoding.UTF8.GetBytes("fixture-media-kill");
        var send = MediaSendStep(MediaKind.File, bytes.Length, "fixture-kill.txt"); send.DelayMs = -1; send.WaitMarker = "media-send-entered.marker";
        var t = await TestDirectory.CreateAsync([UploadUrlStep(bytes, MediaKind.File), UploadStep(bytes), send]);
        await t.SaveAsync(State()); var file = Path.Combine(t.Directory, "fixture-kill.txt"); await File.WriteAllBytesAsync(file, bytes);
        var businessKey = Convert.ToHexString(SHA256.HashData("fixture-media-kill-business"u8));
        var args = new[] { "--file", file, "--kind", "file", "--source-key", businessKey };
        using (var child = Start(t, "send-media", args))
        {
            await WaitFileAsync(Path.Combine(t.Directory, "media-send-entered.marker"), child, TimeSpan.FromSeconds(8));
            var snapshot = Path.Combine(t.Directory, "fixture-media-snapshot.dpapi"); File.Copy(t.State, snapshot);
            using var vault = new StateVault(snapshot);
            Assert((await vault.LoadAsync<BotState>())!.Outbox.Single().Status == "Sending", "Media send began before a durable Sending receipt.");
            await child.KillAsync();
        }
        await t.RewriteAsync([]); await RunAsync(t, "status", [], 0);
        Assert((await t.LoadAsync()).Outbox.Single().Status == "Unknown", "Killed media send did not recover to Unknown.");
        await RunAsync(t, "send-media", args, 1);
        Assert((await t.LoadAsync()).Outbox.Count == 1, "Unknown media replay added another attempt.");
        t.AssertRequests("getuploadurl", "upload", "sendmessage");
    }

    private static async Task MediaReplayAsync()
    {
        var bytes = Encoding.UTF8.GetBytes("fixture-media-replay");
        var t = await TestDirectory.CreateAsync([UploadUrlStep(bytes, MediaKind.File), UploadStep(bytes), MediaSendStep(MediaKind.File, bytes.Length, "fixture-replay.txt")]);
        await t.SaveAsync(State()); var file = Path.Combine(t.Directory, "fixture-replay.txt"); await File.WriteAllBytesAsync(file, bytes);
        var key = Convert.ToHexString(SHA256.HashData("fixture-media-replay-business"u8));
        var args = new[] { "--file", file, "--kind", "file", "--source-key", key };
        await RunAsync(t, "send-media", args, 0);
        var receipt = (await t.LoadAsync()).Outbox.Single();
        Assert(receipt.PayloadSha256?.Length == 64, "Source file/metadata fingerprint was not persisted.");
        await t.RewriteAsync([]); await RunAsync(t, "send-media", args, 0);
        Assert((await t.LoadAsync()).Outbox.Single().ClientId == receipt.ClientId, "Acknowledged media replay changed client_id.");
        await File.WriteAllTextAsync(file, "fixture-changed-media", new UTF8Encoding(false));
        await RunAsync(t, "send-media", args, 1);
        t.AssertRequests("getuploadurl", "upload", "sendmessage");
    }

    private static string LegacyAudioFingerprint(byte[] bytes, string filename) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{Convert.ToHexString(SHA256.HashData(bytes))}\n3\n\n\n{filename}")));

    private static async Task AudioReplayAsync()
    {
        const string filename = "fixture-audio-replay.mp3";
        var t = await TestDirectory.CreateAsync([UploadUrlStep(Mp3Fixture, MediaKind.File), UploadStep(Mp3Fixture),
            MediaSendStep(MediaKind.File, Mp3Fixture.Length, filename)]);
        await t.SaveAsync(State()); var file = Path.Combine(t.Directory, filename); await File.WriteAllBytesAsync(file, Mp3Fixture);
        var key = Convert.ToHexString(SHA256.HashData("fixture-audio-replay-business"u8));
        string[] args = ["--file", file, "--kind", "audio", "--source-key", key];
        var sent = await RunAsync(t, "send-media", args, 0);
        Assert(sent.Stdout.Contains("音频文件附件 API 已接受", StringComparison.Ordinal), "Audio success output did not identify FILE API acceptance.");
        var receipt = (await t.LoadAsync()).Outbox.Single(); var before = await HashAsync(t.State);
        Assert(receipt.PayloadSha256 == LegacyAudioFingerprint(Mp3Fixture, filename), "Audio changed the legacy FILE fingerprint's empty duration/encoding fields.");
        await t.RewriteAsync([]);
        await RunAsync(t, "send-media", args, 0);
        await RunAsync(t, "send-media", ["--file", file, "--kind", "file", "--source-key", key], 0);
        Assert(await HashAsync(t.State) == before && (await t.LoadAsync()).Outbox.Single().ClientId == receipt.ClientId,
            "Audio/file alias replay altered the accepted receipt.");
        var renamed = Path.Combine(t.Directory, "fixture-renamed-audio.mp3"); await File.WriteAllBytesAsync(renamed, Mp3Fixture);
        await RunAsync(t, "send-media", ["--file", renamed, "--kind", "audio", "--source-key", key], 1);
        await File.WriteAllBytesAsync(file, [.. Mp3Fixture, 1]);
        await RunAsync(t, "send-media", args, 1);
        Assert(await HashAsync(t.State) == before, "Changed audio filename or bytes altered the accepted receipt.");
        t.AssertRequests("getuploadurl", "upload", "sendmessage");
    }

    private static async Task AudioLegacyReceiptAsync()
    {
        // Seed the pre-change FILE fingerprint directly, proving upgrade compatibility
        // independently of the new executable's own fingerprint implementation.
        foreach (var priorStatus in new[] { "Sent", "Unknown", "Sending" })
        {
            const string filename = "fixture-legacy-audio.mp3";
            var t = await TestDirectory.CreateAsync([]); var file = Path.Combine(t.Directory, filename);
            await File.WriteAllBytesAsync(file, Mp3Fixture);
            var key = Convert.ToHexString(SHA256.HashData("fixture-legacy-audio-business"u8));
            var state = State();
            state.Outbox.Add(new SendReceipt { ClientId = "fixture-legacy-audio-client", SourceKey = key,
                ContentSha256 = Convert.ToHexString(SHA256.HashData("fixture-legacy-file-descriptor"u8)),
                PayloadSha256 = LegacyAudioFingerprint(Mp3Fixture, filename), Status = priorStatus,
                AttemptedAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
            await t.SaveAsync(state);
            if (priorStatus == "Sending")
            {
                await RunAsync(t, "status", [], 0);
                Assert((await t.LoadAsync()).Outbox.Single().Status == "Unknown", "Legacy in-flight audio was not recovered to Unknown.");
            }
            var before = await HashAsync(t.State);
            await RunAsync(t, "send-media", ["--file", file, "--kind", "audio", "--source-key", key], priorStatus == "Sent" ? 0 : 1);
            var receipt = (await t.LoadAsync()).Outbox.Single();
            Assert(await HashAsync(t.State) == before && receipt.ClientId == "fixture-legacy-audio-client" &&
                receipt.Status == (priorStatus == "Sent" ? "Sent" : "Unknown"), "Legacy audio replay altered a completed/uncertain receipt.");
            t.AssertRequests();
        }
    }

    private static InboundMessage IncomingMedia(string id, int type, string parameter, string? filename = null)
    {
        var msg = Message(id, "", "fixture-media-context");
        var media = new CdnMedia { EncryptQueryParam = parameter, AesKey = Convert.ToBase64String(DownloadKey), EncryptType = 1 };
        MessageItem item = type switch
        {
            2 => new() { Type = 2, ImageItem = new() { Media = media, AesKey = Convert.ToHexString(DownloadKey).ToLowerInvariant() } },
            3 => new() { Type = 3, VoiceItem = new() { Media = media, EncodeType = 7, Playtime = 1234, Text = "fixture-transcript" } },
            4 => new() { Type = 4, FileItem = new() { Media = media, FileName = filename ?? "fixture-download.txt" } },
            5 => new() { Type = 5, VideoItem = new() { Media = media } },
            _ => throw new ArgumentException("Unknown inbound fixture kind.")
        };
        msg.Items = [item]; return msg;
    }
    private static Step DownloadStep(string parameter, byte[] plaintext) => new()
    {
        Host = CdnHost, Method = "GET", Path = "/c2c/download", ContentType = "application/octet-stream",
        Query = new() { ["encrypted_query_param"] = parameter }, BodyBase64 = Convert.ToBase64String(EncryptFixture(plaintext))
    };
    private static byte[] EncryptFixture(byte[] plaintext)
    { using var aes = Aes.Create(); aes.Key = DownloadKey; return aes.EncryptEcb(plaintext, PaddingMode.PKCS7); }
    private static string DownloadName(InboundMessage msg, string extension) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(PersistentBotRunner.MessageKey(msg) + "\nitem:0"))).ToLowerInvariant() + extension;

    private static async Task DownloadAllKindsAsync()
    {
        var image = IncomingMedia("fixture-in-image", 2, "fixture-in-image-param");
        // image.aeskey must take precedence over this deliberately different media.aes_key.
        image.Items![0].ImageItem!.Media!.AesKey = Convert.ToBase64String(new byte[16]);
        var voice = IncomingMedia("fixture-in-voice", 3, "fixture-in-voice-param");
        var file = IncomingMedia("fixture-in-file", 4, "fixture-in-file-param", "../../fixture-download.txt");
        var video = IncomingMedia("fixture-in-video", 5, "fixture-in-video-param");
        // Also exercise the official base64-of-ASCII-hex compatibility representation.
        video.Items![0].VideoItem!.Media!.AesKey = Convert.ToBase64String(Encoding.ASCII.GetBytes(Convert.ToHexString(DownloadKey).ToLowerInvariant()));
        var other = IncomingMedia("fixture-other-media", 2, "fixture-other-param"); other.FromUserId = "fixture-other-owner";
        var fileBytes = Encoding.UTF8.GetBytes("fixture-downloaded内容");
        var t = await TestDirectory.CreateAsync([Notify(true), Updates([other, image, voice, file, video], "fixture-media-cursor"),
            DownloadStep("fixture-in-image-param", PngFixture), DownloadStep("fixture-in-voice-param", Mp3Fixture),
            DownloadStep("fixture-in-file-param", fileBytes), DownloadStep("fixture-in-video-param", VideoFixture), LongPoll(), Notify(false)]);
        await t.SaveAsync(State()); var directory = Path.Combine(t.Directory, "fixture-downloads");
        var result = await RunAsync(t, "listen", ["--download-dir", directory, "--run-for", "2"], 0);
        Assert(result.Stdout.Contains("fixture-transcript", StringComparison.Ordinal), "Voice transcription metadata did not reach CLI.");
        foreach (var value in new (InboundMessage Message, string Extension, byte[] Bytes)[] { (image, ".image", PngFixture), (voice, ".mp3", Mp3Fixture),
                     (file, ".txt", fileBytes), (video, ".mp4", VideoFixture) })
            Assert((await File.ReadAllBytesAsync(Path.Combine(directory, DownloadName(value.Message, value.Extension)))).SequenceEqual(value.Bytes),
                "Downloaded fixture plaintext differs for " + value.Extension);
        Assert(Directory.GetFiles(directory).Length == 4 && !Directory.GetFiles(directory, "*.tmp").Any(), "Download created unexpected files or temporary remnants.");
        var state = await t.LoadAsync();
        Assert(state.Inbox.Count == 0 && state.SeenKeys.Count == 4 && state.Outbox.Count == 0 && state.Session.Cursor == "fixture-media-cursor",
            "Mixed media inbox/owner filtering failed.");
        t.AssertRequests("notifystart", "getupdates", "download", "download", "download", "download", "getupdates", "notifystop");
    }

    private static async Task DownloadFailureRecoveryAsync()
    {
        var message = IncomingMedia("fixture-download-retry", 4, "fixture-retry-download-param");
        var plaintext = Encoding.UTF8.GetBytes("fixture-recovered-download");
        var broken = DownloadStep("fixture-retry-download-param", plaintext); broken.BodyBase64 = Convert.ToBase64String(new byte[5]);
        var t = await TestDirectory.CreateAsync([Notify(true), Updates([message], "fixture-download-retry-cursor"), broken, Notify(false)]);
        await t.SaveAsync(State()); var directory = Path.Combine(t.Directory, "fixture-downloads");
        await RunAsync(t, "listen", ["--download-dir", directory, "--run-for", "2"], 4);
        Assert((await t.LoadAsync()).Inbox.Count == 1 && (!Directory.Exists(directory) || Directory.GetFiles(directory).Length == 0),
            "Failed decryption acknowledged pending input or saved plaintext.");
        await t.RewriteAsync([DownloadStep("fixture-retry-download-param", plaintext), Notify(true), LongPoll(), Notify(false)]);
        await RunAsync(t, "listen", ["--download-dir", directory, "--run-for", "1.2"], 0);
        Assert((await File.ReadAllBytesAsync(Path.Combine(directory, DownloadName(message, ".txt")))).SequenceEqual(plaintext) &&
            (await t.LoadAsync()).Inbox.Count == 0, "Pending download did not recover exactly on restart.");
        t.AssertNoRejections();
    }

    private static async Task DownloadConflictAsync()
    {
        var message = IncomingMedia("fixture-download-conflict", 4, "fixture-conflict-param");
        var t = await TestDirectory.CreateAsync([Notify(true), Updates([message], "fixture-conflict-cursor"),
            DownloadStep("fixture-conflict-param", Encoding.UTF8.GetBytes("fixture-new-content")), Notify(false)]);
        await t.SaveAsync(State()); var directory = Path.Combine(t.Directory, "fixture-downloads"); Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, DownloadName(message, ".txt")); await File.WriteAllTextAsync(target, "fixture-existing-content", new UTF8Encoding(false));
        var before = await HashAsync(target);
        await RunAsync(t, "listen", ["--download-dir", directory, "--run-for", "2"], 4);
        Assert(await HashAsync(target) == before && (await t.LoadAsync()).Inbox.Count == 1, "Conflicting target was overwritten or pending input discarded.");
        Assert(!Directory.GetFiles(directory, "*.tmp").Any(), "Conflict left temporary plaintext.");
        t.AssertRequests("notifystart", "getupdates", "download", "notifystop");
    }

    private static async Task DownloadHostGuardAsync()
    {
        var message = IncomingMedia("fixture-download-host", 4, "fixture-host-param");
        message.Items![0].FileItem!.Media!.FullUrl = "https://example.com/private-download";
        var t = await TestDirectory.CreateAsync([Notify(true), Updates([message], "fixture-host-cursor"), Notify(false)]);
        await t.SaveAsync(State());
        await RunAsync(t, "listen", ["--download-dir", Path.Combine(t.Directory, "fixture-downloads"), "--run-for", "2"], 1);
        Assert((await t.LoadAsync()).Inbox.Count == 1, "Unapproved download host discarded pending input.");
        t.AssertRequests("notifystart", "getupdates", "notifystop");
    }

    private static async Task Download403Async()
    {
        var message = IncomingMedia("fixture-download-403", 4, "fixture-403-param");
        var denied = DownloadStep("fixture-403-param", "fixture-403-body"u8.ToArray()); denied.Status = 403;
        var t = await TestDirectory.CreateAsync([Notify(true), Updates([message], "fixture-403-cursor"), denied, Notify(false)]);
        await t.SaveAsync(State());
        var result = await RunAsync(t, "listen", ["--download-dir", Path.Combine(t.Directory, "fixture-downloads"), "--run-for", "2"], 7);
        var state = await t.LoadAsync();
        Assert(state.Inbox.Count == 1 && state.Session.BotToken == Token && state.Session.Cursor == "fixture-403-cursor", "CDN403 lost input or changed bot session.");
        Assert(!result.Stderr.Contains("会话失效", StringComparison.Ordinal), "CDN403 was incorrectly reported as bot session expiry.");
        t.AssertRequests("notifystart", "getupdates", "download", "notifystop");
    }

    private static InboundMessage PlainImage(string id, string parameter)
    {
        var image = IncomingMedia(id, 2, parameter);
        image.Items![0].ImageItem!.AesKey = null;
        image.Items[0].ImageItem!.Media!.AesKey = null;
        return image;
    }
    private static async Task PlainImageAsync()
    {
        var message = PlainImage("fixture-plain-image", "fixture-plain-image-param");
        var response = DownloadStep("fixture-plain-image-param", PngFixture); response.BodyBase64 = Convert.ToBase64String(PngFixture);
        var t = await TestDirectory.CreateAsync([Notify(true), Updates([message], "fixture-plain-cursor"), response, LongPoll(), Notify(false)]);
        await t.SaveAsync(State()); var directory = Path.Combine(t.Directory, "fixture-downloads");
        await RunAsync(t, "listen", ["--download-dir", directory, "--run-for", "1.2"], 0);
        Assert((await File.ReadAllBytesAsync(Path.Combine(directory, DownloadName(message, ".image")))).SequenceEqual(PngFixture) &&
            (await t.LoadAsync()).Inbox.Count == 0, "Unencrypted image bytes were changed or pending input not acknowledged.");
        t.AssertRequests("notifystart", "getupdates", "download", "getupdates", "notifystop");
    }
    private static async Task PlainImageLimitAsync()
    {
        var message = PlainImage("fixture-plain-image-large", "fixture-plain-large-param");
        var response = DownloadStep("fixture-plain-large-param", PngFixture); response.BodyBase64 = Convert.ToBase64String(new byte[1024 * 1024 + 1]);
        var t = await TestDirectory.CreateAsync([Notify(true), Updates([message], "fixture-plain-large-cursor"), response, Notify(false)]);
        await t.SaveAsync(State()); var directory = Path.Combine(t.Directory, "fixture-downloads");
        await RunAsync(t, "listen", ["--download-dir", directory, "--max-media-mib", "1", "--run-for", "2"], 4);
        Assert((await t.LoadAsync()).Inbox.Count == 1 && (!Directory.Exists(directory) || Directory.GetFiles(directory).Length == 0),
            "Plain image bypassed its byte limit or acknowledged rejected content.");
        t.AssertRequests("notifystart", "getupdates", "download", "notifystop");
    }

    private static (string Input, string First, string Second) MarkdownFixture()
    {
        const string prefix = "fixture-title\n中文\n\n";
        var cjk = new string('测', 4000 - prefix.Length - 1);
        const string second = "😀fixture-tail";
        const string markup = "##### fixture-title\n*中文*\n![fixture-alt](https://example.com/fixture.png)\n";
        return (markup + cjk + second, prefix + cjk, second);
    }
    private static async Task MarkdownAsync()
    {
        var sample = MarkdownFixture();
        var t = await TestDirectory.CreateAsync([Send(sample.First), Send(sample.Second)]);
        await t.SaveAsync(State()); var file = Path.Combine(t.Directory, "fixture-document.md"); await File.WriteAllTextAsync(file, sample.Input, new UTF8Encoding(false));
        var businessKey = Convert.ToHexString(SHA256.HashData("fixture-markdown-business"u8));
        var args = new[] { "--markdown", "--text-file", file, "--source-key", businessKey };
        await RunAsync(t, "send", args, 0);
        var state = await t.LoadAsync();
        Assert(state.Outbox.Count == 2 && state.Outbox.All(x => x.Status == "Sent"), "Markdown chunks were not acknowledged exactly once.");
        var ids = state.Outbox.Select(x => x.ClientId).ToArray();
        var actual = t.Requests().Select(x => x.GetProperty("body").GetProperty("msg").GetProperty("item_list")[0].GetProperty("text_item").GetProperty("text").GetString()!).ToArray();
        Assert(actual.SequenceEqual([sample.First, sample.Second]) && actual[0].Length == 3999 && actual[1].StartsWith("😀", StringComparison.Ordinal),
            "Markdown filter/chunking split the emoji or applied a UTF8-byte cap.");
        await t.RewriteAsync([]); await RunAsync(t, "send", args, 0);
        Assert((await t.LoadAsync()).Outbox.Select(x => x.ClientId).SequenceEqual(ids), "Markdown replay changed client IDs.");
        await File.WriteAllTextAsync(file, "fixture-changed-markdown", new UTF8Encoding(false));
        await RunAsync(t, "send", args, 1);
        t.AssertRequests("sendmessage", "sendmessage");
    }
    private static async Task MarkdownUnknownAsync()
    {
        var sample = MarkdownFixture(); var unknown = Send(sample.Second); unknown.Body = null; unknown.RawBody = "{";
        var t = await TestDirectory.CreateAsync([Send(sample.First), unknown]); await t.SaveAsync(State());
        var file = Path.Combine(t.Directory, "fixture-document.md"); await File.WriteAllTextAsync(file, sample.Input, new UTF8Encoding(false));
        var key = Convert.ToHexString(SHA256.HashData("fixture-markdown-unknown"u8));
        var args = new[] { "--markdown", "--text-file", file, "--source-key", key };
        await RunAsync(t, "send", args, 3);
        var state = await t.LoadAsync();
        Assert(state.Outbox.Count == 2 && state.Outbox[0].Status == "Sent" && state.Outbox[1].Status == "Unknown", "Partial Markdown unknown state differs.");
        await t.RewriteAsync([]); await RunAsync(t, "send", args, 1);
        Assert((await t.LoadAsync()).Outbox.Count == 2, "Unknown Markdown chunk was retried.");
        t.AssertRequests("sendmessage", "sendmessage");
    }
}
