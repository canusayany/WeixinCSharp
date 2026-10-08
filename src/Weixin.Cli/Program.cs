using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QRCoder;
using Weixin.Protocol;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
return await App.RunAsync(args);

internal static class App
{
    private const string NativeVoiceDisabled = "原生语音发送已停用；请用 send-media --file audio.mp3 --kind audio 发送 MP3 文件附件。其他音频保留原格式，不自动转码。";

    public static async Task<int> RunAsync(string[] args)
    {
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        var ct = shutdown.Token;
        try
        {
            var options = Options.Parse(args);
            if (options.Command is "help" or "--help" or "-h") { Help(); return 0; }
            if (options.Command == "demo") { Demo(); return 0; }
            if (options.Command == "probe")
            {
                using var probeHttp = CreateFixtureClient(options);
                using var probeClient = new ILinkClient(options.AllowedHosts, probeHttp);
                await probeClient.GetQrCodeAsync(cancellationToken: ct);
                Console.WriteLine("官方扫码接口响应有效（二维码字段完整）。未扫码、未获取账号凭据、未收发账号消息。"); return 0;
            }
            if (options.Command == "qr-demo")
            {
                var dir = Path.GetFullPath(options.StatePath + ".qr-demo");
                Directory.CreateDirectory(dir);
                RenderQr("https://example.com/weixin-offline-demo", Path.Combine(dir, "login-qr.html"));
                Console.WriteLine("离线示例二维码（不用于微信登录）：" + Path.Combine(dir, "login-qr.html")); return 0;
            }
            if (options.Command is "prepare-voice" or "decode-voice")
            {
                var codec = new VoiceCodec { MaxDurationMilliseconds = options.MaximumVoiceMilliseconds };
                var input = await ReadCodecInputAsync(options.MediaFile!, codec.MaxInputBytes, ct);
                VoiceCodecResult? result = null;
                try
                {
                    result = options.Command == "decode-voice" ? await codec.DecodeSilkToWaveAsync(input, ct) :
                        options.InputFormat == "wav" ? await codec.EncodeWaveToSilkAsync(input, ct) :
                        await codec.EncodePcmToSilkAsync(input, ct);
                    await WriteNewFileAsync(options.OutputFile!, result.Data, ct);
                    Console.WriteLine($"本地语音转换完成：{result.DurationMilliseconds} 毫秒，{result.Data.Length} 字节；未发送到微信。");
                    return 0;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(input);
                    if (result is not null) CryptographicOperations.ZeroMemory(result.Data);
                }
            }
            if (options.Command is not ("login" or "listen" or "send" or "send-media" or "status" or "typing"))
                throw new ArgumentException("未知命令，使用 help 查看帮助。");
            using var fixtureHttp = CreateFixtureClient(options);
            using var vault = new StateVault(options.StatePath);
            var state = await vault.LoadAsync<BotState>(ct);
            ValidateTransportState(options, state);
            using var client = new ILinkClient(options.AllowedHosts, fixtureHttp);
            using var mediaClient = new MediaClient(client, new MediaLimits { MaximumBytes = options.MaximumMediaBytes,
                MaximumVoiceMilliseconds = options.MaximumVoiceMilliseconds }, fixtureHttp, options.AllowedCdnHosts);
            if (state is not null) new PersistentBotRunner(client, vault, state).ValidateState();
            if (options.Command == "login") { await LoginAsync(client, vault, state, options, ct); return 0; }
            if (state is null) throw new InvalidOperationException("尚未绑定，请先运行 login --open。");
            var runner = new PersistentBotRunner(client, vault, state) { Diagnostic = Console.Error.WriteLine };
            await runner.RecoverAsync(ct);
            state = runner.State;
            if (options.Command == "typing")
            {
                var config = await client.GetConfigAsync(state.Session, ct);
                if (!config.HasTypingTicket) throw new InvalidOperationException("该绑定未提供打字票据，当前无法显示打字状态。");
                var started = options.TypingStatus == "start";
                if (started && options.RunFor is { } typingDuration)
                {
                    var typing = client.CreateTypingLifecycle(state.Session, config.TypingTicket, diagnostic: Console.Error.WriteLine);
                    try
                    {
                        await typing.StartAsync(ct);
                        Console.WriteLine("打字开始请求已被 HTTP 接受，请在手机核对显示。");
                        using var active = CancellationTokenSource.CreateLinkedTokenSource(ct, typing.AuthenticationCancellation);
                        await Task.Delay(typingDuration, active.Token);
                    }
                    finally
                    {
                        await typing.DisposeAsync();
                        if (typing.CancelAccepted) Console.WriteLine("已发送打字取消请求。");
                    }
                }
                else
                {
                    await client.SetTypingAsync(state.Session, started, config.TypingTicket, ct);
                    Console.WriteLine(started ? "打字开始请求已被 HTTP 接受，请在手机核对显示。" : "打字取消请求已被 HTTP 接受，请在手机核对显示。");
                }
                return 0;
            }
            if (options.Command == "status")
            {
                // Local status is not a server availability or account-ban diagnosis.
                Console.WriteLine($"本地已保存绑定：{state.BoundAt?.ToString("O") ?? "时间未知"}");
                Console.WriteLine($"协议参考：{ILinkClient.ProtocolVersion}；API：{state.Session.BaseUrl}");
                Console.WriteLine($"待处理入站：{state.Inbox.Count}；上下文：{(string.IsNullOrEmpty(state.Session.ContextToken) ? "未取得" : "已保存")}");
                foreach (var receipt in state.Outbox.TakeLast(10))
                    Console.WriteLine($"发送 {receipt.ClientId}：{receipt.Status}，{receipt.AttemptedAt:O}");
                return 0;
            }
            if (options.Command == "send")
            {
                string text;
                if (options.TextFile is not null)
                {
                    if (new FileInfo(options.TextFile).Length > (options.Markdown ? 1024 * 1024 : 16000))
                        throw new ArgumentException("文本文件超过本地输入大小上限。");
                    text = await File.ReadAllTextAsync(options.TextFile, Encoding.UTF8, ct);
                }
                else
                {
                    Console.WriteLine("请输入发给绑定微信账号的文本（单行）：");
                    text = await Console.In.ReadLineAsync(ct) ?? throw new InvalidOperationException("未读到文本。");
                }
                if (options.Markdown)
                {
                    var sourceKey = options.SourceKey ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                    Console.WriteLine("Markdown 业务键（恢复时使用相同文本与 --source-key）：" + sourceKey);
                    var receipts = await runner.SendBoundMarkdownAsync(text, sourceKey, ct);
                    Console.WriteLine($"API 已接受 {receipts.Count} 段普通文本。已按官方过滤器转换并分块，显示效果请在手机核对。"); return 0;
                }
                var receipt = await runner.SendBoundTextAsync(text, options.SourceKey, ct);
                Console.WriteLine($"服务端已确认：{receipt.ClientId}。请在微信中核对实际送达。"); return 0;
            }
            if (options.Command == "send-media")
            {
                var kind = options.Kind switch { "image" => MediaKind.Image, "video" => MediaKind.Video,
                    "file" or "audio" => MediaKind.File, _ => throw new ArgumentException("媒体类型不受支持。") };
                // Hold a read-only file handle across fingerprint and upload, preventing a changed
                // file from being sent under an earlier business fingerprint on Windows.
                await using var fingerprintInput = options.SourceKey is null ? null : File.OpenRead(options.MediaFile!);
                string? payloadSha256 = null;
                if (options.SourceKey is not null)
                {
                    if (fingerprintInput!.Length <= 0 || fingerprintInput.Length > options.MaximumMediaBytes)
                        throw new ArgumentException("媒体文件为空或超过本地大小保护上限。");
                    var fileHash = Convert.ToHexString(await SHA256.HashDataAsync(fingerprintInput, ct));
                    // Preserve the empty duration/encoding slots in historical non-voice
                    // fingerprints so accepted file/audio records remain resumable.
                    var payload = $"{fileHash}\n{(int)kind}\n\n\n{Path.GetFileName(options.MediaFile)}";
                    payloadSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
                    if (runner.State.Outbox.LastOrDefault(r => r.SourceKey == options.SourceKey) is { } previous)
                    {
                        if (previous.PayloadSha256 != payloadSha256)
                            throw new InvalidOperationException("该业务键已有不同媒体或旧版无法比对的记录，未上传或重发。");
                        if (previous.Status != "Sent")
                            throw new InvalidOperationException("该媒体已有未成功确认的发送尝试，请先核对手机送达；未上传或重发。");
                        Console.WriteLine("该媒体业务键已经由 API 接受：" + previous.ClientId + "；本次未重复上传或发送。"); return 0;
                    }
                }
                var uploaded = await mediaClient.UploadBoundFileAsync(runner.State.Session, options.MediaFile!, kind, ct: ct);
                var item = uploaded.ToMessageItem();
                var receipt = await runner.SendBoundItemAsync(item, options.SourceKey, ct, payloadSha256);
                Console.WriteLine(options.Kind == "audio"
                    ? $"音频文件附件 API 已接受：{receipt.ClientId}，{uploaded.PlaintextBytes} 明文字节。保留原文件名与格式；请在微信中核对收到及打开。"
                    : $"媒体 API 已接受：{receipt.ClientId}，{uploaded.PlaintextBytes} 明文字节。请在手机核对收到及播放。"); return 0;
            }
            Console.WriteLine(options.Echo ? "正在接收；--echo 会回复绑定账号的文字消息。Ctrl+C 停止。" :
                "正在接收绑定账号消息。Ctrl+C 停止；停止后可用 send 回复。");
            if (options.RunFor is { } duration) shutdown.CancelAfter(duration);
            await runner.RunAsync(async (entry, cancel) =>
            {
                Console.WriteLine($"[{entry.Key[..12]}] {SafeConsoleText(entry.Message.Text)}");
                if (!string.IsNullOrWhiteSpace(entry.Message.VoiceTranscript))
                    Console.WriteLine("[语音转写] " + SafeConsoleText(entry.Message.VoiceTranscript));
                if (entry.Message.Items is { } items)
                    for (var index = 0; index < items.Count; index++)
                    {
                        if (items[index].Type is < 2 or > 5) continue;
                        if (options.DownloadDirectory is null) { Console.WriteLine("[本次未下载媒体；如需保存请启用 --download-dir 后从微信重新发送]"); continue; }
                        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Key + "\nitem:" + index)));
                        var saved = await mediaClient.DownloadItemAsync(items[index], options.DownloadDirectory, name, cancel);
                        Console.WriteLine("媒体已保存：" + saved);
                        // The official downloader tries SILK for every VOICE item. Real
                        // phone payloads may contain Tencent SILK under another encode_type;
                        // the codec validates the bytes, retaining raw input on failure.
                        if (items[index].Type == 3)
                        {
                            byte[]? encoded = null;
                            VoiceCodecResult? decoded = null;
                            try
                            {
                                var codec = new VoiceCodec { MaxDurationMilliseconds = options.MaximumVoiceMilliseconds };
                                encoded = await ReadCodecInputAsync(saved, codec.MaxInputBytes, cancel);
                                decoded = await codec.DecodeSilkToWaveAsync(encoded, cancel);
                                var wavePath = Path.ChangeExtension(saved, ".wav");
                                await WriteNewFileAsync(wavePath, decoded.Data, cancel);
                                Console.WriteLine("语音已解码为可本地播放的 WAV：" + wavePath);
                            }
                            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or IOException)
                            { Console.Error.WriteLine("语音解码未完成，已保留原始媒体；未输出音频或原始诊断。"); }
                            finally
                            {
                                if (encoded is not null) CryptographicOperations.ZeroMemory(encoded);
                                if (decoded is not null) CryptographicOperations.ZeroMemory(decoded.Data);
                            }
                        }
                    }
                if (options.Echo && !string.IsNullOrWhiteSpace(entry.Message.Text))
                {
                    TypingLifecycle? typing = null;
                    try
                    {
                        if (options.Typing)
                            try
                            {
                                var config = await client.GetConfigAsync(runner.State.Session, cancel);
                                if (config.HasTypingTicket)
                                {
                                    typing = client.CreateTypingLifecycle(runner.State.Session, config.TypingTicket, diagnostic: Console.Error.WriteLine);
                                    await typing.StartAsync(cancel);
                                }
                            }
                            catch (ApiException ex) when (!ex.SessionExpired)
                            { Console.Error.WriteLine("打字状态暂不可用，继续回复。"); }
                        using var active = typing is null ? null : CancellationTokenSource.CreateLinkedTokenSource(cancel, typing.AuthenticationCancellation);
                        await runner.SendBoundTextAsync("已收到：" + LimitUtf8(entry.Message.Text, 3900), entry.Key, active?.Token ?? cancel);
                    }
                    finally
                    {
                        if (typing is not null)
                            try { await typing.DisposeAsync(); }
                            catch (ApiException ex) when (!ex.SessionExpired)
                            { Console.Error.WriteLine("打字取消暂未确认。"); }
                    }
                }
            }, ct);
            return 0;
        }
        catch (OperationCanceledException) { Console.WriteLine("已停止。"); return 0; }
        catch (ApiException ex)
        {
            Console.Error.WriteLine(ex.Message);
            if (ex.SessionExpired) Console.Error.WriteLine("会话失效或权限被拒绝；请核对微信状态并重新 login。这本身不能证明封号。");
            return 2;
        }
        catch (DeliveryUnknownException ex) { Console.Error.WriteLine(ex.Message); return 3; }
        catch (MediaTransferException ex) { Console.Error.WriteLine(ex.Message); return 7; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or JsonException)
        { Console.Error.WriteLine("本地状态或响应读取失败。请保留状态文件并检查锁、磁盘、账号及协议版本；未输出敏感原始数据。"); return 4; }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or PlatformNotSupportedException)
        { Console.Error.WriteLine(ex.Message); return 1; }
        catch (HttpRequestException)
        { Console.Error.WriteLine("网络连接失败，请检查 DNS、TLS 和互联网连接。"); return 5; }
        catch (Exception)
        { Console.Error.WriteLine("发生未预期的本地错误，已停止；未输出原始数据或异常堆栈。"); return 6; }
    }

    private static HttpClient? CreateFixtureClient(Options options) => options.OfflineFixture is null ? null :
        OfflineFixtureTransport.CreateClient(options.OfflineFixture, options.StatePath);

    private static async Task<byte[]> ReadCodecInputAsync(string path, int maximumBytes, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length is <= 0 || stream.Length > maximumBytes)
            throw new ArgumentException("语音输入为空或超过本地编解码大小上限。");
        var bytes = new byte[(int)stream.Length];
        try { await stream.ReadExactlyAsync(bytes, ct); return bytes; }
        catch { CryptographicOperations.ZeroMemory(bytes); throw; }
    }

    private static async Task WriteNewFileAsync(string path, byte[] data, CancellationToken ct)
    {
        path = Path.GetFullPath(path);
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("输出文件已存在，未覆盖。");
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".voice-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await stream.WriteAsync(data, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void ValidateTransportState(Options options, BotState? state)
    {
        if (state is null) return;
        if (state.Session is null) throw new InvalidDataException("本地绑定结构异常。");
        var expected = options.OfflineFixture is null ? "live" : "offline-fixture";
        if (state.TransportMode != expected)
            throw new InvalidOperationException("状态文件的传输模式与本次命令不同，请使用各自独立的状态文件。");
        if (expected == "offline-fixture" &&
            (!state.Session.BotToken.StartsWith("fixture-", StringComparison.Ordinal) ||
             !state.Session.BotId.StartsWith("fixture-", StringComparison.Ordinal) ||
             !state.Session.UserId.StartsWith("fixture-", StringComparison.Ordinal) ||
             state.Session.ContextToken is { } context && !context.StartsWith("fixture-", StringComparison.Ordinal)))
            throw new InvalidOperationException("离线测试模式只接受专用虚构凭据。");
    }

    private static async Task LoginAsync(ILinkClient client, StateVault vault, BotState? oldState, Options options, CancellationToken ct)
    {
        if (oldState is not null && (oldState.Inbox.Count > 0 || oldState.Outbox.Any(r => r.Status is "Sending" or "Unknown")))
            throw new InvalidOperationException("旧绑定有待处理消息或未确定送达记录；请先 listen / status 核对，再保留旧状态并选用新的 --state 路径绑定。");
        var qrPath = Path.GetFullPath(options.StatePath + ".login-qr.html");
        if (options.QrPng is { } pngPath && (File.Exists(pngPath) ||
            Path.GetFullPath(pngPath) == Path.GetFullPath(options.StatePath)))
            throw new InvalidOperationException("二维码 PNG 路径已存在或与状态文件冲突，请使用新的路径。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            var refreshes = 0;
            var redirects = 0;
            var failures = 0;
            var verificationAttempts = 0;
            var qr = await client.GetQrCodeAsync(oldState?.Session.BotToken, deadline.Token);
            ShowQr(qr);
            var apiBase = ILinkClient.DefaultBaseUrl;
            string? code = null;
            while (true)
            {
                QrStatus status;
                try { status = await client.GetQrStatusAsync(apiBase, qr.Code, code, deadline.Token); }
                catch (ApiException ex) when (ex.IsTransient)
                { if (++failures >= 8) throw; await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, failures))), deadline.Token); continue; }
                catch (HttpRequestException)
                { if (++failures >= 8) throw; await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, failures))), deadline.Token); continue; }
                failures = 0;
                switch (status.Status)
                {
                    case "wait": break;
                    case "scaned": code = null; Console.WriteLine("已扫码，请在手机微信确认。"); break;
                    case "scaned_but_redirect":
                        if (++redirects > 5 || string.IsNullOrWhiteSpace(status.RedirectHost))
                            throw new InvalidOperationException("扫码路由重定向异常。");
                        apiBase = client.ValidateBaseUrl("https://" + status.RedirectHost).ToString();
                        break;
                    case "need_verifycode":
                        if (++verificationAttempts > 3) throw new InvalidOperationException("验证码尝试达到本地上限，请稍后重新登录。");
                        Console.WriteLine("请输入手机微信显示的配对验证码（不会保存或输出到日志）：");
                        code = (await Console.In.ReadLineAsync(deadline.Token))?.Trim();
                        if (string.IsNullOrWhiteSpace(code) || code.Length > 32 || code.Any(char.IsControl))
                            throw new InvalidOperationException("验证码为空或格式异常。");
                        break;
                    case "verify_code_blocked": throw new InvalidOperationException("配对验证码被服务端限制，已停止；请稍后重新绑定。");
                    case "binded_redirect":
                        if (oldState is null) throw new InvalidOperationException("服务端称已绑定，但本程序没有可用凭据。请在微信管理绑定后重试。");
                        Console.WriteLine("已有绑定；本程序保留原有凭据。"); return;
                    case "expired":
                        if (++refreshes > 3) throw new InvalidOperationException("二维码多次过期，请重新运行 login。");
                        code = null;
                        qr = await client.GetQrCodeAsync(oldState?.Session.BotToken, deadline.Token);
                        apiBase = ILinkClient.DefaultBaseUrl; ShowQr(qr); break;
                    case "confirmed":
                        if (string.IsNullOrWhiteSpace(status.BotToken) || string.IsNullOrWhiteSpace(status.BotId) || string.IsNullOrWhiteSpace(status.UserId))
                            throw new InvalidOperationException("绑定响应缺少凭据或用户标识；未保存不完整状态。");
                        var baseUrl = client.ValidateBaseUrl(status.BaseUrl ?? ILinkClient.DefaultBaseUrl).ToString();
                        var state = new BotState { BoundAt = DateTimeOffset.UtcNow,
                        TransportMode = options.OfflineFixture is null ? "live" : "offline-fixture", Session = new()
                        { BotToken = status.BotToken, BotId = status.BotId, UserId = status.UserId, BaseUrl = baseUrl } };
                        ValidateTransportState(options, state);
                        new PersistentBotRunner(client, vault, state).ValidateState();
                        if (File.Exists(options.StatePath)) File.Copy(options.StatePath, options.StatePath + ".previous", true);
                        await vault.SaveAsync(state, deadline.Token);
                        Console.WriteLine("绑定已保存并由当前 Windows 用户 DPAPI 加密。请运行 listen，并在手机微信向该机器人发消息。"); return;
                    default: throw new InvalidOperationException("遇到未知扫码状态，已停止；请核对协议版本。");
                }
                await Task.Delay(1000, deadline.Token);
            }
            void ShowQr(QrCode value)
            {
                RenderQr(value.Content, qrPath, options.QrPng);
                Console.WriteLine("用手机微信扫描本地页面中的二维码：" + qrPath);
                if (options.QrPng is { } png) Console.WriteLine("本次绑定二维码 PNG：" + png);
                if (options.Open) Process.Start(new ProcessStartInfo(qrPath) { UseShellExecute = true });
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new InvalidOperationException("五分钟绑定窗口结束，请重新运行 login。"); }
        finally
        {
            if (File.Exists(qrPath)) File.Delete(qrPath);
            if (options.QrPng is { } png && File.Exists(png)) File.Delete(png);
        }
    }

    private static void RenderQr(string content, string path, string? pngPath = null)
    {
        var png = PngByteQRCodeHelper.GetQRCode(content, QRCodeGenerator.ECCLevel.M, 8);
        var html = "<!doctype html><html lang='zh-CN'><meta charset='utf-8'><meta name='viewport' content='width=device-width'>" +
            "<meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'\">" +
            "<title>微信助理 · 扫码绑定</title><style>body{font:18px system-ui;background:#f4f6f8;text-align:center;padding:48px}img{max-width:80vw;width:360px}p{color:#52606d}</style>" +
            "<h1>微信助理 · 扫码绑定</h1><p>用手机微信扫码，并在手机上确认授权。二维码请勿分享。</p>" +
            "<img alt='微信绑定二维码' src='data:image/png;base64," + Convert.ToBase64String(png) + "'><p>程序结束时会删除本地二维码页面。</p></html>";
        File.WriteAllText(path, html, new UTF8Encoding(false));
        if (pngPath is not null) File.WriteAllBytes(pngPath, png);
    }

    private static string LimitUtf8(string value, int maximum)
    {
        var builder = new StringBuilder(); var bytes = 0;
        foreach (var rune in value.EnumerateRunes()) { if (bytes + rune.Utf8SequenceLength > maximum) break; builder.Append(rune); bytes += rune.Utf8SequenceLength; }
        return builder.ToString();
    }
    private static string SafeConsoleText(string text) => new(text.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray());
    private static void Demo()
    {
        const string sample = "{\"message_id\":18446744073709551615,\"from_user_id\":\"demo-owner\",\"message_type\":1,\"message_state\":2,\"item_list\":[{\"type\":1,\"text_item\":{\"text\":\"你好，微信助理\"}}]}";
        var message = JsonSerializer.Deserialize<InboundMessage>(sample, ILinkClient.Json)!;
        Console.WriteLine("离线解析示例；不会连接微信或发送消息。");
        Console.WriteLine("message_id=" + message.MessageId);
        Console.WriteLine("text=" + message.Text);
        Console.WriteLine("dedup_key=" + PersistentBotRunner.MessageKey(message));
    }
    private static void Help() => Console.WriteLine($"""
        微信助理 C# 客户端 {typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown"} · 腾讯 iLink 2.4.9 协议兼容实现
        weixin login --open              生成本地二维码并绑定自己的微信
        weixin listen                    收消息；不执行命令，不自动回复
        weixin listen --echo              收到文字后回复“已收到：…”（仅绑定账号）
        weixin listen --echo --typing     回复前开始打字，结束时取消
        weixin typing --typing-status start --run-for 10  开始打字，10秒后取消
        weixin typing --typing-status stop              取消打字状态
        weixin listen --run-for 300        接收 300 秒后正常停止，可配合 --echo
        weixin login --qr-png qr.png       同时生成临时 PNG，退出后清理
        weixin send                      从终端输入文字发送到绑定账号
        weixin send --text-file reply.txt 从 UTF-8 文件发送文字
        weixin send --markdown --text-file reply.md  转成普通文本并分块发送
        weixin send-media --file demo.mp4 --kind video  加密上传并发送视频
        weixin send-media --file audio.mp3 --kind audio  发送 MP3 文件附件（FILE）
        音频保留原文件名与字节，不自动转码；原生语音发送已停用
        weixin listen --download-dir downloads  下载绑定账号的图片/语音/视频/文件
        weixin prepare-voice --file mono.wav --input-format wav --output voice.silk  本地转SILK
        weixin prepare-voice --file mono.pcm --input-format pcm --output voice.silk  PCM16单声道24kHz
        weixin decode-voice --file voice.silk --output voice.wav  本地转可播放WAV
        weixin status                    查看本地状态与发送结果
        weixin demo                      离线协议解析示例
        weixin probe                     核查官方扫码接口（生成临时二维码，不显示/保存）
        weixin qr-demo                   生成离线演示二维码页面
        通用参数：--state <文件路径>，--allow-host <精确域名>（可重复）
        媒体参数：--allow-cdn-host <精确域名>，--max-media-mib <1..512>
        本地语音转换参数：--max-voice-seconds <1..3600>（也用于接收语音解码）
        发送幂等：--source-key <64位SHA256>；Markdown恢复应提供同一业务键
        本地媒体保护默认100MiB；语音转换默认60秒，不是已公布的微信服务端配额。
        自动化测试：--offline-fixture <夹具文件>（必须使用专用 --state；完全离线）
        一份状态文件只能由一个进程持有。发送结果未知时不会自动重发。
        独立 C# 接入不代表腾讯保证；源码验收与真实账号收发验收分别见报告。
        """);

    private sealed class Options
    {
        public string Command { get; private set; } = "help";
        public string StatePath { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeixinCSharp", "binding.dpapi");
        public List<string> AllowedHosts { get; } = [];
        public List<string> AllowedCdnHosts { get; } = [];
        public bool Open { get; private set; }
        public bool Echo { get; private set; }
        public bool Typing { get; private set; }
        public string? TypingStatus { get; private set; }
        public string? TextFile { get; private set; }
        public string? OfflineFixture { get; private set; }
        public string? QrPng { get; private set; }
        public TimeSpan? RunFor { get; private set; }
        public bool Markdown { get; private set; }
        public string? SourceKey { get; private set; }
        public string? MediaFile { get; private set; }
        public string? Kind { get; private set; }
        public string? InputFormat { get; private set; }
        public string? OutputFile { get; private set; }
        public string? DownloadDirectory { get; private set; }
        public long MaximumMediaBytes { get; private set; } = 100L * 1024 * 1024;
        public int MaximumVoiceMilliseconds { get; private set; } = 60_000;
        public static Options Parse(string[] args)
        {
            var value = new Options();
            var singleOptions = new HashSet<string>(StringComparer.Ordinal);
            if (args.Length > 0) value.Command = args[0];
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i] is not ("--allow-host" or "--allow-cdn-host") && !singleOptions.Add(args[i]))
                    throw new ArgumentException("参数重复。");
                switch (args[i])
                {
                    case "--open": value.Open = true; break;
                    case "--echo": value.Echo = true; break;
                    case "--typing": value.Typing = true; break;
                    case "--typing-status": value.TypingStatus = ReadValue(); break;
                    case "--state": value.StatePath = Path.GetFullPath(ReadValue()); break;
                    case "--allow-host": value.AllowedHosts.Add(ReadValue()); break;
                    case "--allow-cdn-host": value.AllowedCdnHosts.Add(ReadValue()); break;
                    case "--text-file": value.TextFile = Path.GetFullPath(ReadValue()); break;
                    case "--offline-fixture": value.OfflineFixture = Path.GetFullPath(ReadValue()); break;
                    case "--qr-png": value.QrPng = Path.GetFullPath(ReadValue()); break;
                    case "--markdown": value.Markdown = true; break;
                    case "--source-key": value.SourceKey = ReadValue(); break;
                    case "--file": value.MediaFile = Path.GetFullPath(ReadValue()); break;
                    case "--kind":
                        value.Kind = ReadValue();
                        if (value.Kind == "voice") throw new ArgumentException(NativeVoiceDisabled);
                        break;
                    case "--voice-encoding":
                    case "--voice-sample-rate":
                    case "--voice-bits-per-sample":
                    case "--duration-ms": throw new ArgumentException(NativeVoiceDisabled);
                    case "--input-format": value.InputFormat = ReadValue(); break;
                    case "--output": value.OutputFile = Path.GetFullPath(ReadValue()); break;
                    case "--download-dir": value.DownloadDirectory = Path.GetFullPath(ReadValue()); break;
                    case "--max-media-mib": value.MaximumMediaBytes = ReadNumber(1, 512) * 1024L * 1024; break;
                    case "--max-voice-seconds": value.MaximumVoiceMilliseconds = ReadNumber(1, 3600) * 1000; break;
                    case "--run-for":
                        if (!double.TryParse(ReadValue(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
                            !double.IsFinite(seconds) || seconds < 0.1 || seconds > 86400)
                            throw new ArgumentException("--run-for 应为 0.1 至 86400 秒。");
                        value.RunFor = TimeSpan.FromSeconds(seconds); break;
                    default: throw new ArgumentException("未知参数，使用 help 查看帮助。");
                }
                string ReadValue() => ++i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal) ? args[i] :
                    throw new ArgumentException("参数缺少值。");
                int ReadNumber(int minimum, int maximum) => int.TryParse(ReadValue(), NumberStyles.None, CultureInfo.InvariantCulture,
                    out var number) && number >= minimum && number <= maximum ? number : throw new ArgumentException("参数数值超出允许范围。");
            }
            if (value.Open && value.Command != "login" || value.Echo && value.Command != "listen" ||
                value.TextFile is not null && value.Command != "send" || value.QrPng is not null && value.Command != "login" ||
                value.RunFor is not null && value.Command is not ("listen" or "typing") || value.Markdown && value.Command != "send" ||
                value.Typing && (value.Command != "listen" || !value.Echo) ||
                value.TypingStatus is not null && value.Command != "typing" ||
                value.DownloadDirectory is not null && value.Command != "listen" ||
                value.SourceKey is not null && value.Command is not ("send" or "send-media") ||
                value.MediaFile is not null && value.Command is not ("send-media" or "prepare-voice" or "decode-voice") ||
                value.Kind is not null && value.Command != "send-media" ||
                value.InputFormat is not null && value.Command != "prepare-voice" ||
                value.OutputFile is not null && value.Command is not ("prepare-voice" or "decode-voice"))
                throw new ArgumentException("参数与命令不匹配。");
            if (value.Command == "typing" && (value.TypingStatus is not ("start" or "stop") ||
                value.RunFor is not null && value.TypingStatus != "start"))
                throw new ArgumentException("typing 需要 --typing-status start|stop；--run-for 只用于 start。");
            if (value.Command is "prepare-voice" or "decode-voice" && (value.MediaFile is null || value.OutputFile is null ||
                value.Command == "prepare-voice" && value.InputFormat is not ("wav" or "pcm")))
                throw new ArgumentException("语音转换需要 --file、--output；prepare-voice 还需 --input-format wav|pcm。");
            if (value.SourceKey is { } key && (key.Length != 64 || !key.All(Uri.IsHexDigit)))
                throw new ArgumentException("--source-key 必须是64位SHA256键。");
            if (value.Command == "send-media" && (value.MediaFile is null || value.Kind is not ("image" or "video" or "file" or "audio")))
                throw new ArgumentException("send-media 需要 --file 和有效 --kind。");
            if (value.OfflineFixture is not null && (!singleOptions.Contains("--state") || value.Open ||
                value.Command is not ("login" or "listen" or "send" or "send-media" or "status" or "probe" or "typing")))
                throw new ArgumentException("离线夹具需要显式 --state，且仅适用于协议命令，不允许 --open。");
            return value;
        }
    }
}
