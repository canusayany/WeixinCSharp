using System.Buffers.Binary;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Weixin.Protocol;

/// <summary>Memory-only config. Default JSON serialization and diagnostic formatting exclude the ticket.</summary>
public sealed class TypingConfig
{
    [JsonIgnore] public string TypingTicket { get; }
    public bool HasTypingTicket => !string.IsNullOrWhiteSpace(TypingTicket);
    internal TypingConfig(string ticket) => TypingTicket = ticket;
    public override string ToString() => HasTypingTicket ? "TypingConfig (ticket available)" : "TypingConfig (typing unavailable)";
}

public sealed partial class ILinkClient
{
    /// <summary>Tencent's lightweight getConfig/sendTyping timeout defaults to 10 seconds.</summary>
    public TimeSpan TypingRequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim typingConfigGate = new(1, 1);
    private readonly Dictionary<string, TypingCacheEntry> typingConfigCache = new(StringComparer.Ordinal);
    private sealed record TypingCacheEntry(TypingConfig Config, DateTimeOffset ExpiresAt);
    private sealed class WireTypingConfig
    {
        public WireTypingConfig() { }
        [JsonPropertyName("ret")] public int? Ret { get; set; }
        [JsonPropertyName("typing_ticket")] public string? Ticket { get; set; }
    }

    /// <summary>
    /// Obtain a binding-scoped ticket, caching only explicit ret=0 responses in memory.
    /// Callers must not print or persist TypingTicket. The local cache expires after 24 hours.
    /// </summary>
    public async Task<TypingConfig> GetConfigAsync(BotSession session, CancellationToken ct = default)
    {
        var frozen = ValidateAndFreezeTypingSession(session);
        ct.ThrowIfCancellationRequested();
        // Binding changes must not reuse another token's ticket. No raw credentials are
        // used as dictionary keys, exported in diagnostics or written to BotState.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            frozen.BaseUrl + "\n" + frozen.BotId + "\n" + frozen.UserId + "\n" + frozen.BotToken)));
        await typingConfigGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (typingConfigCache.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow) return cached.Config;
            var body = new Dictionary<string, object?>
            {
                ["ilink_user_id"] = frozen.UserId,
                ["base_info"] = BaseInfo
            };
            if (frozen.ContextToken is not null) body["context_token"] = frozen.ContextToken;
            WireTypingConfig result;
            try
            {
                result = await RequestAsync<WireTypingConfig>(frozen.BaseUrl, "ilink/bot/getconfig", body,
                    frozen.BotToken, TypingRequestTimeout, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { throw new ApiException("微信打字配置请求超时。", transient: true); }
            catch (HttpRequestException)
            { throw new ApiException("微信打字配置连接失败。", transient: true); }
            catch (Exception ex) when (ex is JsonException or IOException)
            { throw new ApiException("微信打字配置响应结构异常。"); }
            // Unlike sendmessage's optional success code, config-cache.ts accepts a
            // ticket only when resp.ret === 0. Missing codes are never cached as success.
            if (result.Ret != 0) throw new ApiException("微信打字配置未确认 ret=0，不使用该票据。", ret: result.Ret);
            var config = new TypingConfig(result.Ticket ?? "");
            if (typingConfigCache.Count >= 256 && !typingConfigCache.ContainsKey(key))
            {
                var oldest = typingConfigCache.MinBy(entry => entry.Value.ExpiresAt).Key;
                typingConfigCache.Remove(oldest);
            }
            typingConfigCache[key] = new(config, DateTimeOffset.UtcNow.AddHours(24));
            return config;
        }
        finally { typingConfigGate.Release(); }
    }

    /// <summary>
    /// Send Tencent TypingStatus.TYPING=1 or CANCEL=2 to the bound user.
    /// Official sendTyping ignores the 2xx response body; HTTP acceptance does not
    /// prove that the phone displayed an indicator. No ticket is durably stored.
    /// </summary>
    public async Task SetTypingAsync(BotSession session, bool started, string ticket, CancellationToken ct = default)
    {
        var frozen = ValidateAndFreezeTypingSession(session);
        if (string.IsNullOrWhiteSpace(ticket)) throw new ArgumentException("打字票据不能为空。", nameof(ticket));
        ct.ThrowIfCancellationRequested();
        var uri = new Uri(ValidateBaseUrl(frozen.BaseUrl), "ilink/bot/sendtyping");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add("iLink-App-Id", "bot");
        request.Headers.Add("iLink-App-ClientVersion", "132105");
        request.Headers.Add("AuthorizationType", "ilink_bot_token");
        var random = BinaryPrimitives.ReadUInt32BigEndian(RandomNumberGenerator.GetBytes(4));
        request.Headers.Add("X-WECHAT-UIN", Convert.ToBase64String(Encoding.ASCII.GetBytes(random.ToString(CultureInfo.InvariantCulture))));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", frozen.BotToken);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            ilink_user_id = frozen.UserId, typing_ticket = ticket, status = started ? 1 : 2, base_info = BaseInfo
        }, Json), Encoding.UTF8, "application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TypingRequestTimeout);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var retry = response.Headers.RetryAfter?.Delta;
                if (retry is null && response.Headers.RetryAfter?.Date is { } date) retry = date - DateTimeOffset.UtcNow;
                throw new ApiException($"微信打字状态 HTTP {status}。", httpStatus: status,
                    transient: status is 408 or 429 || status >= 500, retryAfter: retry);
            }
            // Deliberately do not deserialize/inspect the body: api.ts sendTyping
            // makes no JSON/result-code assertion and accepts empty success responses.
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ApiException("微信打字状态请求超时。", transient: true); }
        catch (HttpRequestException)
        { throw new ApiException("微信打字状态连接失败。", transient: true); }
    }

    private BotSession ValidateAndFreezeTypingSession(BotSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var frozen = new BotSession
        {
            BotToken = session.BotToken, BotId = session.BotId, UserId = session.UserId,
            BaseUrl = session.BaseUrl, ContextToken = session.ContextToken
        };
        RequireSession(frozen);
        ValidateBaseUrl(frozen.BaseUrl);
        if (frozen.BotToken.Any(char.IsControl)) throw new InvalidOperationException("绑定凭据格式异常，请重新执行 login。");
        try { _ = new AuthenticationHeaderValue("Bearer", frozen.BotToken); }
        catch (FormatException) { throw new InvalidOperationException("绑定凭据格式异常，请重新执行 login。"); }
        if (TypingRequestTimeout != Timeout.InfiniteTimeSpan &&
            (TypingRequestTimeout < TimeSpan.Zero || TypingRequestTimeout.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(TypingRequestTimeout), "打字请求超时配置无效。");
        return frozen;
    }
}
