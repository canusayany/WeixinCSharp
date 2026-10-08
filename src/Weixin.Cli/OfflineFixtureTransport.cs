using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Explicit, fail-closed test transport. It never creates a socket or delegates to a network handler.
/// Fixture/state/trace paths are confined to a UUID-named, explicitly marked test directory.
/// This transport exercises the CLI process; its responses are not evidence of real WeChat delivery.
/// </summary>
internal sealed class OfflineFixtureTransport : HttpMessageHandler
{
    private readonly string directory;
    private readonly string tracePath;
    private readonly string fixtureFile;
    private readonly List<FixtureStep> steps;
    private readonly Dictionary<int, UploadCapture> uploads = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private int nextStep;
    private int calls;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> CredentialNames = new(StringComparer.OrdinalIgnoreCase)
    { "bot_token", "context_token", "qrcode", "verify_code", "local_token_list", "BotToken", "ContextToken",
      "upload_param", "encrypt_query_param", "encrypted_query_param", "typing_ticket" };

    private OfflineFixtureTransport(string fixturePath, string statePath)
    {
        fixturePath = Path.GetFullPath(fixturePath);
        fixtureFile = Path.GetFileName(fixturePath);
        statePath = Path.GetFullPath(statePath);
        directory = Path.GetDirectoryName(fixturePath)!;
        if (!string.Equals(directory, Path.GetDirectoryName(statePath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("离线 fixture 与显式 state 必须位于同一个新建测试目录。");
        var testId = Path.GetFileName(directory);
        if (!testId.StartsWith("fixture-", StringComparison.Ordinal) || !Guid.TryParseExact(testId[8..], "N", out _))
            throw new ArgumentException("离线测试目录必须使用 fixture-UUID 名称。");
        for (var parent = new DirectoryInfo(directory); parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("离线测试路径不允许符号链接或目录联接。");
        var marker = Path.Combine(directory, ".weixin-offline-test");
        if (!File.Exists(marker) || (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 ||
            File.ReadAllText(marker, Encoding.UTF8).Trim() != testId)
            throw new ArgumentException("离线测试目录缺少匹配的测试哨兵。");
        if (string.Equals(fixturePath, statePath, StringComparison.OrdinalIgnoreCase) ||
            (File.GetAttributes(fixturePath) & FileAttributes.ReparsePoint) != 0 ||
            (File.Exists(statePath) && (File.GetAttributes(statePath) & FileAttributes.ReparsePoint) != 0))
            throw new ArgumentException("离线测试文件路径无效。");
        var info = new FileInfo(fixturePath);
        if (info.Length > 4 * 1024 * 1024) throw new ArgumentException("离线 fixture 超过大小上限。");
        var fixture = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(fixturePath, Encoding.UTF8), Json)
            ?? throw new ArgumentException("离线 fixture 为空。");
        if (fixture.FormatVersion != 1 || fixture.TestId != testId || fixture.Steps is null || fixture.Steps.Count > 1000)
            throw new ArgumentException("离线 fixture 格式或测试目录标识错误。");
        steps = fixture.Steps;
        foreach (var step in steps)
        {
            var apiPath = step.Host == "ilinkai.weixin.qq.com" && step.Path.StartsWith("/ilink/bot/", StringComparison.Ordinal);
            var cdnPath = step.Host == "novac2c.cdn.weixin.qq.com" &&
                ((step.Method == "POST" && step.Path == "/c2c/upload") || (step.Method == "GET" && step.Path == "/c2c/download"));
            if (step.Method is not ("GET" or "POST") || (!apiPath && !cdnPath) ||
                step.Path.Contains('?') || step.Path.Contains('#') || step.Status is < 100 or > 599 ||
                step.DelayMs is < -1 or > 300000 || step.Fault is not (null or "disconnect") ||
                (cdnPath && step.RequireBearer) || step.ContentType is not ("application/json" or "application/octet-stream"))
                throw new ArgumentException("离线 fixture 步骤格式错误。");
            if (step.BodySubset is { } body) ValidateFixtureCredentials(body);
            if (step.Body is { } response) ValidateFixtureCredentials(response);
            if (step.RawBody is not null)
            {
                // Malformed response fixtures can contain punctuation, but no non-fixture credentials.
                try { using var doc = JsonDocument.Parse(step.RawBody); ValidateFixtureCredentials(doc.RootElement); }
                catch (JsonException) { if (step.RawBody.Length > 1024 || step.RawBody.Contains("token", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("非法 JSON fixture 不得包含凭据字段。"); }
            }
            foreach (var pair in step.Query ?? [])
                if (CredentialNames.Contains(pair.Key)) RequireFixtureValue(pair.Value);
            foreach (var header in step.ResponseHeaders ?? [])
            {
                if (!header.Key.Equals("x-encrypted-param", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("离线媒体 fixture 只允许声明 x-encrypted-param 响应头。");
                RequireFixtureValue(header.Value);
            }
            if (step.BodyBase64 is not null) _ = Convert.FromBase64String(step.BodyBase64);
            if (step.ExpectedPlaintextBase64 is not null &&
                (step.Host != "novac2c.cdn.weixin.qq.com" || step.Method != "POST" || step.AesKeyStep is null))
                throw new ArgumentException("离线上传明文断言需要 CDN POST 和先前 AES 捕获步骤。");
            if (step.ExpectedPlaintextBase64 is not null) _ = Convert.FromBase64String(step.ExpectedPlaintextBase64);
            if (step.WaitMarker is not null)
            {
                ValidateMarker(step.WaitMarker);
                if (step.WaitMarker.StartsWith(Path.GetFileName(statePath), StringComparison.OrdinalIgnoreCase) ||
                    step.WaitMarker.Equals(Path.GetFileName(fixturePath), StringComparison.OrdinalIgnoreCase) ||
                    step.WaitMarker is ".weixin-offline-test" or "fixture-trace.jsonl")
                    throw new ArgumentException("离线等待标记不得覆盖 fixture、状态、哨兵或调用轨迹。");
                var markerPath = Path.Combine(directory, step.WaitMarker);
                if (File.Exists(markerPath) && (File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("离线等待标记不得是符号链接。");
            }
        }
        tracePath = Path.Combine(directory, "fixture-trace.jsonl");
        if (File.Exists(tracePath) && (File.GetAttributes(tracePath) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("离线调用轨迹路径无效。");
    }

    public static HttpClient CreateClient(string fixturePath, string statePath) =>
        new(new OfflineFixtureTransport(fixturePath, statePath), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        FixtureStep? step = null;
        int index = -1;
        int call = 0;
        try
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("离线请求缺少地址。");
            var cdn = uri.Host == "novac2c.cdn.weixin.qq.com";
            if (uri.Scheme != "https" || (uri.Host != "ilinkai.weixin.qq.com" && !cdn) || !uri.IsDefaultPort ||
                (cdn && uri.AbsolutePath is not ("/c2c/upload" or "/c2c/download")))
                throw new InvalidOperationException("离线 fixture 只支持官方 HTTPS 主机名。");
            if (cdn && request.Headers.Authorization is not null)
                throw new InvalidOperationException("离线 CDN transport 拒绝所有 Authorization 凭据。");
            if (request.Headers.Authorization is { } auth && (auth.Scheme != "Bearer" || !IsFixtureValue(auth.Parameter)))
                throw new InvalidOperationException("离线 transport 拒绝非测试 Bearer 凭据。");
            var query = ParseQuery(uri.Query);
            foreach (var pair in query)
                if (CredentialNames.Contains(pair.Key)) RequireFixtureValue(pair.Value);
            JsonElement? body = null;
            byte[]? binary = null;
            if (request.Content is not null)
            {
                if (cdn)
                {
                    if (request.Content.Headers.ContentType?.MediaType != "application/octet-stream" ||
                        request.Content.Headers.ContentLength > 4 * 1024 * 1024)
                        throw new InvalidOperationException("离线 CDN 上传须为大小受限的 octet-stream。");
                    binary = await request.Content.ReadAsByteArrayAsync(ct);
                }
                else
                {
                    using var doc = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
                    ValidateFixtureCredentials(doc.RootElement);
                    body = doc.RootElement.Clone();
                }
            }
            index = nextStep;
            call = ++calls;
            if (index >= steps.Count) throw new InvalidOperationException("离线 fixture 步骤已耗尽；禁止网络回退。");
            step = steps[index];
            if (request.Method.Method != step.Method || uri.Host != step.Host || uri.AbsolutePath != step.Path ||
                !EqualQuery(query, ResolveQuery(step.Query ?? [])) || (step.BodySubset is { } expected &&
                (body is null || !Contains(body.Value, expected))))
                throw new InvalidOperationException("离线 fixture 请求与预期步骤不匹配。");
            if (step.RequireBearer != (request.Headers.Authorization is not null))
                throw new InvalidOperationException("离线 fixture Bearer 存在性与预期不匹配。");
            if (uri.AbsolutePath == "/ilink/bot/getuploadurl") CaptureUpload(index, body);
            var binaryEvidence = VerifyUploadBinary(binary, step);
            var mediaEvidence = VerifySentAesKey(body, step);
            nextStep++;
            // Record declared fixture content, the generated correlation ID and a whitelisted media
            // type only. Unmatched fields and arbitrary headers are never copied into logs.
            var traceBody = FixtureTraceBody(body, step.BodySubset);
            var contentType = request.Content?.Headers.ContentType?.MediaType switch
            {
                "application/json" => "application/json",
                "application/octet-stream" => "application/octet-stream",
                null => null,
                _ => "other"
            };
            await TraceAsync(new { call, step = index, phase = "request", method = step.Method, host = step.Host, path = step.Path,
                bearerPresent = request.Headers.Authorization is not null, contentType,
                query = step.Query ?? [], body = traceBody, utc = DateTimeOffset.UtcNow }, ct);
            if (binaryEvidence is not null || mediaEvidence is not null)
                await TraceAsync(new { call, step = index, phase = "fixture-assertion", binary = binaryEvidence,
                    media = mediaEvidence, utc = DateTimeOffset.UtcNow }, ct);
            if (step.WaitMarker is not null)
                await File.WriteAllTextAsync(Path.Combine(directory, step.WaitMarker), "fixture-request-observed", Encoding.UTF8, ct);
        }
        catch
        {
            // A mismatch never writes raw input, request headers, bearer values, or exception details.
            await TraceAsync(new { call, step = index, phase = "rejected", utc = DateTimeOffset.UtcNow }, CancellationToken.None);
            throw;
        }
        finally { gate.Release(); }
        try
        {
            if (step.DelayMs != 0) await Task.Delay(step.DelayMs, ct);
            if (step.Fault == "disconnect") throw new HttpRequestException("离线 fixture 模拟连接中断。");
            var text = step.RawBody ?? (step.Body?.GetRawText() ?? "{\"ret\":0}");
            await TraceAsync(new { call, step = index, phase = "response", status = step.Status, utc = DateTimeOffset.UtcNow }, ct);
            var response = new HttpResponseMessage((HttpStatusCode)step.Status) { RequestMessage = request };
            if (step.BodyBase64 is not null)
            {
                response.Content = new ByteArrayContent(Convert.FromBase64String(step.BodyBase64));
                response.Content.Headers.ContentType = new(step.ContentType);
            }
            else response.Content = new StringContent(text, Encoding.UTF8, step.ContentType);
            foreach (var header in step.ResponseHeaders ?? []) response.Headers.Add(header.Key, header.Value);
            return response;
        }
        catch (OperationCanceledException)
        {
            await TraceAsync(new { call, step = index, phase = "canceled", utc = DateTimeOffset.UtcNow }, CancellationToken.None);
            throw;
        }
        catch (HttpRequestException)
        {
            await TraceAsync(new { call, step = index, phase = "disconnect", utc = DateTimeOffset.UtcNow }, CancellationToken.None);
            throw;
        }
    }

    private Task TraceAsync(object value, CancellationToken ct)
    {
        var trace = JsonSerializer.SerializeToNode(value, Json)!.AsObject();
        trace["processId"] = Environment.ProcessId;
        trace["fixtureFile"] = fixtureFile;
        return File.AppendAllTextAsync(tracePath, trace.ToJsonString(Json) + "\n", new UTF8Encoding(false), ct);
    }

    private static Dictionary<string, string> ParseQuery(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in value.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            if (!result.TryAdd(Uri.UnescapeDataString(pair[0]), pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : ""))
                throw new InvalidOperationException("离线请求包含重复 query 字段。");
        }
        return result;
    }
    private static bool EqualQuery(Dictionary<string, string> actual, Dictionary<string, string> expected) =>
        actual.Count == expected.Count && expected.All(p => actual.TryGetValue(p.Key, out var value) && value == p.Value);

    private Dictionary<string, string> ResolveQuery(Dictionary<string, string> expected)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in expected)
        {
            var value = pair.Value;
            if (value.StartsWith("$uploadFileKey:", StringComparison.Ordinal))
            {
                if (pair.Key != "filekey" || !int.TryParse(value[15..], out var index) || !uploads.TryGetValue(index, out var capture))
                    throw new InvalidOperationException("离线上传 filekey 占位符没有对应的捕获步骤。");
                value = capture.FileKey;
            }
            result.Add(pair.Key, value);
        }
        return result;
    }

    private void CaptureUpload(int index, JsonElement? body)
    {
        if (body is not { } request || !request.TryGetProperty("aeskey", out var aes) ||
            !request.TryGetProperty("filekey", out var file))
            throw new InvalidOperationException("离线媒体上传缺少密钥或 filekey。");
        var hex = aes.GetString(); var fileKey = file.GetString();
        if (hex?.Length != 32 || !hex.All(Uri.IsHexDigit) || fileKey?.Length != 32 || !fileKey.All(Uri.IsHexDigit))
            throw new InvalidOperationException("离线媒体上传密钥或 filekey 格式错误。");
        uploads.Add(index, new(Convert.FromHexString(hex), fileKey));
    }

    private object? VerifyUploadBinary(byte[]? binary, FixtureStep step)
    {
        if (step.ExpectedPlaintextBase64 is null) return null;
        if (binary is null || step.AesKeyStep is not { } index || !uploads.TryGetValue(index, out var capture))
            throw new InvalidOperationException("离线媒体上传断言缺少二进制或 AES 捕获步骤。");
        using var aes = Aes.Create(); aes.Key = capture.Key;
        var plaintext = aes.DecryptEcb(binary, PaddingMode.PKCS7);
        var expected = Convert.FromBase64String(step.ExpectedPlaintextBase64);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(plaintext, expected))
                throw new InvalidOperationException("离线媒体上传解密后的字节不等于 fixture 明文。");
            return new { plaintextVerified = true, aesKeyStep = index, ciphertextBytes = binary.Length,
                plaintextBytes = plaintext.Length, ciphertextSha256 = Convert.ToHexString(SHA256.HashData(binary)),
                plaintextSha256 = Convert.ToHexString(SHA256.HashData(plaintext)) };
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); CryptographicOperations.ZeroMemory(expected); }
    }

    private object? VerifySentAesKey(JsonElement? body, FixtureStep step)
    {
        if (step.AssertSentAesKeyStep is not { } index) return null;
        if (!uploads.TryGetValue(index, out var capture) || body is not { } request || !request.TryGetProperty("msg", out var msg) ||
            !msg.TryGetProperty("item_list", out var items) || items.GetArrayLength() != 1)
            throw new InvalidOperationException("离线媒体发送 AES 断言缺少捕获步骤或单项消息。");
        var item = items[0];
        var descriptor = item.EnumerateObject().FirstOrDefault(p => p.Name is "image_item" or "video_item" or "file_item" or "voice_item");
        if (descriptor.Value.ValueKind != JsonValueKind.Object || !descriptor.Value.TryGetProperty("media", out var media) ||
            !media.TryGetProperty("aes_key", out var key))
            throw new InvalidOperationException("离线媒体发送缺少 AES 描述符。");
        var encoded = Convert.FromBase64String(key.GetString()!);
        byte[] decoded;
        if (encoded.Length == 16) decoded = encoded;
        else if (encoded.Length == 32 && Encoding.ASCII.GetString(encoded).All(Uri.IsHexDigit))
        { decoded = Convert.FromHexString(Encoding.ASCII.GetString(encoded)); CryptographicOperations.ZeroMemory(encoded); }
        else { CryptographicOperations.ZeroMemory(encoded); throw new InvalidOperationException("离线媒体发送密钥编码无效。"); }
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(capture.Key, decoded))
                throw new InvalidOperationException("离线媒体发送密钥不等于上传密钥。");
            bool voice = descriptor.Name == "voice_item";
            bool ratePresent = voice && descriptor.Value.TryGetProperty("sample_rate", out _);
            bool bitsPresent = voice && descriptor.Value.TryGetProperty("bits_per_sample", out _);
            return new { aesKeyVerified = true, aesKeyStep = index,
                itemType = item.GetProperty("type").GetInt32(), descriptorName = descriptor.Name,
                voiceItemPresent = item.TryGetProperty("voice_item", out _),
                fileMimePresent = item.TryGetProperty("file_item", out var file) &&
                    (file.TryGetProperty("mime", out _) || file.TryGetProperty("mime_type", out _) || file.TryGetProperty("content_type", out _)),
                voiceSampleRatePresent = ratePresent, voiceBitsPerSamplePresent = bitsPresent,
                voiceSampleRate = ratePresent ? descriptor.Value.GetProperty("sample_rate").GetInt32() : (int?)null,
                voiceBitsPerSample = bitsPresent ? descriptor.Value.GetProperty("bits_per_sample").GetInt32() : (int?)null,
                voiceEncoding = voice && descriptor.Value.TryGetProperty("encode_type", out var encoding) ? encoding.GetInt32() : (int?)null };
        }
        finally { CryptographicOperations.ZeroMemory(decoded); }
    }

    private static bool Contains(JsonElement actual, JsonElement expected)
    {
        if (expected.ValueKind == JsonValueKind.Object)
            return actual.ValueKind == JsonValueKind.Object && expected.EnumerateObject().All(p =>
                actual.TryGetProperty(p.Name, out var item) && Contains(item, p.Value));
        if (expected.ValueKind == JsonValueKind.Array)
        {
            var items = expected.EnumerateArray().ToArray();
            return actual.ValueKind == JsonValueKind.Array && actual.GetArrayLength() == items.Length &&
                actual.EnumerateArray().Zip(items).All(p => Contains(p.First, p.Second));
        }
        return actual.ValueKind == expected.ValueKind && actual.GetRawText() == expected.GetRawText();
    }
    private static JsonNode? FixtureTraceBody(JsonElement? actual, JsonElement? expected)
    {
        var trace = expected is { } declared ? JsonNode.Parse(declared.GetRawText()) : null;
        if (actual is { } request && request.ValueKind == JsonValueKind.Object && request.TryGetProperty("msg", out var msg) &&
            msg.ValueKind == JsonValueKind.Object && msg.TryGetProperty("client_id", out var id))
        {
            var value = id.GetString();
            if (string.IsNullOrEmpty(value) || value.Length > 128 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not (':' or '-')))
                throw new InvalidOperationException("离线发送关联标识格式错误。");
            trace ??= new JsonObject();
            if (trace is not JsonObject root) throw new InvalidOperationException("离线发送 fixture 必须使用对象。");
            root["msg"] ??= new JsonObject();
            if (root["msg"] is not JsonObject target) throw new InvalidOperationException("离线发送 fixture 消息必须使用对象。");
            target["client_id"] = value;
        }
        return trace;
    }
    private static bool IsFixtureValue(string? value) => value is not null && value.StartsWith("fixture-", StringComparison.Ordinal);
    private static void RequireFixtureValue(string? value)
    { if (!IsFixtureValue(value)) throw new ArgumentException("离线 transport 只接受 fixture- 前缀测试凭据。"); }
    private static void ValidateFixtureCredentials(JsonElement element, string? name = null)
    {
        if (element.ValueKind == JsonValueKind.Object)
        { foreach (var p in element.EnumerateObject()) ValidateFixtureCredentials(p.Value, p.Name); }
        else if (element.ValueKind == JsonValueKind.Array)
        { foreach (var value in element.EnumerateArray()) ValidateFixtureCredentials(value, name); }
        else if (name is not null && CredentialNames.Contains(name))
        { if (element.ValueKind != JsonValueKind.String) throw new ArgumentException("离线测试凭据类型错误。"); RequireFixtureValue(element.GetString()); }
    }
    private static void ValidateMarker(string value)
    { if (value != Path.GetFileName(value) || value.Length is < 1 or > 80 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value is "." or "..")
        throw new ArgumentException("离线等待标记必须是测试目录中的简单文件名。"); }

    private sealed class Fixture
    {
        public int FormatVersion { get; set; }
        public string TestId { get; set; } = "";
        public List<FixtureStep>? Steps { get; set; }
    }
    private sealed class FixtureStep
    {
        public string Host { get; set; } = "ilinkai.weixin.qq.com";
        public string Method { get; set; } = "POST";
        public string Path { get; set; } = "";
        public Dictionary<string, string>? Query { get; set; }
        public JsonElement? BodySubset { get; set; }
        public bool RequireBearer { get; set; }
        public int Status { get; set; } = 200;
        public JsonElement? Body { get; set; }
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
    private sealed record UploadCapture(byte[] Key, string FileKey);
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var upload in uploads.Values) CryptographicOperations.ZeroMemory(upload.Key);
            uploads.Clear(); gate.Dispose();
        }
        base.Dispose(disposing);
    }
}
