using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Weixin.Protocol;

/// <summary>Local SDK lifecycle settings; these are not server quotas or wire fields.</summary>
public sealed class TypingLifecycleOptions
{
    public TimeSpan KeepaliveInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan TimeToLive { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

public sealed partial class ILinkClient
{
    /// <summary>Create an in-memory scope for a frozen binding and cached ticket.</summary>
    public TypingLifecycle CreateTypingLifecycle(BotSession session, string ticket,
        TypingLifecycleOptions? options = null, Action<string>? diagnostic = null)
    {
        var frozen = ValidateAndFreezeTypingSession(session);
        if (string.IsNullOrWhiteSpace(ticket)) throw new ArgumentException("打字票据不能为空。", nameof(ticket));
        options ??= new TypingLifecycleOptions();
        foreach (var duration in new[] { options.KeepaliveInterval, options.TimeToLive, options.CleanupTimeout })
            if (duration <= TimeSpan.Zero || duration.TotalMilliseconds > uint.MaxValue - 1)
                throw new ArgumentOutOfRangeException(nameof(options), "打字生命周期时间配置无效。");
        return new TypingLifecycle((started, ct) => SetTypingAsync(frozen, started, ticket, ct), options, diagnostic);
    }
}

/// <summary>
/// OpenClaw SDK defaults: one initial START, 5-second keepalive and a 60-second local TTL.
/// Each request is one attempt. Two consecutive refresh errors disable keepalive; TTL
/// still closes the scope. STOP cancels and joins active work before one CANCEL.
/// Neither credentials nor tickets are exposed by this scope or persisted.
/// </summary>
public sealed class TypingLifecycle : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Func<bool, CancellationToken, Task> send;
    private readonly TypingLifecycleOptions options;
    private readonly Action<string>? diagnostic;
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationTokenSource authenticationCancellation = new();
    private Task? startTask, refreshTask, stopTask, cancelTask, cancelObservationTask;
    private bool attempted, closed, disposed, startAccepted, cancelAccepted, refreshStoppedAfterErrors;
    private int refreshAttempts, refreshFailures, consecutiveRefreshFailures;
    private ApiException? authenticationError, lastRefreshError, stopError;

    internal TypingLifecycle(Func<bool, CancellationToken, Task> send, TypingLifecycleOptions options, Action<string>? diagnostic)
    {
        this.send = send; this.options = options; this.diagnostic = diagnostic;
        AuthenticationCancellation = authenticationCancellation.Token;
    }

    [JsonIgnore] public CancellationToken AuthenticationCancellation { get; }
    public bool StartAccepted { get { lock (gate) return startAccepted; } }
    public bool CancelAccepted { get { lock (gate) return cancelAccepted; } }
    public bool RefreshStoppedAfterErrors { get { lock (gate) return refreshStoppedAfterErrors; } }
    public int RefreshAttempts => Volatile.Read(ref refreshAttempts);
    public int RefreshFailures => Volatile.Read(ref refreshFailures);
    public int ConsecutiveRefreshFailures => Volatile.Read(ref consecutiveRefreshFailures);
    [JsonIgnore] public ApiException? LastRefreshError { get { lock (gate) return lastRefreshError; } }
    [JsonIgnore] public ApiException? StopError { get { lock (gate) return stopError; } }
    public override string ToString() => nameof(TypingLifecycle);

    /// <summary>One initial request; repeated calls share its result, never resend it.</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        lock (gate)
        {
            if (closed) throw new InvalidOperationException("打字生命周期已经停止。");
            return startTask ??= StartCoreAsync(ct);
        }
    }

    private async Task StartCoreAsync(CancellationToken ct)
    {
        // Assign startTask before work can complete or a concurrent STOP can join it.
        await Task.Yield();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        linked.Token.ThrowIfCancellationRequested();
        lock (gate) attempted = true;
        try { await send(true, linked.Token).ConfigureAwait(false); }
        catch (ApiException ex) when (ex.SessionExpired) { RecordAuthenticationError(ex); throw; }
        lock (gate)
        {
            startAccepted = true;
            if (closed || lifetime.IsCancellationRequested) return;
            var startedAt = Stopwatch.GetTimestamp();
            refreshTask = RefreshAsync(startedAt);
            // TTL is deliberately not joined by StopCore: it may itself initiate STOP.
            _ = ExpireAsync();
        }
    }

    private async Task RefreshAsync(long startedAt)
    {
        await Task.Yield();
        long tick = 1;
        TimeSpan notBefore = TimeSpan.Zero;
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var due = TimeSpan.FromTicks(checked(options.KeepaliveInterval.Ticks * tick));
                var remaining = due - Stopwatch.GetElapsedTime(startedAt);
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, lifetime.Token).ConfigureAwait(false);
                if (lifetime.IsCancellationRequested) break;
                if (Stopwatch.GetElapsedTime(startedAt) >= notBefore)
                {
                    Interlocked.Increment(ref refreshAttempts);
                    try
                    {
                        await send(true, lifetime.Token).ConfigureAwait(false);
                        Interlocked.Exchange(ref consecutiveRefreshFailures, 0);
                    }
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                    catch (ApiException ex) when (ex.SessionExpired)
                    {
                        Interlocked.Increment(ref refreshFailures);
                        Interlocked.Increment(ref consecutiveRefreshFailures);
                        lock (gate) lastRefreshError = ex;
                        RecordAuthenticationError(ex);
                        break;
                    }
                    catch (Exception ex)
                    {
                        var safe = SafeRefreshError(ex);
                        lock (gate) lastRefreshError = safe;
                        Interlocked.Increment(ref refreshFailures);
                        var failures = Interlocked.Increment(ref consecutiveRefreshFailures);
                        Warn(failures >= 2 ? "打字状态连续两次刷新未确认，停止刷新；本地 TTL 仍会取消。" : "打字状态刷新暂未确认。");
                        if (failures >= 2)
                        {
                            lock (gate) refreshStoppedAfterErrors = true;
                            break;
                        }
                        if (safe.RetryAfter is { } retry && retry > TimeSpan.Zero)
                            notBefore = Stopwatch.GetElapsedTime(startedAt) + (retry > options.TimeToLive ? options.TimeToLive : retry);
                    }
                }
                // Fixed cadence skips ticks elapsed during an active request, rather
                // than overlapping or immediately replaying an accumulated timer tick.
                tick = Math.Max(tick + 1, Stopwatch.GetElapsedTime(startedAt).Ticks / options.KeepaliveInterval.Ticks + 1);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task ExpireAsync()
    {
        try
        {
            await Task.Delay(options.TimeToLive, lifetime.Token).ConfigureAwait(false);
            await StopAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (ApiException) { /* Stored on the shared STOP task and observable by Stop/Dispose. */ }
    }

    /// <summary>
    /// Idempotent, bounded cleanup independent of the user's cancelled operation token.
    /// A failed CANCEL is observable; callers may explicitly issue a separate stop later.
    /// </summary>
    public Task StopAsync()
    {
        lock (gate)
        {
            closed = true;
            return stopTask ??= StopCoreAsync();
        }
    }

    private async Task StopCoreAsync()
    {
        await Task.Yield();
        using var cleanup = new CancellationTokenSource(options.CleanupTimeout);
        try
        {
            await lifetime.CancelAsync().WaitAsync(cleanup.Token).ConfigureAwait(false);
            Task? initial;
            lock (gate) initial = startTask;
            if (initial is not null)
                try { await initial.WaitAsync(cleanup.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cleanup.IsCancellationRequested) { }
                catch (Exception) when (!cleanup.IsCancellationRequested) { /* Initial START caller retains its own result. */ }
            Task? active;
            lock (gate) active = refreshTask;
            if (active is not null) await active.WaitAsync(cleanup.Token).ConfigureAwait(false);
            bool shouldCancel;
            lock (gate)
            {
                if (authenticationError is not null) throw authenticationError;
                shouldCancel = attempted;
            }
            if (shouldCancel)
            {
                var pendingCancel = send(false, cleanup.Token);
                lock (gate)
                {
                    cancelTask = pendingCancel;
                    cancelObservationTask = ObserveCancelCompletionAsync(cancelTask);
                }
                // A transport can ignore its token. Bound waiting as well as the
                // request token, preserving the failed shared STOP result if its
                // one CANCEL subsequently finishes after the cleanup deadline.
                await pendingCancel.WaitAsync(cleanup.Token).ConfigureAwait(false);
                lock (gate) cancelAccepted = true;
            }
        }
        catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
        {
            var safe = new ApiException("打字取消清理超时，取消尚未确认。", transient: true);
            lock (gate) stopError = safe;
            throw safe;
        }
        catch (ApiException ex)
        {
            if (ex.SessionExpired) RecordAuthenticationError(ex);
            lock (gate) stopError = ex;
            throw;
        }
        catch (Exception)
        {
            var safe = new ApiException("打字取消清理发生本地或连接错误，取消尚未确认。", transient: true);
            lock (gate) stopError = safe;
            throw safe;
        }
    }

    private void RecordAuthenticationError(ApiException error)
    {
        lock (gate) authenticationError ??= error;
        // Cancellation only signals local callers; it adds no network request.
        authenticationCancellation.Cancel();
        lifetime.Cancel();
    }

    private async Task ObserveCancelCompletionAsync(Task pending)
    {
        try { await pending.ConfigureAwait(false); }
        catch (ApiException ex) when (ex.SessionExpired) { RecordAuthenticationError(ex); }
        catch { /* STOP owns its result; observe late faults without secret diagnostics. */ }
    }

    private static ApiException SafeRefreshError(Exception error) => error is ApiException api ? api :
        new ApiException("打字状态刷新发生本地或连接错误，尚未确认。", transient: true);

    private void Warn(string message)
    {
        try { diagnostic?.Invoke(message); }
        catch { /* Diagnostic consumers must not break lifecycle cleanup. */ }
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync().ConfigureAwait(false); }
        finally
        {
            lock (gate)
            {
                if (!disposed)
                {
                    disposed = true;
                    // A broken transport can ignore cancellation and outlive the
                    // cleanup budget. Keep its tokens valid until that work exits;
                    // never wait unbounded here or send a concurrent late CANCEL.
                    _ = ReleaseTokensAsync(startTask, refreshTask, cancelObservationTask);
                }
            }
        }
    }

    private async Task ReleaseTokensAsync(Task? initial, Task? refresh, Task? cancelObservation)
    {
        foreach (var task in new[] { initial, refresh, cancelObservation })
            if (task is not null)
                try { await task.ConfigureAwait(false); }
                catch { /* The START caller/shared STOP result already owns failures. */ }
        lifetime.Dispose();
        authenticationCancellation.Dispose();
    }
}
