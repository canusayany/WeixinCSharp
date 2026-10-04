using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Weixin.Protocol;

// Opt-in live-account test. No credentials, identifiers or arbitrary chat content are recorded.
var replayInboundVoice = args.Count(a => a == "--replay-inbound-voice") == 1;
if (args.Count(a => a == "--replay-inbound-voice") > 1) throw new ArgumentException("Duplicate replay mode flag.");
var options = args.Where(a => a != "--replay-inbound-voice").Chunk(2)
    .ToDictionary(p => p.Length == 2 ? p[0] : throw new ArgumentException("Missing value"), p => p[1]);
var statePath = Path.GetFullPath(options["--state"]);
var output = Path.GetFullPath(options["--output"]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var marker = options.GetValueOrDefault("--marker");
var exe = options.GetValueOrDefault("--exe");
var downloadDirectory = options.GetValueOrDefault("--download-dir");
var expectedMedia = options.GetValueOrDefault("--expect-media", "").Split(',', StringSplitOptions.RemoveEmptyEntries);
if (expectedMedia.Any(k => k is not ("voice" or "video"))) throw new ArgumentException("Expected media must be voice or video.");
if (expectedMedia.Length > 0 && exe is null) throw new ArgumentException("Expected media requires a listening executable.");
var businessKeys = options.GetValueOrDefault("--business-keys", "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
if (replayInboundVoice)
{
    if (exe is not null || marker is not null || expectedMedia.Length > 0 || businessKeys.Count > 0 || options.ContainsKey("--native-silk"))
        throw new ArgumentException("Inbound voice replay cannot be combined with other live test modes.");
    return await ReplayInboundVoiceAsync(options, statePath, output, downloadDirectory);
}
var observed = false;
var otherMessageCount = 0;
int? exitCode = null;
var started = DateTimeOffset.UtcNow;
if (options.GetValueOrDefault("--native-silk") is { } silkPath)
{
    if (exe is not null) throw new ArgumentException("Do not combine live native send and listen.");
    var sourceKey = options.GetValueOrDefault("--source-key") ?? throw new ArgumentException("Explicit business key required.");
    if (sourceKey.Length != 64 || !sourceKey.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid business key.");
    using var sendVault = new StateVault(statePath);
    var sendState = await sendVault.LoadAsync<BotState>() ?? throw new InvalidOperationException("No live binding.");
    if (sendState.TransportMode != "live") throw new InvalidOperationException("Explicit live binding required.");
    using var api = new ILinkClient();
    var runner = new PersistentBotRunner(api, sendVault, sendState);
    runner.ValidateState();
    await runner.RecoverAsync();
    if (runner.State.Outbox.Any(r => r.SourceKey == sourceKey))
        throw new InvalidOperationException("This native test business key was already attempted; no upload or resend.");
    var codec = new VoiceCodec();
    await using var input = File.OpenRead(silkPath);
    if (input.Length <= 0 || input.Length > codec.MaxInputBytes) throw new ArgumentException("Invalid local SILK sample size.");
    var encoded = new byte[(int)input.Length];
    await input.ReadExactlyAsync(encoded);
    VoiceCodecResult? decoded = null;
    try
    {
        decoded = await codec.DecodeSilkToWaveAsync(encoded);
        using var media = new MediaClient(api);
        var uploaded = await media.UploadBoundFileAsync(runner.State.Session, silkPath, MediaKind.Voice, decoded.DurationMilliseconds);
        var item = uploaded.ToMessageItem(decoded.DurationMilliseconds, 6, 24000);
        item.VoiceItem!.BitsPerSample = 16;
        var receipt = await runner.SendBoundItemAsync(item, sourceKey);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            formatVersion = 1, testLevel = "live-WeChat-account-SDK-native-SILK-full-metadata", started,
            completed = DateTimeOffset.UtcNow, sampleBytes = encoded.Length,
            sampleSha256 = Convert.ToHexString(SHA256.HashData(encoded)).ToLowerInvariant(),
            playtimeMilliseconds = decoded.DurationMilliseconds, sampleRateHz = 24000, bitsPerSample = 16, encodeType = 6,
            aesKeyFormat = "base64-ASCII-hex-as-current-official-media-senders",
            sourceKey, receipt.ClientId, receipt.Status, receipt.ContentSha256,
            protocolDllSha256 = await HashFileAsync(typeof(ILinkClient).Assembly.Location),
            clientDelivery = "requires independent client observation", containsCredentialsOrAccountIds = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Full-metadata SILK request accepted by API; verify the native bubble in the WeChat client.");
        return 0;
    }
    finally
    {
        CryptographicOperations.ZeroMemory(encoded);
        if (decoded is not null) CryptographicOperations.ZeroMemory(decoded.Data);
    }
}
if (exe is not null)
{
    if (marker is null && expectedMedia.Length == 0 || marker is not null &&
        (marker.Length is < 3 or > 64 || marker.Any(char.IsControl)))
        throw new ArgumentException("Explicit test marker or expected media is required.");
    if (expectedMedia.Length > 0 && (downloadDirectory is null || Directory.Exists(downloadDirectory) && Directory.EnumerateFileSystemEntries(downloadDirectory).Any()))
        throw new ArgumentException("Media-only verification requires an empty explicit download directory.");
    exe = Path.GetFullPath(exe);
    var duration = options.GetValueOrDefault("--seconds", "180");
    var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8 };
    foreach (var value in new[] { "listen", "--echo", "--state", statePath, "--run-for", duration }) start.ArgumentList.Add(value);
    if (downloadDirectory is not null)
    { start.ArgumentList.Add("--download-dir"); start.ArgumentList.Add(Path.GetFullPath(downloadDirectory)); }
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start live test.");
    Console.WriteLine("LIVE LISTENER READY; only the requested marker is recorded.");
    var stdout = ConsumeAsync(process.StandardOutput);
    var stderr = ConsumeAsync(process.StandardError);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(double.Parse(duration, System.Globalization.CultureInfo.InvariantCulture) + 30));
    try { await process.WaitForExitAsync(deadline.Token); }
    catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
    await Task.WhenAll(stdout, stderr); exitCode = process.ExitCode;
}
using var vault = new StateVault(statePath);
var state = await vault.LoadAsync<BotState>() ?? throw new InvalidOperationException("No live binding.");
if (state.TransportMode != "live") throw new InvalidOperationException("This tool requires an explicit live binding.");
var hash = marker is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("已收到：" + marker)));
var receipts = state.Outbox.Where(r => hash is not null && r.ContentSha256 == hash).Select(r => new
    { r.ClientId, r.Status, r.AttemptedAt, r.SourceKey, r.ContentSha256 }).ToArray();
var mediaFiles = new List<object>();
var mediaPaths = new List<string>();
if (downloadDirectory is not null && Directory.Exists(downloadDirectory))
    foreach (var path in Directory.GetFiles(downloadDirectory).Where(p => !p.EndsWith(".tmp", StringComparison.Ordinal)))
    {
        mediaPaths.Add(path);
        mediaFiles.Add(new { name = Path.GetFileName(path), bytes = new FileInfo(path).Length, sha256 = await HashFileAsync(path) });
    }
var expectedMediaObserved = expectedMedia.All(k => k switch
{
    "voice" => mediaPaths.Any(p => Path.GetExtension(p) == ".wav") && mediaPaths.Any(p => Path.GetExtension(p) is ".voice" or ".silk"),
    "video" => mediaPaths.Any(p => Path.GetExtension(p) == ".mp4"),
    _ => false
});
var evidence = new
{
    formatVersion = 1, testLevel = "live-WeChat-account-CLI", started, completed = DateTimeOffset.UtcNow,
    executableSha256 = exe is null ? null : await HashFileAsync(exe),
    cliDllSha256 = exe is null ? null : await HashFileAsync(Path.Combine(Path.GetDirectoryName(exe)!, "weixin.dll")),
    protocolDllSha256 = exe is null ? null : await HashFileAsync(Path.Combine(Path.GetDirectoryName(exe)!, "Weixin.Protocol.dll")),
    marker, expectedMessageObserved = observed, expectedMedia,
    expectedMediaObserved = expectedMedia.Length > 0 ? (bool?)expectedMediaObserved : null, otherMessageCount, exitCode,
    stateHasContext = !string.IsNullOrEmpty(state.Session.ContextToken), stateHasCursor = !string.IsNullOrEmpty(state.Session.Cursor),
    inboxCount = state.Inbox.Count, seenKeyCount = state.SeenKeys.Count,
    outboxStatuses = state.Outbox.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count()),
    expectedReplyReceipts = receipts, clientDelivery = "requires independent client observation",
    selectedBusinessReceipts = state.Outbox.Where(r => r.SourceKey is { } key && businessKeys.Contains(key))
        .Select(r => new { r.ClientId, r.Status, r.AttemptedAt, r.SourceKey, r.ContentSha256, r.PayloadSha256 }).ToArray(),
    selectedMarkdownJobs = state.MarkdownJobs.Where(j => businessKeys.Contains(j.SourceKey))
        .Select(j => new { j.SourceKey, j.ContentSha256, j.ChunkCount, completedChunks = j.CompletedChunks.Count }).ToArray(),
    downloadedMedia = mediaFiles,
    containsCredentialsOrAccountIds = false
};
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Evidence saved. Expected marker observed={observed}; matching accepted replies={receipts.Count(r => r.Status == "Sent")}; pending inbox={state.Inbox.Count}.");
return exe is null || exitCode == 0 && expectedMediaObserved &&
    (marker is null || observed && receipts.Count(r => r.Status == "Sent") == 1) ? 0 : 1;

async Task ConsumeAsync(StreamReader reader)
{
    string? line;
    while ((line = await reader.ReadLineAsync()) is not null)
    {
        if (!line.StartsWith('[')) continue;
        var closing = line.IndexOf("] ", StringComparison.Ordinal);
        if (closing < 0) continue;
        if (line[(closing + 2)..] == marker) { observed = true; Console.WriteLine("Expected live marker received."); }
        else otherMessageCount++;
    }
}
static async Task<string> HashFileAsync(string path)
{
    await using var stream = File.OpenRead(path);
    return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
}

// Opt-in bounded experiment. It reuses only the received, already-known VOICE/CDN
// fields; it never replays the incoming USER envelope or invents codec metadata.
static async Task<int> ReplayInboundVoiceAsync(Dictionary<string, string> options, string statePath,
    string output, string? downloadDirectory)
{
    var sourceKey = options.GetValueOrDefault("--source-key") ?? throw new ArgumentException("An explicit 64-hex source key is required.");
    if (sourceKey.Length != 64 || !sourceKey.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid replay source key.");
    sourceKey = sourceKey.ToLowerInvariant();
    if (downloadDirectory is null) throw new ArgumentException("An explicit empty private download directory is required.");
    downloadDirectory = Path.GetFullPath(downloadDirectory);
    if (Directory.Exists(downloadDirectory) && Directory.EnumerateFileSystemEntries(downloadDirectory).Any())
        throw new ArgumentException("Use a fresh empty private download directory.");
    if (File.Exists(output) || string.Equals(output, statePath, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Use a new evidence file distinct from the binding.");
    if (!double.TryParse(options.GetValueOrDefault("--seconds", "180"), System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds) || seconds is < 10 or > 180)
        throw new ArgumentException("Replay budget must be 10..180 seconds.");
    // Reserve five seconds inside the total budget for a best-effort listener stop.
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    using var work = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
    work.CancelAfter(TimeSpan.FromSeconds(seconds - 5));
    var started = DateTimeOffset.UtcNow;
    var phase = "binding"; var outcome = "not-started";
    var selected = false; var handlerCompleted = false; var sendAttemptStarted = false;
    string? selectedInboxKey = null;
    var lifecycleMayNeedStop = false; var sessionUsable = true; var stopConfirmed = false;
    var ignoredMessageCount = 0; int? httpStatus = null;
    object? metadata = null; string? payloadSha256 = null; int? payloadBytes = null;
    int? actualDuration = null; string? decodedWaveSha256 = null; int? decodedWaveBytes = null;
    PersistentBotRunner? runner = null;
    StateVault? vault = null; ILinkClient? api = null; MediaClient? media = null;
    using var userStop = new CancellationTokenSource();
    ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; userStop.Cancel(); };
    Console.CancelKeyPress += onCancel;
    using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(work.Token, userStop.Token);
    var ct = receiveCancellation.Token;
    try
    {
        vault = new StateVault(statePath);
        var state = await vault.LoadAsync<BotState>(ct) ?? throw new InvalidOperationException("Live binding required.");
        if (state.TransportMode != "live") throw new InvalidOperationException("Explicit live binding required.");
        api = new ILinkClient(); runner = new PersistentBotRunner(api, vault, state);
        runner.ValidateState(); await runner.RecoverAsync(ct);
        phase = "source-key-guard";
        if (runner.State.Outbox.Any(r => string.Equals(r.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase)) ||
            runner.State.MarkdownJobs.Any(j => string.Equals(j.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase) ||
                j.CompletedChunks.Any(r => string.Equals(r.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("The replay source key already has an attempt; no poll, download or resend.");
        phase = "existing-inbox-guard";
        if (runner.State.Inbox.Count != 0)
            throw new InvalidOperationException("Existing pending inbox must be handled separately; this experiment receives only a fresh batch.");
        media = new MediaClient(api);
        phase = "notify-start"; lifecycleMayNeedStop = true;
        await api.NotifyAsync(runner.State.Session, true, ct);
        Console.WriteLine("VOICE DESCRIPTOR REPLAY READY; waiting for the first bound VOICE only.");
        while (!selected && !ct.IsCancellationRequested)
        {
            if (runner.State.Inbox.Count == 0)
            {
                phase = "receive";
                var updates = await api.GetUpdatesAsync(runner.State.Session, TimeSpan.FromSeconds(20), ct);
                // StageUpdates performs the production binding/group/type/state filter
                // and atomically persists the entire inbox and cursor before processing.
                await runner.StageUpdatesAsync(updates, ct);
            }
            phase = "process-inbox";
            try
            {
                await runner.DrainInboxAsync(async (entry, cancel) =>
                {
                    // This is reached only AFTER the first handler returned and its
                    // inbox acknowledgment was saved. Preserve the rest of the batch.
                    if (selected) throw new StopVoiceReplayException();
                    var voice = entry.Message.Items?.FirstOrDefault(item => item.Type == 3);
                    if (voice is null) { ignoredMessageCount++; return; }
                    selected = true; selectedInboxKey = entry.Key;
                    phase = "known-voice-fields";
                    if (voice.VoiceItem is null) throw new InvalidDataException("The first VOICE has no known descriptor; no send attempted.");
                    var descriptor = CopyKnownVoiceDescriptor(voice.VoiceItem);
                    metadata = new
                    {
                        encodeType = descriptor.VoiceItem!.EncodeType, sampleRate = descriptor.VoiceItem.SampleRate,
                        bitsPerSample = descriptor.VoiceItem.BitsPerSample, playtime = descriptor.VoiceItem.Playtime,
                        encryptType = descriptor.VoiceItem.Media?.EncryptType,
                        aesKeyShape = KeyShape(descriptor.VoiceItem.Media?.AesKey)
                    };
                    phase = "message-context";
                    if (string.IsNullOrWhiteSpace(entry.Message.ContextToken))
                        throw new InvalidDataException("The selected VOICE has no message context; no metadata is guessed.");
                    phase = "download";
                    // DownloadItemAsync enforces the production HTTPS CDN allowlist,
                    // AES/key parsing and byte guards; raw stays only in this explicit dir.
                    var saved = await media.DownloadItemAsync(descriptor, downloadDirectory, sourceKey, cancel);
                    var codec = new VoiceCodec();
                    await using var input = File.OpenRead(saved);
                    if (input.Length is <= 0 || input.Length > codec.MaxInputBytes) throw new InvalidDataException("Unsupported voice sample size.");
                    var encoded = new byte[(int)input.Length]; VoiceCodecResult? decoded = null;
                    try
                    {
                        await input.ReadExactlyAsync(encoded, cancel);
                        payloadBytes = encoded.Length; payloadSha256 = Convert.ToHexString(SHA256.HashData(encoded)).ToLowerInvariant();
                        phase = "verify-Tencent-SILK";
                        if (encoded.Length < 10 || !encoded.AsSpan(0, 10).SequenceEqual("\u0002#!SILK_V3"u8))
                            throw new InvalidDataException("The received bytes are not Tencent SILK; no send attempted.");
                        decoded = await codec.DecodeSilkToWaveAsync(encoded, cancel);
                        actualDuration = decoded.DurationMilliseconds; decodedWaveBytes = decoded.Data.Length;
                        decodedWaveSha256 = Convert.ToHexString(SHA256.HashData(decoded.Data)).ToLowerInvariant();
                        phase = "validate-known-descriptor";
                        // Existing SDK policy requires received encode_type/playtime.
                        // If missing/invalid, stop rather than substitute guessed values.
                        ILinkClient.ValidateOutboundItem(descriptor);
                        // An independent business key does not automatically select an
                        // Inbox context. Apply this exact entry's token in memory before
                        // SendBoundItemAsync persists its frozen intent and BOT envelope.
                        runner.State.Session.ContextToken = entry.Message.ContextToken;
                        phase = "send"; sendAttemptStarted = true;
                        await runner.SendBoundItemAsync(descriptor, sourceKey, cancel, payloadSha256);
                        handlerCompleted = true; phase = "acknowledge-inbox";
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(encoded);
                        if (decoded is not null) CryptographicOperations.ZeroMemory(decoded.Data);
                    }
                }, ct);
            }
            catch (StopVoiceReplayException) { }
            if (!selected) await Task.Delay(500, ct);
        }
        // No cancellation is triggered from the handler. DrainInbox has already
        // acknowledged the successful first VOICE, even when more inbox entries remain.
        outcome = handlerCompleted && !runner.State.Inbox.Any(i => i.Key == selectedInboxKey)
            ? "API-accepted-first-voice" : "no-voice-before-deadline";
    }
    catch (ApiException ex)
    {
        sessionUsable = !ex.SessionExpired; httpStatus = ex.HttpStatus;
        outcome = ex.SessionExpired ? "authentication-rejected" : "API-failed";
    }
    catch (DeliveryUnknownException) { outcome = "delivery-unknown-no-retry"; }
    catch (OperationCanceledException) { outcome = userStop.IsCancellationRequested ? "user-stopped" : "deadline-no-retry"; }
    catch (HttpRequestException ex) { httpStatus = ex.StatusCode is { } status ? (int)status : null; outcome = "transport-failed-no-retry"; }
    catch { outcome = "local-validation-or-state-failed"; }
    finally
    {
        Console.CancelKeyPress -= onCancel;
        if (lifecycleMayNeedStop && sessionUsable && api is not null && runner is not null && !deadline.IsCancellationRequested)
            try
            {
                // Cleanup consumes only the reserved part of the same bounded budget.
                await api.NotifyAsync(runner.State.Session, false, deadline.Token); stopConfirmed = true;
            }
            catch (Exception ex) when (ex is ApiException or OperationCanceledException or HttpRequestException or IOException or JsonException) { }
        var receipt = runner?.State.Outbox.LastOrDefault(r => string.Equals(r.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase));
        try
        {
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
            {
            formatVersion = 1, testLevel = "live-SDK-replay-known-inbound-voice-descriptor", started,
            completed = DateTimeOffset.UtcNow, totalBudgetSeconds = seconds, receiveWindowSeconds = seconds - 5,
            outcome, phase, selectedVoice = selected, ignoredMessageCount, sendAttemptStarted,
            handlerAcknowledged = handlerCompleted && runner is not null && !runner.State.Inbox.Any(i => i.Key == selectedInboxKey),
            pendingInboxCount = runner?.State.Inbox.Count, receivedMetadata = metadata,
            reusedInboundCdnDescriptor = sendAttemptStarted, botEnvelopeMessageType = 2,
            originalMessageContextApplied = sendAttemptStarted, payloadBytes, payloadSha256,
            tencentSilkHeaderAndCodecVerified = actualDuration is > 0, actualDurationMilliseconds = actualDuration,
            decodedWaveBytes, decodedWaveSha256, notifyStopConfirmed = stopConfirmed, httpStatus,
            receipt = receipt is null ? null : new { receipt.ClientId, receipt.Status, receipt.AttemptedAt, receipt.ContentSha256, receipt.PayloadSha256 },
            protocolDllSha256 = await HashFileAsync(typeof(ILinkClient).Assembly.Location),
            clientDelivery = "requires independent client observation", containsCredentialsOrAccountIds = false,
            containsCdnUrlsQueriesAesKeysContextOrOriginalText = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { media?.Dispose(); api?.Dispose(); vault?.Dispose(); }
    }
    Console.WriteLine("VOICE REPLAY evidence saved; client delivery is unverified and no failed send was retried.");
    return handlerCompleted && runner is not null && !runner.State.Inbox.Any(i => i.Key == selectedInboxKey) && stopConfirmed ? 0 : 1;
}

static MessageItem CopyKnownVoiceDescriptor(VoiceItem source) => new()
{
    Type = 3, VoiceItem = new()
    {
        EncodeType = source.EncodeType, SampleRate = source.SampleRate, BitsPerSample = source.BitsPerSample,
        Playtime = source.Playtime, Text = source.Text,
        Media = source.Media is null ? null : new()
        {
            EncryptQueryParam = source.Media.EncryptQueryParam, AesKey = source.Media.AesKey,
            EncryptType = source.Media.EncryptType, FullUrl = source.Media.FullUrl
        }
    }
};

static object KeyShape(string? key)
{
    if (key is null) return new { encodedCharacters = 0, decodedBytes = (int?)null, format = "missing" };
    byte[]? decoded = null;
    try
    {
        if (key.Length > 256) return new { encodedCharacters = key.Length, decodedBytes = (int?)null, format = "unrecognized" };
        decoded = Convert.FromBase64String(key.Trim());
        var format = decoded.Length == 16 ? "base64-raw-16-bytes" :
            decoded.Length == 32 && decoded.All(b => b is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f' or >= (byte)'A' and <= (byte)'F')
            ? "base64-ASCII-32-hex" : "base64-other-length";
        return new { encodedCharacters = key.Length, decodedBytes = (int?)decoded.Length, format };
    }
    catch (FormatException) { return new { encodedCharacters = key.Length, decodedBytes = (int?)null, format = "not-base64" }; }
    finally { if (decoded is not null) CryptographicOperations.ZeroMemory(decoded); }
}

sealed class StopVoiceReplayException : Exception { }
