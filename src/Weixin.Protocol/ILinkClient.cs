using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Weixin.Protocol;

/// <summary>HTTPS text transport aligned to Tencent openclaw-weixin 2.4.9.</summary>
public sealed partial class ILinkClient : IDisposable
{
    public const string ProtocolVersion = "2.4.9";
    public const string DefaultBaseUrl = "https://ilinkai.weixin.qq.com";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly HashSet<string> hosts;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public int MaxResponseBytes { get; init; } = 4 * 1024 * 1024;

    public ILinkClient(IEnumerable<string>? additionalHosts = null, HttpClient? httpClient = null)
    {
        hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ilinkai.weixin.qq.com" };
        foreach (var host in additionalHosts ?? [])
        {
            // Explicit exact hosts only. No wildcard, URL, port or path is accepted.
            if (Uri.CheckHostName(host) != UriHostNameType.Dns || host.Contains('*'))
                throw new ArgumentException("allow-host 必须是完整 DNS 主机名。");
            hosts.Add(host);
        }
        ownsHttp = httpClient is null;
        http = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10), AutomaticDecompression = DecompressionMethods.All
        }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>A supplied HttpClient must also disable automatic redirects.</summary>
    public Uri ValidateBaseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !hosts.Contains(uri.IdnHost))
            throw new InvalidOperationException("API 主机未获本地允许；核实后使用 --allow-host 指定精确域名。");
        return uri;
    }

    // Match buildBaseInfo()'s public default. No additional product marker is sent.
    // Official docs state bot_agent is monitoring attribution, not authentication/routing.
    private static object BaseInfo => new { channel_version = ProtocolVersion, bot_agent = "OpenClaw" };

    public async Task<QrCode> GetQrCodeAsync(string? existingBotToken = null, CancellationToken cancellationToken = default)
    {
        // No WorkBuddy token files are read; only this program's own previous binding is supplied.
        var result = await RequestAsync<QrCode>(DefaultBaseUrl, "ilink/bot/get_bot_qrcode?bot_type=3",
            new { local_token_list = string.IsNullOrWhiteSpace(existingBotToken) ? Array.Empty<string>() : new[] { existingBotToken } },
            null, RequestTimeout, cancellationToken);
        if (string.IsNullOrWhiteSpace(result.Code) || string.IsNullOrWhiteSpace(result.Content))
            throw new ApiException("二维码响应缺少必要字段。");
        return result;
    }

    public async Task<QrStatus> GetQrStatusAsync(string baseUrl, string qrCode, string? verifyCode = null,
        CancellationToken cancellationToken = default)
    {
        var path = "ilink/bot/get_qrcode_status?qrcode=" + Uri.EscapeDataString(qrCode);
        if (!string.IsNullOrEmpty(verifyCode)) path += "&verify_code=" + Uri.EscapeDataString(verifyCode);
        try { return await RequestAsync<QrStatus>(baseUrl, path, null, null, TimeSpan.FromSeconds(40), cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new() { Status = "wait" }; }
    }

    public async Task<Updates> GetUpdatesAsync(BotSession session, TimeSpan pollTimeout,
        CancellationToken cancellationToken = default)
    {
        RequireSession(session);
        try
        {
            return await RequestAsync<Updates>(session.BaseUrl, "ilink/bot/getupdates",
                new { get_updates_buf = session.Cursor, base_info = BaseInfo }, session.BotToken,
                pollTimeout, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new() { Cursor = session.Cursor }; }
    }

    public async Task SendTextAsync(BotSession session, string text, string clientId,
        CancellationToken cancellationToken = default)
        => await SendItemAsync(session, new MessageItem { Type = 1, TextItem = new() { Text = text } }, clientId, cancellationToken);

    public static void ValidateOutboundItem(MessageItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Type == 1)
        {
            if (string.IsNullOrWhiteSpace(item.TextItem?.Text) || item.TextItem.Text.Length > 4000)
                throw new ArgumentException("文本应为 1 至 4000 UTF-16 单位（官方客户端分块配置，非服务端硬上限）。");
            return;
        }
        var field = item.Type switch { 2 => "image_item", 3 => "voice_item", 4 => "file_item", 5 => "video_item", _ => null };
        var wire = JsonSerializer.SerializeToElement(item, Json);
        if (field is null || !wire.TryGetProperty(field, out var descriptor) || descriptor.ValueKind != JsonValueKind.Object ||
            !descriptor.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object ||
            !media.TryGetProperty("aes_key", out var key) || key.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(key.GetString()) ||
            !media.TryGetProperty("encrypt_query_param", out var query) || query.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(query.GetString()))
            throw new ArgumentException("媒体项缺少有效的 CDN 下载参数或加密密钥。");
        var decodedKey = MediaCryptography.DecodeKey(key.GetString()!);
        CryptographicOperations.ZeroMemory(decodedKey);
        if (item.Type == 3 && (item.VoiceItem?.EncodeType is not (>= 1 and <= 8) || item.VoiceItem.Playtime is not > 0))
            throw new ArgumentException("原生语音需要编码类型和正数毫秒时长。");
        if (item.Type == 4 && (string.IsNullOrWhiteSpace(item.FileItem?.FileName) ||
            !long.TryParse(item.FileItem.Length, NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length <= 0))
            throw new ArgumentException("文件项需要文件名和正数明文字节长度。");
        if (item.Type == 5 && item.VideoItem?.VideoSize is not > 0 || item.Type == 2 && item.ImageItem?.MidSize is not > 0)
            throw new ArgumentException("媒体项需要正数密文字节长度。");
    }

    public async Task SendItemAsync(BotSession session, MessageItem item, string clientId,
        CancellationToken cancellationToken = default)
    {
        RequireSession(session);
        ValidateOutboundItem(item);
        if (string.IsNullOrWhiteSpace(session.ContextToken))
            throw new InvalidOperationException("尚无有效会话上下文。请先 listen，并从绑定微信向机器人发一条消息。");
        if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("必须提供 client_id。");
        // Null optional descriptor fields are omitted, like JSON.stringify in the official client.
        var wire = JsonSerializer.SerializeToElement(item, new JsonSerializerOptions(Json)
            { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        try
        {
            await RequestAsync<ApiResult>(session.BaseUrl, "ilink/bot/sendmessage", new
            {
                msg = new { from_user_id = "", to_user_id = session.UserId, client_id = clientId,
                    message_type = 2, message_state = 2, context_token = session.ContextToken,
                    item_list = new[] { wire } }, base_info = BaseInfo
            }, session.BotToken, RequestTimeout, cancellationToken);
        }
        // Timeout, connection loss or invalid reply cannot prove the send was rejected.
        catch (ApiException ex) when (ex.HttpStatus == 408 || ex.HttpStatus >= 500) { throw new DeliveryUnknownException(); }
        catch (OperationCanceledException) { throw new DeliveryUnknownException(); }
        catch (HttpRequestException) { throw new DeliveryUnknownException(); }
        catch (JsonException) { throw new DeliveryUnknownException(); }
        catch (IOException) { throw new DeliveryUnknownException(); }
    }

    public Task<ApiResult> NotifyAsync(BotSession session, bool started, CancellationToken cancellationToken = default)
    {
        RequireSession(session);
        return RequestAsync<ApiResult>(session.BaseUrl, started ? "ilink/bot/msg/notifystart" : "ilink/bot/msg/notifystop",
            new { base_info = BaseInfo }, session.BotToken, TimeSpan.FromSeconds(10), cancellationToken);
    }

    private static void RequireSession(BotSession session)
    {
        if (string.IsNullOrWhiteSpace(session.BotToken) || string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.BotId))
            throw new InvalidOperationException("绑定状态不完整，请重新执行 login。");
    }

    private async Task<T> RequestAsync<T>(string baseUrl, string path, object? body, string? token,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var uri = new Uri(ValidateBaseUrl(baseUrl), path);
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, uri);
        request.Headers.Add("iLink-App-Id", "bot");
        request.Headers.Add("iLink-App-ClientVersion", "132105");
        if (body is not null)
        {
            request.Headers.Add("AuthorizationType", "ilink_bot_token");
            var random = BinaryPrimitives.ReadUInt32BigEndian(RandomNumberGenerator.GetBytes(4));
            request.Headers.Add("X-WECHAT-UIN", Convert.ToBase64String(Encoding.ASCII.GetBytes(random.ToString(CultureInfo.InvariantCulture))));
            if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        }
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var ct = timeoutSource.Token;
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var retry = response.Headers.RetryAfter?.Delta;
            if (retry is null && response.Headers.RetryAfter?.Date is { } date) retry = date - DateTimeOffset.UtcNow;
            throw new ApiException($"微信 API HTTP {status}。", httpStatus: status,
                transient: status is 408 or 429 || status >= 500, retryAfter: retry);
        }
        if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new JsonException("API 响应超过上限。");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (bytes.Length + count > MaxResponseBytes) throw new JsonException("API 响应超过上限。");
            await bytes.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        using var doc = JsonDocument.Parse(bytes.ToArray());
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("API 响应不是对象。");
        int? ret = ReadCode(doc.RootElement, "ret"), error = ReadCode(doc.RootElement, "errcode");
        if (ret is not null and not 0 || error is not null and not 0)
            throw new ApiException($"微信 API 返回 ret={ret?.ToString() ?? "无"}, errcode={error?.ToString() ?? "无"}。",
                ret: ret, errorCode: error);
        // Official 2.4.9 result codes are optional (protobuf default zero may be omitted).
        // A valid 2xx object without a code is API acceptance, never proof of phone delivery.
        return doc.RootElement.Deserialize<T>(Json) ?? throw new JsonException("API 响应为空。");
    }

    private static int? ReadCode(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element)) return null;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var code)) return code;
        throw new JsonException("API 结果码类型错误。");
    }

    public void Dispose() { if (ownsHttp) http.Dispose(); }
}
