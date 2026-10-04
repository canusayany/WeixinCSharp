using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Weixin.Protocol;

namespace Weixin.Protocol.Tests;

public static class MediaClientTests
{
    public static async Task RunAsync()
    {
        await AesInteropAsync();
        await UploadInteropAsync();
        await DownloadInteropAsync();
        await LimitsAndHostGuardsAsync();
        await DownloadFailurePreservesFilesAsync();
        await PlainImagesAndCdnScopeAsync();
        await TransferTimeoutIsFailureAsync();
        Console.WriteLine("MediaClientTests: 7 test groups passed (offline AES/CDN fixtures).");
    }
    private static Task AesInteropAsync()
    {
        var key = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
        var plaintext = Convert.FromHexString("00112233445566778899aabbccddeeff");
        var ciphertext = MediaCryptography.Encrypt(plaintext, key);
        Assert(Convert.ToHexString(ciphertext[..16]).Equals("69C4E0D86A7B0430D8CDB78070B4C55A"), "AES must match the independent NIST AES-128 vector.");
        Assert(ciphertext.Length == 32 && MediaCryptography.PaddedLength(16) == 32 && MediaCryptography.PaddedLength(0) == 16,
            "PKCS7 must add a complete block at exact boundaries.");
        Assert(MediaCryptography.Decrypt(ciphertext, key).SequenceEqual(plaintext), "Decrypt must remove padding exactly.");
        foreach (var encoded in new[] { Convert.ToBase64String(key), Convert.ToBase64String(Encoding.ASCII.GetBytes(Convert.ToHexString(key).ToLowerInvariant())) })
            Assert(MediaCryptography.DecodeKey(encoded).SequenceEqual(key), "Both official inbound key representations must work.");
        foreach (var bad in new[] { "not-base64!", Convert.ToBase64String(new byte[15]), Convert.ToBase64String(Encoding.ASCII.GetBytes(new string('z', 32))) })
            Throws<InvalidDataException>(() => MediaCryptography.DecodeKey(bad));
        Throws<InvalidDataException>(() => MediaCryptography.Decrypt(new byte[15], key));
        return Task.CompletedTask;
    }
    private static async Task UploadInteropAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "weixin-media-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "fixture.mp4");
        var bytes = Encoding.UTF8.GetBytes("fixture-video-你好");
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            JsonElement metadata = default; var uploads = new List<byte[]>();
            using var handler = new Handler(async (request, ct) =>
            {
                if (request.RequestUri!.Host == "ilinkai.weixin.qq.com")
                {
                    Assert(request.Headers.Authorization?.Parameter == "fixture-bot-token", "API metadata needs the bound token.");
                    using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); metadata = doc.RootElement.Clone();
                    return Reply("{\"upload_param\":\"fixture-upload +/\"}");
                }
                Assert(request.Headers.Authorization is null && !request.Headers.Contains("AuthorizationType") && !request.Headers.Contains("User-Agent"),
                    "No bot authentication or custom marker may leak to CDN.");
                Assert(request.Content!.Headers.ContentType!.MediaType == "application/octet-stream", "CDN must receive ciphertext octets.");
                uploads.Add(await request.Content.ReadAsByteArrayAsync(ct));
                if (uploads.Count == 1) return new(HttpStatusCode.InternalServerError);
                var response = new HttpResponseMessage(HttpStatusCode.OK); response.Headers.Add("x-encrypted-param", "fixture-download"); return response;
            });
            using var http = new HttpClient(handler);
            using var api = new ILinkClient(httpClient: http);
            using var media = new MediaClient(api, httpClient: http);
            var uploaded = await media.UploadBoundFileAsync(Session(), path, MediaKind.Video);
            var key = Convert.FromHexString(metadata.GetProperty("aeskey").GetString()!);
            Assert(uploads.Count == 2 && uploads[0].SequenceEqual(uploads[1]), "Upload retries must reuse identical key, filekey and ciphertext.");
            Assert(MediaCryptography.Decrypt(uploads[0], key).SequenceEqual(bytes), "Captured upload must decrypt to the exact file.");
            Assert(metadata.GetProperty("media_type").GetInt32() == 2 && metadata.GetProperty("rawsize").GetInt64() == bytes.Length &&
                metadata.GetProperty("filesize").GetInt64() == uploads[0].Length && metadata.GetProperty("no_need_thumb").GetBoolean(), "Upload sizes/type/thumbnail flags must match official wire behavior.");
            Assert(metadata.GetProperty("rawfilemd5").GetString() == Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), "Upload MD5 is of plaintext.");
            var item = uploaded.ToMessageItem();
            Assert(item.Type == 5 && item.VideoItem!.VideoSize == uploads[0].Length && item.VideoItem.Media!.EncryptType == 1,
                "Video descriptors use ciphertext bytes and official item type.");
            Assert(MediaCryptography.DecodeKey(item.VideoItem!.Media!.AesKey!).SequenceEqual(key), "Outbound key uses base64 of hex ASCII.");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task DownloadInteropAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "weixin-download-fixture-" + Guid.NewGuid().ToString("N"));
        var bytes = Encoding.UTF8.GetBytes("fixture downloaded media"); var key = RandomNumberGenerator.GetBytes(16);
        var encrypted = MediaCryptography.Encrypt(bytes, key);
        using var handler = new Handler((request, _) =>
        {
            Assert(request.Method == HttpMethod.Get && request.Headers.Authorization is null, "Downloads are anonymous CDN GETs.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(encrypted) });
        });
        using var http = new HttpClient(handler); using var api = new ILinkClient(httpClient: http); using var media = new MediaClient(api, httpClient: http);
        try
        {
            foreach (var kind in Enum.GetValues<MediaKind>())
            {
                var uploaded = new UploadedMedia(kind, "fixture-param +/?", Convert.ToHexString(key).ToLowerInvariant(), bytes.Length, encrypted.Length, "../fixture.txt");
                var item = uploaded.ToMessageItem(kind == MediaKind.Voice ? 1000 : null);
                var stable = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind.ToString())));
                var saved = await media.DownloadItemAsync(item, directory, stable);
                Assert((await File.ReadAllBytesAsync(saved)).SequenceEqual(bytes) && Path.GetDirectoryName(saved) == directory,
                    "Each media kind must decrypt exactly and stay in the destination directory.");
                var replay = await media.DownloadItemAsync(item, directory, stable);
                Assert(replay == saved && Directory.GetFiles(directory, "*.tmp").Length == 0, "Download replay must preserve an identical existing target.");
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static async Task LimitsAndHostGuardsAsync()
    {
        var path = Path.GetTempFileName(); await File.WriteAllBytesAsync(path, new byte[9]);
        using var handler = new Handler((_, _) => throw new InvalidOperationException("No HTTP is permitted in preflight tests."));
        using var http = new HttpClient(handler); using var api = new ILinkClient(httpClient: http);
        using var media = new MediaClient(api, new MediaLimits { MaximumBytes = 8, MaximumVoiceMilliseconds = 1000 }, http);
        try
        {
            await ThrowsAsync<ArgumentException>(() => media.UploadBoundFileAsync(Session(), path, MediaKind.Video));
            await ThrowsAsync<ArgumentException>(() => media.UploadBoundFileAsync(Session(), path, MediaKind.Voice, 1001));
            foreach (var url in new[] { "http://novac2c.cdn.weixin.qq.com/c2c/upload", "https://novac2c.cdn.weixin.qq.com.attacker.invalid/c2c/upload",
                "https://user:pass@novac2c.cdn.weixin.qq.com/c2c/upload", "https://novac2c.cdn.weixin.qq.com:444/c2c/upload" })
                Throws<InvalidOperationException>(() => media.ValidateCdnUrl(url));
            Assert(handler.Calls == 0, "Oversize/duration/host preflight must not touch any endpoint.");
        }
        finally { File.Delete(path); }
    }
    private static async Task DownloadFailurePreservesFilesAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "weixin-bad-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var stable = new string('A', 64); var target = Path.Combine(directory, stable.ToLowerInvariant() + ".mp4");
        await File.WriteAllTextAsync(target, "existing-fixture");
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[15]) }));
        using var http = new HttpClient(handler); using var api = new ILinkClient(httpClient: http); using var media = new MediaClient(api, httpClient: http);
        try
        {
            var item = new UploadedMedia(MediaKind.Video, "fixture-param", Convert.ToHexString(new byte[16]), 1, 16, "f.mp4").ToMessageItem();
            await ThrowsAsync<InvalidDataException>(() => media.DownloadItemAsync(item, directory, stable));
            Assert(await File.ReadAllTextAsync(target) == "existing-fixture" && Directory.GetFiles(directory, "*.tmp").Length == 0,
                "Failed decryption must not overwrite an existing media file or leak temporary plaintext.");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task PlainImagesAndCdnScopeAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "weixin-plain-image-" + Guid.NewGuid().ToString("N"));
        var bytes = Encoding.UTF8.GetBytes("fixture plain image bytes");
        var status = HttpStatusCode.OK;
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) }));
        using var http = new HttpClient(handler); using var api = new ILinkClient(httpClient: http); using var media = new MediaClient(api, httpClient: http);
        var item = new MessageItem { Type = 2, ImageItem = new() { Media = new() { EncryptQueryParam = "fixture-plain" } } };
        try
        {
            var saved = await media.DownloadItemAsync(item, directory, new string('B', 64));
            Assert((await File.ReadAllBytesAsync(saved)).SequenceEqual(bytes), "Official plaintext images without AES keys must download unchanged.");
            using var bounded = new MediaClient(api, new MediaLimits { MaximumBytes = 8 }, http);
            await ThrowsAsync<InvalidDataException>(() => bounded.DownloadItemAsync(item, directory, new string('C', 64)));
            status = HttpStatusCode.Forbidden;
            try { await media.DownloadItemAsync(item, directory, new string('D', 64)); throw new InvalidOperationException("Expected CDN rejection."); }
            catch (MediaTransferException ex) { Assert(ex.HttpStatus == 403, "CDN authentication failure must stay separate from Bot session expiration."); }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static async Task TransferTimeoutIsFailureAsync()
    {
        using var handler = new Handler(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Reply("{}"); });
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }; using var api = new ILinkClient(httpClient: http);
        using var media = new MediaClient(api, httpClient: http) { TransferTimeout = TimeSpan.FromMilliseconds(20) };
        var item = new MessageItem { Type = 2, ImageItem = new() { Media = new() { EncryptQueryParam = "fixture-plain" } } };
        await ThrowsAsync<MediaTransferException>(() => media.DownloadItemAsync(item, Path.GetTempPath(), new string('F', 64)));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => media.DownloadItemAsync(item, Path.GetTempPath(), new string('F', 64), canceled.Token));
    }
    private static BotSession Session() => new() { BotId = "fixture-bot", BotToken = "fixture-bot-token", UserId = "fixture-user", ContextToken = "fixture-context" };
    private static HttpResponseMessage Reply(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { public int Calls { get; private set; } protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return send(request, ct); } }
}
