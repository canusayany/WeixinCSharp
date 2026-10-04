using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Weixin.Protocol;

public sealed class BotSession
{
    public string BotToken { get; set; } = "";
    public string BotId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string BaseUrl { get; set; } = ILinkClient.DefaultBaseUrl;
    public string Cursor { get; set; } = "";
    public string? ContextToken { get; set; }
}

public sealed class BotState
{
    public int FormatVersion { get; set; } = 1;
    public string TransportMode { get; set; } = "live";
    public BotSession Session { get; set; } = new();
    public List<InboxEntry> Inbox { get; set; } = [];
    public List<string> SeenKeys { get; set; } = [];
    public List<SendReceipt> Outbox { get; set; } = [];
    public List<MarkdownJob> MarkdownJobs { get; set; } = [];
    public DateTimeOffset? LastSendAttempt { get; set; }
    public DateTimeOffset? BoundAt { get; set; }
}

public sealed class InboxEntry
{
    public string Key { get; set; } = "";
    public InboundMessage Message { get; set; } = new();
}

public sealed class SendReceipt
{
    public string ClientId { get; set; } = "";
    public string? SourceKey { get; set; }
    public string ContentSha256 { get; set; } = "";
    public string? PayloadSha256 { get; set; }
    public string Status { get; set; } = "Sending";
    public DateTimeOffset AttemptedAt { get; set; }
}

public sealed class MarkdownJob
{
    public string SourceKey { get; set; } = "";
    public string ContentSha256 { get; set; } = "";
    public int ChunkCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    // Archive acknowledged chunks independently from the bounded general outbox.
    // Otherwise receipt pruning could cause an older completed job to be resent.
    public List<SendReceipt> CompletedChunks { get; set; } = [];
}

/// <summary>One exclusive vault, one polling loop. Call methods sequentially.</summary>
public sealed class PersistentBotRunner(ILinkClient client, StateVault vault, BotState state)
{
    public BotState State { get; private set; } = state;
    public Action<string>? Diagnostic { get; set; }
    public TimeSpan MinimumSendInterval { get; init; } = TimeSpan.FromSeconds(3);
    public int MaximumConsecutiveReadFailures { get; init; } = 8;
    private TimeSpan pollTimeout = TimeSpan.FromSeconds(40);

    /// <summary>Read-only validation of persisted state before recovery, delivery or network I/O.</summary>
    public void ValidateState()
    {
        if (State is null || State.FormatVersion != 1 || State.TransportMode is not ("live" or "offline-fixture") ||
            State.Session is null || State.Inbox is null || State.SeenKeys is null || State.Outbox is null || State.MarkdownJobs is null)
            throw new InvalidDataException("本地状态版本或结构不受支持。");
        var session = State.Session;
        if (string.IsNullOrWhiteSpace(session.BotToken) || string.IsNullOrWhiteSpace(session.BotId) ||
            string.IsNullOrWhiteSpace(session.UserId) || session.Cursor is null ||
            session.BotToken.Any(char.IsControl))
            throw new InvalidDataException("本地绑定状态缺少必要字段或格式异常，请重新执行 login。");
        try
        {
            _ = new AuthenticationHeaderValue("Bearer", session.BotToken);
            client.ValidateBaseUrl(session.BaseUrl);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        { throw new InvalidDataException("本地绑定的凭据格式或 API 主机配置异常。"); }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in State.SeenKeys)
            if (!IsSha256(key) || !seen.Add(key)) throw new InvalidDataException("本地去重记录结构异常。");
        var pending = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in State.Inbox)
        {
            if (entry is null || entry.Message is null || !IsSha256(entry.Key) || !pending.Add(entry.Key))
                throw new InvalidDataException("本地收件箱结构异常。");
            entry.Message.ValidateStructure();
            // Older optional-ID messages used a serialized fallback hash. Adding typed
            // optional media descriptors can change JSON ordering, so only recompute keys
            // when a stable wire message/client ID exists; retain legacy fallback keys.
            if (!IsBoundMessage(entry.Message, session) || HasWireIdentity(entry.Message) && entry.Key != MessageKey(entry.Message) || !seen.Contains(entry.Key))
                throw new InvalidDataException("本地收件箱消息与绑定或去重记录不一致。");
        }
        var clientIds = new HashSet<string>(StringComparer.Ordinal);
        var sourceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var receipt in State.Outbox)
        {
            if (receipt is null || string.IsNullOrWhiteSpace(receipt.ClientId) || !clientIds.Add(receipt.ClientId) ||
                !IsSha256(receipt.ContentSha256) || receipt.PayloadSha256 is { } payload && !IsSha256(payload) ||
                receipt.Status is not ("Sending" or "Sent" or "Rejected" or "Unknown") ||
                receipt.AttemptedAt == default || receipt.SourceKey is { } source && (!IsSha256(source) || !sourceKeys.Add(source)))
                throw new InvalidDataException("本地发件记录结构异常。");
        }
        var jobSources = new HashSet<string>(StringComparer.Ordinal);
        var archivedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var job in State.MarkdownJobs)
        {
            if (job is null || !IsSha256(job.SourceKey) || !jobSources.Add(job.SourceKey) || !IsSha256(job.ContentSha256) ||
                job.ChunkCount is < 1 or > 64 || job.CreatedAt == default || job.CompletedChunks is null || job.CompletedChunks.Count > job.ChunkCount)
                throw new InvalidDataException("本地 Markdown 作业记录结构异常。");
            for (int index = 0; index < job.CompletedChunks.Count; index++)
            {
                var receipt = job.CompletedChunks[index];
                if (receipt is null || string.IsNullOrWhiteSpace(receipt.ClientId) || !archivedIds.Add(receipt.ClientId) ||
                    !IsSha256(receipt.ContentSha256) || receipt.PayloadSha256 is { } archivedPayload && !IsSha256(archivedPayload) ||
                    receipt.Status != "Sent" || receipt.AttemptedAt == default ||
                    receipt.SourceKey != MarkdownChunkSourceKey(job.SourceKey, index))
                    throw new InvalidDataException("本地 Markdown 已发送分段记录异常。");
                var active = State.Outbox.FirstOrDefault(r => r.ClientId == receipt.ClientId);
                if (active is not null && (active.Status != receipt.Status || active.SourceKey != receipt.SourceKey ||
                    !string.Equals(active.ContentSha256, receipt.ContentSha256, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(active.PayloadSha256, receipt.PayloadSha256, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("本地 Markdown 分段与发件记录不一致。");
            }
        }
        if (State.Inbox.Count > 256 || State.SeenKeys.Count > 4096 || State.Outbox.Count > 128 ||
            State.MarkdownJobs.Count > 64 ||
            State.LastSendAttempt == default(DateTimeOffset) || State.BoundAt == default(DateTimeOffset))
            throw new InvalidDataException("本地状态容量或时间字段异常。");
    }

    public async Task RecoverAsync(CancellationToken ct = default)
    {
        ValidateState();
        // A crash between transmission and recording an acknowledgment leaves Sending on disk.
        var aliasesNeeded = State.Inbox.Where(entry => !HasWireIdentity(entry.Message))
            .Select(entry => MessageKey(entry.Message)).Where(key => !State.SeenKeys.Contains(key, StringComparer.Ordinal)).ToArray();
        if (State.Outbox.Any(r => r.Status == "Sending") || aliasesNeeded.Length > 0)
        {
            var next = Clone(State);
            foreach (var receipt in next.Outbox.Where(r => r.Status == "Sending")) receipt.Status = "Unknown";
            foreach (var alias in aliasesNeeded.Distinct(StringComparer.Ordinal)) next.SeenKeys.Add(alias);
            TrimSeenKeys(next);
            await vault.SaveAsync(next, ct);
            State = next;
        }
    }

    /// <summary>Durably stage inbox AND cursor in one atomic encrypted write.</summary>
    public async Task StageUpdatesAsync(Updates updates, CancellationToken ct = default)
    {
        ValidateState();
        // Validate the complete batch, including messages which would be filtered. A malformed
        // later entry must not poison an earlier context or commit the response cursor.
        if (updates is null || updates.Messages is null) throw new InvalidDataException("收消息响应结构异常。");
        foreach (var message in updates.Messages)
        {
            if (message is null) throw new InvalidDataException("收消息响应包含空消息。");
            message.ValidateStructure();
        }
        var next = Clone(State);
        var seen = next.SeenKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var entry in next.Inbox.Where(entry => !HasWireIdentity(entry.Message)))
        {
            var alias = MessageKey(entry.Message);
            if (seen.Add(alias)) next.SeenKeys.Add(alias);
        }
        foreach (var message in updates.Messages)
        {
            if (!IsBoundMessage(message, next.Session)) continue;
            // Tokens from unbound senders are never accepted.
            if (!string.IsNullOrWhiteSpace(message.ContextToken)) next.Session.ContextToken = message.ContextToken;
            var key = MessageKey(message);
            if (!seen.Add(key)) continue;
            if (next.Inbox.Count >= 256) throw new InvalidOperationException("持久收件箱已满，游标未推进。请先处理现有消息。");
            next.Inbox.Add(new() { Key = key, Message = message });
            next.SeenKeys.Add(key);
        }
        // Empty/missing cursor must not reset a working cursor.
        if (!string.IsNullOrEmpty(updates.Cursor)) next.Session.Cursor = updates.Cursor;
        TrimSeenKeys(next);
        await vault.SaveAsync(next, ct);
        State = next;
        if (updates.TimeoutMilliseconds is > 0)
            pollTimeout = TimeSpan.FromMilliseconds(Math.Clamp((long)updates.TimeoutMilliseconds.Value + 5000, 10000, 120000));
    }

    /// <summary>At-least-once handler delivery; implement idempotent application side effects using entry.Key.</summary>
    public async Task DrainInboxAsync(Func<InboxEntry, CancellationToken, Task> handler, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ValidateState();
        while (State.Inbox.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var first = State.Inbox[0];
            await handler(first, ct);
            // A completed handler is acknowledged even when Ctrl+C was pressed just afterwards.
            ValidateState();
            var next = Clone(State);
            next.Inbox.RemoveAt(0);
            await vault.SaveAsync(next, CancellationToken.None);
            State = next;
        }
    }

    public async Task RunAsync(Func<InboxEntry, CancellationToken, Task> handler, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        await RecoverAsync(ct);
        await DrainInboxAsync(handler, ct);
        await LifecycleAsync(true, ct);
        var failures = 0;
        var sessionUsable = true;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Updates updates;
                ValidateState();
                try { updates = await client.GetUpdatesAsync(State.Session, pollTimeout, ct); }
                catch (ApiException ex) when (ex.IsTransient && !ex.SessionExpired)
                {
                    await BackoffAsync(++failures, ex.RetryAfter, ct); continue;
                }
                catch (HttpRequestException) { await BackoffAsync(++failures, null, ct); continue; }
                // Unknown schema, auth/business errors and persistence failures stop visibly.
                await StageUpdatesAsync(updates, ct);
                failures = 0;
                await DrainInboxAsync(handler, ct);
                // Prevent hot loops if a gateway returns immediate empty results.
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            }
        }
        catch (ApiException ex) when (ex.SessionExpired) { sessionUsable = false; throw; }
        finally { if (sessionUsable) await LifecycleAsync(false, CancellationToken.None); }
    }

    public Task<SendReceipt> SendBoundTextAsync(string text, string? sourceKey = null, CancellationToken ct = default)
    {
        ValidateState();
        ValidateSendConfiguration();
        ValidateSourceKey(sourceKey);
        RejectMarkdownRootKey(sourceKey);
        if (string.IsNullOrWhiteSpace(text) || text.Length > MarkdownFormatting.DefaultTextChunkLimit)
            throw new ArgumentException("每段文本应为 1 至 4000 个 UTF-16 码元；长文本请先分段。");
        var contentSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return SendBoundAsync(contentSha256, (session, id) => client.SendTextAsync(session, text, id, ct), sourceKey, ct);
    }

    /// <summary>Send one uploaded media descriptor with the same durable delivery guarantees as text.</summary>
    public Task<SendReceipt> SendBoundItemAsync(MessageItem item, string? sourceKey = null, CancellationToken ct = default, string? payloadSha256 = null)
    {
        ValidateState();
        ValidateSendConfiguration();
        ValidateSourceKey(sourceKey);
        RejectMarkdownRootKey(sourceKey);
        if (payloadSha256 is not null && !IsSha256(payloadSha256))
            throw new ArgumentException("源文件摘要应为 64 位十六进制 SHA256。", nameof(payloadSha256));
        ILinkClient.ValidateOutboundItem(item);
        // Freeze caller-owned mutable descriptors so the receipt hash and actual sent item
        // describe the same content, even if another caller changes the original object.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(item, ILinkClient.Json);
        var frozen = JsonSerializer.Deserialize<MessageItem>(bytes, ILinkClient.Json)!;
        ILinkClient.ValidateOutboundItem(frozen);
        // Preserve historical UTF-8 text hashes and cross-API source-key idempotency.
        var contentSha256 = Convert.ToHexString(SHA256.HashData(frozen.Type == 1 ? Encoding.UTF8.GetBytes(frozen.TextItem!.Text!) : bytes));
        return SendBoundAsync(contentSha256, (session, id) => client.SendItemAsync(session, frozen, id, ct), sourceKey, ct, payloadSha256: payloadSha256);
    }

    /// <summary>
    /// Filter and send a resumable Markdown job, with at most 64 UTF-16 chunks.
    /// The job/chunk limits are local safeguards; they are not official server quotas.
    /// </summary>
    public async Task<IReadOnlyList<SendReceipt>> SendBoundMarkdownAsync(string markdown, string sourceKey, CancellationToken ct = default)
    {
        ValidateState(); ValidateSendConfiguration(); ValidateSourceKey(sourceKey);
        ArgumentNullException.ThrowIfNull(markdown);
        if (sourceKey is null) throw new ArgumentException("Markdown 作业必须提供稳定的 SHA256 来源键。", nameof(sourceKey));
        var plain = MarkdownFormatting.ConvertToPlainText(markdown);
        var chunks = MarkdownFormatting.ChunkText(plain);
        if (chunks.Count == 0 || chunks.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Markdown 过滤后必须有可发送的文本，每段不能只包含空白。", nameof(markdown));
        if (chunks.Count > 64) throw new ArgumentException("一个 Markdown 作业最多 64 段（本地限制）。", nameof(markdown));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdown)));
        var job = State.MarkdownJobs.FirstOrDefault(j => j.SourceKey == sourceKey);
        if (job is not null)
        {
            if (!string.Equals(job.ContentSha256, hash, StringComparison.OrdinalIgnoreCase) || job.ChunkCount != chunks.Count)
                throw new InvalidOperationException("该 Markdown 来源键已用于其他输入，不能修改内容或段数。");
            // Verify the archived content before skipping any completed segment.
            for (int index = 0; index < job.CompletedChunks.Count; index++)
                if (!string.Equals(job.CompletedChunks[index].ContentSha256,
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chunks[index]))), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Markdown 已发送分段与当前过滤结果不一致。");
        }
        else
        {
            if (State.Outbox.Any(receipt => receipt.SourceKey == sourceKey))
                throw new InvalidOperationException("该来源键已用于单条消息，不能再用于 Markdown 作业。");
            if (State.MarkdownJobs.Count >= 64) throw new InvalidOperationException("Markdown 作业记录已满（本地 64 个），不会自动删除历史记录。");
            var next = Clone(State);
            next.MarkdownJobs.Add(new() { SourceKey = sourceKey, ContentSha256 = hash, ChunkCount = chunks.Count, CreatedAt = DateTimeOffset.UtcNow });
            await vault.SaveAsync(next, ct);
            State = next;
        }
        for (int index = 0; index < chunks.Count; index++)
        {
            job = State.MarkdownJobs.Single(j => j.SourceKey == sourceKey);
            if (index < job.CompletedChunks.Count) continue;
            var text = chunks[index];
            var childSource = MarkdownChunkSourceKey(sourceKey, index);
            var receipt = await SendBoundAsync(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
                (session, id) => client.SendTextAsync(session, text, id, ct), childSource, ct, replySourceKey: sourceKey);
            var next = Clone(State);
            next.MarkdownJobs.Single(j => j.SourceKey == sourceKey).CompletedChunks.Add(new()
            {
                ClientId = receipt.ClientId, SourceKey = receipt.SourceKey, ContentSha256 = receipt.ContentSha256,
                Status = receipt.Status, AttemptedAt = receipt.AttemptedAt
            });
            // A confirmed segment is durably archived even if cancellation arrives after
            // its acknowledgment. A failed archive can resume from the Sent outbox receipt.
            await vault.SaveAsync(next, CancellationToken.None);
            State = next;
        }
        return State.MarkdownJobs.Single(j => j.SourceKey == sourceKey).CompletedChunks.ToArray();
    }

    private async Task<SendReceipt> SendBoundAsync(string contentSha256, Func<BotSession, string, Task> transmit,
        string? sourceKey, CancellationToken ct, string? replySourceKey = null, string? payloadSha256 = null)
    {
        var existing = sourceKey is null ? null : State.Outbox.LastOrDefault(r => r.SourceKey == sourceKey) ??
            State.MarkdownJobs.SelectMany(job => job.CompletedChunks).LastOrDefault(r => r.SourceKey == sourceKey);
        if (existing is not null)
        {
            if (existing.Status == "Sent")
            {
                if (!string.Equals(existing.ContentSha256, contentSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("该来源已发送其他内容，不能以同一个来源键发送不同内容。");
                if (payloadSha256 is not null && !string.Equals(existing.PayloadSha256, payloadSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("该来源已用于其他源文件或媒体元数据。");
                return existing;
            }
            throw new InvalidOperationException("该入站消息已有发送尝试。先核对微信送达情况；不会在重启后自动重发。");
        }
        var sendSession = JsonSerializer.Deserialize<BotSession>(JsonSerializer.SerializeToUtf8Bytes(State.Session, ILinkClient.Json), ILinkClient.Json)!;
        if (sourceKey is not null)
        {
            var source = State.Inbox.FirstOrDefault(i => i.Key == (replySourceKey ?? sourceKey));
            // A business idempotency key can also identify a direct bound send. Inbound
            // replies retain the original source context; other sends use the latest one.
            if (source is not null) sendSession.ContextToken = source.Message.ContextToken;
        }
        if (string.IsNullOrWhiteSpace(sendSession.ContextToken))
            throw new InvalidOperationException("请先 listen，并从绑定微信发消息以取得上下文。");
        if (State.LastSendAttempt is { } last)
        {
            var remaining = MinimumSendInterval - (DateTimeOffset.UtcNow - last);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, ct);
        }
        ct.ThrowIfCancellationRequested();
        var receipt = new SendReceipt
        {
            // Tencent send.ts calls generateId("openclaw-weixin"); random.ts uses epoch
            // milliseconds plus four cryptographically random bytes in lower-case hex.
            ClientId = GenerateClientId(), SourceKey = sourceKey,
            ContentSha256 = contentSha256, PayloadSha256 = payloadSha256, AttemptedAt = DateTimeOffset.UtcNow
        };
        var next = Clone(State);
        // Never discard unresolved attempts or receipts needed by a pending inbox handler.
        if (next.Outbox.Count >= 128)
        {
            var index = next.Outbox.FindIndex(r => r.Status is "Sent" or "Rejected" &&
                !next.Inbox.Any(i => i.Key == r.SourceKey) && !next.MarkdownJobs.Any(job =>
                    job.CompletedChunks.Count < job.ChunkCount && MarkdownChunkSourceKey(job.SourceKey, job.CompletedChunks.Count) == r.SourceKey));
            if (index < 0) throw new InvalidOperationException("发件记录已满，请核对未确定送达的消息。");
            next.Outbox.RemoveAt(index);
        }
        next.Outbox.Add(receipt);
        next.LastSendAttempt = receipt.AttemptedAt;
        await vault.SaveAsync(next, ct); // Write intent before network I/O.
        State = next;
        try
        {
            await transmit(sendSession, receipt.ClientId);
            receipt.Status = "Sent";
        }
        catch (ApiException)
        {
            receipt.Status = "Rejected";
            await vault.SaveAsync(State, CancellationToken.None);
            throw;
        }
        catch
        {
            receipt.Status = "Unknown";
            await vault.SaveAsync(State, CancellationToken.None);
            throw;
        }
        await vault.SaveAsync(State, CancellationToken.None);
        return receipt;
    }

    private async Task LifecycleAsync(bool started, CancellationToken ct)
    {
        ValidateState();
        try { await client.NotifyAsync(State.Session, started, ct); }
        catch (ApiException ex) when (ex.SessionExpired && started) { throw; }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or OperationCanceledException or JsonException or IOException)
        { Diagnostic?.Invoke(started ? "启动通知未确认；继续接收。" : "停止通知未确认。"); }
    }

    private async Task BackoffAsync(int failures, TimeSpan? retryAfter, CancellationToken ct)
    {
        if (failures >= MaximumConsecutiveReadFailures) throw new ApiException("连续读取失败达到上限，已停止。", transient: true);
        var seconds = Math.Min(60, Math.Pow(2, failures)) + Random.Shared.NextDouble();
        if (retryAfter is { } retry) seconds = Math.Max(seconds, Math.Clamp(retry.TotalSeconds, 0, 300));
        Diagnostic?.Invoke($"读取暂时失败，第 {failures} 次；{Math.Ceiling(seconds)} 秒后恢复。");
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
    }

    public static string MessageKey(InboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.ValidateStructure();
        var id = !string.IsNullOrWhiteSpace(message.MessageId) ? "id:" + message.MessageId :
            !string.IsNullOrWhiteSpace(message.ClientId) ? "client:" + message.ClientId :
            "fallback:" + JsonSerializer.Serialize(message, ILinkClient.Json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((message.FromUserId ?? "") + "\n" + id)));
    }

    private static BotState Clone(BotState value) => JsonSerializer.Deserialize<BotState>(
        JsonSerializer.SerializeToUtf8Bytes(value, ILinkClient.Json), ILinkClient.Json)!;

    private static bool IsBoundMessage(InboundMessage message, BotSession session) =>
        message.FromUserId == session.UserId && !string.IsNullOrWhiteSpace(message.ToUserId) &&
        message.ToUserId == session.BotId && message.MessageType == 1 &&
        string.IsNullOrEmpty(message.GroupId) && message.MessageState is null or 2;

    private static bool HasWireIdentity(InboundMessage message) =>
        !string.IsNullOrWhiteSpace(message.MessageId) || !string.IsNullOrWhiteSpace(message.ClientId);

    private static void TrimSeenKeys(BotState state)
    {
        var pending = state.Inbox.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in state.Inbox.Where(entry => !HasWireIdentity(entry.Message))) pending.Add(MessageKey(entry.Message));
        while (state.SeenKeys.Count > 4096)
        {
            var index = state.SeenKeys.FindIndex(key => !pending.Contains(key));
            if (index < 0) break;
            state.SeenKeys.RemoveAt(index);
        }
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void ValidateSourceKey(string? sourceKey)
    {
        if (sourceKey is not null && !IsSha256(sourceKey))
            throw new ArgumentException("来源键应为 64 位十六进制 SHA256。", nameof(sourceKey));
    }

    private void RejectMarkdownRootKey(string? sourceKey)
    {
        if (sourceKey is not null && State.MarkdownJobs.Any(job => job.SourceKey == sourceKey))
            throw new InvalidOperationException("该来源键已用于 Markdown 作业，请用原 Markdown 输入恢复。");
    }

    private static string MarkdownChunkSourceKey(string source, int index) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(source + "\nmarkdown-chunk:" + index.ToString(CultureInfo.InvariantCulture))));

    private string GenerateClientId()
    {
        string id;
        do
        {
            id = "openclaw-weixin:" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + "-" +
                Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        } while (State.Outbox.Any(receipt => receipt.ClientId == id));
        return id;
    }

    private void ValidateSendConfiguration()
    {
        // CancelAfter rejects these timeout values while constructing the request, before
        // SendAsync. Catch such local mistakes before persisting an ambiguous send intent.
        if (client.RequestTimeout != Timeout.InfiniteTimeSpan &&
            (client.RequestTimeout < TimeSpan.Zero || client.RequestTimeout.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(client.RequestTimeout), "请求超时配置无效。");
        if (client.MaxResponseBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(client.MaxResponseBytes), "响应大小上限必须为正数。");
        if (MinimumSendInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MinimumSendInterval), "最小发送间隔不能为负数。");
    }
}
