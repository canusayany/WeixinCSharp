using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Weixin.Protocol;

public enum MediaKind { Image = 1, Video = 2, File = 3, Voice = 4 }

public sealed class MediaTransferException(string safeMessage, int? httpStatus = null) : Exception(safeMessage)
{
    public int? HttpStatus { get; } = httpStatus;
}

/// <summary>Local resource guards, not published WeChat server quotas.</summary>
public sealed class MediaLimits
{
    public long MaximumBytes { get; init; } = 100L * 1024 * 1024;
    public int MaximumVoiceMilliseconds { get; init; } = 60_000;
    internal void Validate()
    {
        if (MaximumBytes is < 1 or > int.MaxValue - 16 || MaximumVoiceMilliseconds < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumBytes), "本地媒体保护配置无效。");
    }
}

public sealed record UploadedMedia(MediaKind Kind, string DownloadParameter, string AesKeyHex,
    long PlaintextBytes, long CiphertextBytes, string FileName)
{
    public MessageItem ToMessageItem(int? voiceMilliseconds = null, int voiceEncoding = 7, int? sampleRate = null)
    {
        var media = new CdnMedia { EncryptQueryParam = DownloadParameter,
            AesKey = Convert.ToBase64String(Encoding.ASCII.GetBytes(AesKeyHex)), EncryptType = 1 };
        return Kind switch
        {
            MediaKind.Image => new() { Type = 2, ImageItem = new() { Media = media, MidSize = CiphertextBytes } },
            MediaKind.Video => new() { Type = 5, VideoItem = new() { Media = media, VideoSize = CiphertextBytes } },
            MediaKind.File => new() { Type = 4, FileItem = new() { Media = media, FileName = FileName,
                Length = PlaintextBytes.ToString(CultureInfo.InvariantCulture) } },
            MediaKind.Voice when voiceMilliseconds is > 0 && voiceEncoding is >= 1 and <= 8 =>
                new() { Type = 3, VoiceItem = new() { Media = media, EncodeType = voiceEncoding,
                    Playtime = voiceMilliseconds, SampleRate = sampleRate } },
            _ => throw new ArgumentException("原生语音须给出正数毫秒时长和有效编码类型。")
        };
    }
}

public sealed class UploadUrl
{
    [JsonPropertyName("upload_param")] public string? Parameter { get; set; }
    [JsonPropertyName("upload_full_url")] public string? FullUrl { get; set; }
}

public sealed partial class ILinkClient
{
    public Task<UploadUrl> GetUploadUrlAsync(BotSession session, MediaKind kind, string fileKey,
        long plaintextBytes, string plaintextMd5, string aesKeyHex, CancellationToken ct = default)
    {
        RequireSession(session);
        if (!Enum.IsDefined(kind) || plaintextBytes <= 0 || fileKey.Length != 32 || plaintextMd5.Length != 32 ||
            aesKeyHex.Length != 32 || !fileKey.All(Uri.IsHexDigit) || !plaintextMd5.All(Uri.IsHexDigit) || !aesKeyHex.All(Uri.IsHexDigit))
            throw new ArgumentException("上传元数据格式错误。");
        return RequestAsync<UploadUrl>(session.BaseUrl, "ilink/bot/getuploadurl", new
        {
            filekey = fileKey, media_type = (int)kind, to_user_id = session.UserId, rawsize = plaintextBytes,
            rawfilemd5 = plaintextMd5, filesize = MediaCryptography.PaddedLength(plaintextBytes), no_need_thumb = true,
            aeskey = aesKeyHex, base_info = BaseInfo
        }, session.BotToken, RequestTimeout, ct);
    }
}

public static class MediaCryptography
{
    public static long PaddedLength(long plaintextBytes) => plaintextBytes >= 0 ?
        checked((plaintextBytes / 16 + 1) * 16) : throw new ArgumentOutOfRangeException(nameof(plaintextBytes));

    public static byte[] DecodeKey(string encoded)
    {
        byte[] decoded;
        try { decoded = Convert.FromBase64String(encoded); }
        catch (FormatException) { throw new InvalidDataException("媒体密钥不是有效 base64。"); }
        if (decoded.Length == 16) return decoded;
        try
        {
            if (decoded.Length == 32)
            {
                var hex = Encoding.ASCII.GetString(decoded);
                if (hex.All(Uri.IsHexDigit)) return Convert.FromHexString(hex);
            }
            throw new InvalidDataException("媒体 AES 密钥长度或编码不受支持。");
        }
        finally { CryptographicOperations.ZeroMemory(decoded); }
    }

    public static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        using var aes = Aes.Create(); aes.Key = key;
        return aes.EncryptEcb(plaintext, PaddingMode.PKCS7);
    }
    public static byte[] Decrypt(byte[] ciphertext, byte[] key)
    {
        using var aes = Aes.Create(); aes.Key = key;
        try { return aes.DecryptEcb(ciphertext, PaddingMode.PKCS7); }
        catch (CryptographicException) { throw new InvalidDataException("媒体解密失败，请保留消息并核对协议。"); }
    }
}

/// <summary>Official AES/CDN wire behavior. Never sends API Bearer headers to CDN.</summary>
public sealed class MediaClient : IDisposable
{
    public const string DefaultCdnBaseUrl = "https://novac2c.cdn.weixin.qq.com/c2c";
    private readonly ILinkClient api;
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly HashSet<string> hosts = new(StringComparer.OrdinalIgnoreCase) { "novac2c.cdn.weixin.qq.com" };
    public MediaLimits Limits { get; }
    public TimeSpan TransferTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public MediaClient(ILinkClient api, MediaLimits? limits = null, HttpClient? httpClient = null,
        IEnumerable<string>? additionalCdnHosts = null)
    {
        this.api = api; Limits = limits ?? new(); Limits.Validate();
        foreach (var host in additionalCdnHosts ?? [])
        {
            if (Uri.CheckHostName(host) != UriHostNameType.Dns || host.Contains('*'))
                throw new ArgumentException("CDN allow-host 必须是精确 DNS 主机名。");
            hosts.Add(host);
        }
        ownsHttp = httpClient is null;
        http = httpClient ?? new(new SocketsHttpHandler { AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10), PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.None }) { Timeout = Timeout.InfiniteTimeSpan };
        if (http.DefaultRequestHeaders.Authorization is not null || new[] { "Cookie", "AuthorizationType", "X-WECHAT-UIN" }
            .Any(http.DefaultRequestHeaders.Contains))
            throw new ArgumentException("CDN HttpClient 不得带有默认账号凭据请求头。");
    }

    public Uri ValidateCdnUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || !hosts.Contains(uri.IdnHost))
            throw new InvalidOperationException("CDN 主机未获允许，请核实官方返回地址后添加精确 CDN 域名。");
        return uri;
    }

    public async Task<UploadedMedia> UploadBoundFileAsync(BotSession session, string filePath, MediaKind kind,
        int? voiceMilliseconds = null, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("媒体类型不受支持。");
        if (string.IsNullOrWhiteSpace(session.ContextToken)) throw new InvalidOperationException("请先接收绑定账号的消息获取上下文。");
        if (kind == MediaKind.Voice && (voiceMilliseconds is not > 0 || voiceMilliseconds > Limits.MaximumVoiceMilliseconds))
            throw new ArgumentException("原生语音时长超过本地保护上限或未提供；该上限不是已公开的服务端限制。");
        await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 || file.Length > Limits.MaximumBytes)
            throw new ArgumentException("媒体文件为空或超过本地大小保护上限（非服务端配额）。");
        var plaintext = await ReadBoundedAsync(file, Limits.MaximumBytes, ct);
        var key = RandomNumberGenerator.GetBytes(16);
        byte[]? ciphertext = null;
        try
        {
            var fileKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var hex = Convert.ToHexString(key).ToLowerInvariant();
            var md5 = Convert.ToHexString(MD5.HashData(plaintext)).ToLowerInvariant();
            ciphertext = MediaCryptography.Encrypt(plaintext, key);
            var upload = await api.GetUploadUrlAsync(session, kind, fileKey, plaintext.LongLength, md5, hex, ct);
            var url = ValidateCdnUrl(!string.IsNullOrWhiteSpace(upload.FullUrl) ? upload.FullUrl.Trim() :
                !string.IsNullOrWhiteSpace(upload.Parameter) ? DefaultCdnBaseUrl + "/upload?encrypted_query_param=" +
                    Uri.EscapeDataString(upload.Parameter) + "&filekey=" + Uri.EscapeDataString(fileKey) :
                    throw new ApiException("媒体上传响应缺少地址或参数。"));
            // Upload retries reuse identical ciphertext/key/filekey. No chat send has started yet.
            for (var attempt = 1; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(ciphertext) };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TransferTimeout);
                try
                {
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                    var status = (int)response.StatusCode;
                    if (status is >= 400 and < 500) throw new MediaTransferException($"媒体 CDN HTTP {status}；请核对媒体权限或 URL 时效。", status);
                    if (status != 200) throw new HttpRequestException("媒体 CDN 上传未成功。");
                    var parameter = response.Headers.TryGetValues("x-encrypted-param", out var values) ? values.FirstOrDefault() : null;
                    if (string.IsNullOrWhiteSpace(parameter)) throw new HttpRequestException("CDN 上传响应缺少下载参数。");
                    return new(kind, parameter, hex, plaintext.LongLength, ciphertext.LongLength, Path.GetFileName(filePath));
                }
                catch (Exception ex) when (attempt < 3 && !ct.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException)
                { ct.ThrowIfCancellationRequested(); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { throw new MediaTransferException("媒体 CDN 上传超时，未开始聊天发送。"); }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext); CryptographicOperations.ZeroMemory(key);
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    public async Task<string> DownloadItemAsync(MessageItem item, string destinationDirectory, string stableName,
        CancellationToken ct = default)
    {
        var (media, extension, hexKey) = item.Type switch
        {
            2 when item.ImageItem is { } image => (image.Media, ".image", image.AesKey),
            3 when item.VoiceItem is { } voice => (voice.Media, voice.EncodeType switch { 1 => ".pcm", 5 => ".amr", 6 => ".silk", 7 => ".mp3", 8 => ".ogg", _ => ".voice" }, null),
            4 when item.FileItem is { } attachment => (attachment.Media, SafeExtension(attachment.FileName), null),
            5 when item.VideoItem is { } video => (video.Media, ".mp4", null),
            _ => throw new ArgumentException("该项目不是支持下载的媒体。")
        };
        if (stableName.Length != 64 || !stableName.All(Uri.IsHexDigit)) throw new ArgumentException("下载文件名应为稳定的 SHA256 键。");
        if (media is null || item.Type != 2 && (string.IsNullOrWhiteSpace(media.AesKey) && string.IsNullOrWhiteSpace(hexKey)))
            throw new InvalidDataException("媒体描述符缺少密钥；保留入站消息以便核对。");
        var url = ValidateCdnUrl(!string.IsNullOrWhiteSpace(media.FullUrl) ? media.FullUrl :
            !string.IsNullOrWhiteSpace(media.EncryptQueryParam) ? DefaultCdnBaseUrl + "/download?encrypted_query_param=" +
            Uri.EscapeDataString(media.EncryptQueryParam) : throw new InvalidDataException("媒体缺少下载地址或参数。"));
        byte[]? key = null;
        if (!string.IsNullOrWhiteSpace(hexKey))
        {
            if (hexKey.Length != 32 || !hexKey.All(Uri.IsHexDigit)) throw new InvalidDataException("图片 AES 十六进制密钥异常。");
            key = Convert.FromHexString(hexKey);
        }
        else if (!string.IsNullOrWhiteSpace(media.AesKey)) key = MediaCryptography.DecodeKey(media.AesKey);
        byte[]? ciphertext = null, plaintext = null;
        string? temporary = null;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TransferTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) throw new MediaTransferException($"媒体 CDN HTTP {(int)response.StatusCode}；请核对媒体权限或 URL 时效。", (int)response.StatusCode);
            var maximumDownload = key is null ? Limits.MaximumBytes : MediaCryptography.PaddedLength(Limits.MaximumBytes);
            if (response.Content.Headers.ContentLength > maximumDownload)
                throw new InvalidDataException("媒体下载超过本地保护上限。");
            await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
            ciphertext = await ReadBoundedAsync(input, maximumDownload, deadline.Token);
            plaintext = key is null ? ciphertext.ToArray() : MediaCryptography.Decrypt(ciphertext, key);
            if (plaintext.LongLength > Limits.MaximumBytes) throw new InvalidDataException("解密媒体超过本地保护上限。");
            destinationDirectory = Path.GetFullPath(destinationDirectory); Directory.CreateDirectory(destinationDirectory);
            var target = Path.Combine(destinationDirectory, stableName.ToLowerInvariant() + extension);
            // Stable paths make replay idempotent. Existing content must match, never be overwritten.
            if (File.Exists(target))
            {
                if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("媒体下载目标不允许符号链接。");
                await using var existing = File.OpenRead(target);
                if (existing.Length != plaintext.LongLength || !CryptographicOperations.FixedTimeEquals(
                    await SHA256.HashDataAsync(existing, ct), SHA256.HashData(plaintext)))
                    throw new InvalidDataException("下载目标已有不同内容，未覆盖。");
                return target;
            }
            temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temporary, plaintext, ct); ct.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: false); temporary = null; return target;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new MediaTransferException("媒体 CDN 下载超时，入站消息已保留供恢复。"); }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string SafeExtension(string? name)
    {
        var extension = Path.GetExtension(name ?? "");
        return extension.Length is >= 2 and <= 12 && extension[1..].All(char.IsAsciiLetterOrDigit) ? extension.ToLowerInvariant() : ".bin";
    }
    private static async Task<byte[]> ReadBoundedAsync(Stream input, long limit, CancellationToken ct)
    {
        using var memory = new MemoryStream(); var buffer = new byte[81920];
        try
        {
            int count;
            while ((count = await input.ReadAsync(buffer, ct)) > 0)
            {
                if (memory.Length + count > limit) throw new InvalidDataException("媒体数据超过本地大小保护上限。");
                await memory.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            return memory.ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(buffer); if (memory.TryGetBuffer(out var bytes)) CryptographicOperations.ZeroMemory(bytes.AsSpan()); }
    }
    public void Dispose() { if (ownsHttp) http.Dispose(); }
}
