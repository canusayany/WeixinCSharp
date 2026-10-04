using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Weixin.Protocol;

namespace Weixin.Protocol.Tests;

/// <summary>Offline concurrency and lifecycle regressions; no live binding or network.</summary>
public static class TypingLifecycleTests
{
    private const string Token = "fixture-lifecycle-token-secret";
    private const string Ticket = "fixture-lifecycle-ticket-secret";
    public static async Task RunAsync()
    {
        var tests = new Func<Task>[]
        {
            DefaultsAndFrozenBindingAsync, RefreshCadenceAsync, ConcurrentStartsShareOneRequestAsync,
            BusyRefreshDoesNotOverlapOrCatchUpAsync, StopJoinsCancelledRefreshAsync,
            RepeatedStopIsIdempotentAsync, InitialFailureStillCleansUpAsync, InitialAuthNeverCancelsAsync,
            RefreshAuthStopsAllWorkAsync, SuccessResetsFailureGuardAsync, TwoFailuresKeepTtlAsync,
            TtlThenDisposeDoesNotCancelAgainAsync, CleanupTimeoutIsObservableAsync,
            UncooperativeRefreshHasBoundedCleanupAsync,
            UncooperativeCancelLateSuccessAsync, UncooperativeCancelLateAuthAsync,
            UncooperativeCancelLateFaultAsync,
            UserCancellationStillCleansUpAsync, StopFailureDoesNotRetryAsync,
            RetryAfterAndDiagnosticsAreSafeAsync, InvalidLocalConfigurationUsesNoHttpAsync
        };
        foreach (var test in tests) await test();
        Console.WriteLine($"TypingLifecycleTests: {tests.Length} test groups passed (offline HTTP lifecycle/concurrency).");
    }

    private static async Task DefaultsAndFrozenBindingAsync()
    {
        var defaults = new TypingLifecycleOptions();
        Assert(defaults.KeepaliveInterval == TimeSpan.FromSeconds(5) && defaults.TimeToLive == TimeSpan.FromSeconds(60) &&
            defaults.CleanupTimeout == TimeSpan.FromSeconds(10), "Official SDK cadence/TTL and bounded local cleanup defaults must remain explicit.");
        using var fixture = new Fixture();
        var session = Session();
        var scope = fixture.Client.CreateTypingLifecycle(session, Ticket, Short());
        session.BotToken = "changed-token"; session.UserId = "changed-peer"; session.BaseUrl = "https://untrusted.example.invalid";
        await scope.StartAsync(); await scope.DisposeAsync();
        Assert(fixture.Requests.All(r => r.Peer == "fixture-peer" && r.Authorization == "Bearer " + Token && r.Ticket == Ticket && r.Path == "/ilink/bot/sendtyping"),
            "The scope must use its frozen validated binding for both START and CANCEL.");
        Assert(fixture.Statuses.SequenceEqual(new[] { 1, 2 }) && scope.StartAccepted && scope.CancelAccepted,
            "A short completed scope has exactly one accepted START and CANCEL.");
        Assert(!JsonSerializer.Serialize(scope).Contains(Token, StringComparison.Ordinal) && !JsonSerializer.Serialize(scope).Contains(Ticket, StringComparison.Ordinal) &&
            !scope.ToString().Contains(Ticket, StringComparison.Ordinal), "Default serialization and diagnostics must expose no token/ticket/session.");
    }

    private static async Task RefreshCadenceAsync()
    {
        using var fixture = new Fixture();
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short(intervalMs: 45));
        await scope.StartAsync();
        await WaitUntilAsync(() => scope.RefreshAttempts >= 3);
        await scope.DisposeAsync();
        var starts = fixture.Requests.Where(r => r.Status == 1).ToArray();
        Assert(starts.Length >= 4 && starts.Zip(starts.Skip(1), (a, b) => b.Elapsed - a.Elapsed).All(gap => gap >= TimeSpan.FromMilliseconds(25)),
            "Keepalive must be periodic START calls, not an immediate tight loop.");
        Assert(fixture.MaximumConcurrent == 1 && fixture.Statuses.Last() == 2, "Refreshes and final CANCEL must never overlap.");
    }

    private static async Task ConcurrentStartsShareOneRequestAsync()
    {
        var entered = Signal(); var release = Signal();
        using var fixture = new Fixture(async (_, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return Reply(); });
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short());
        var first = scope.StartAsync(); var again = scope.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert(ReferenceEquals(first, again) && fixture.Statuses.Length == 1, "Concurrent initial START calls must share the same one-attempt result.");
        release.TrySetResult(); await Task.WhenAll(first, again); await scope.DisposeAsync();
        Assert(fixture.Statuses.SequenceEqual(new[] { 1, 2 }), "Calling Start twice must not duplicate the primitive request.");
    }

    private static async Task BusyRefreshDoesNotOverlapOrCatchUpAsync()
    {
        var entered = Signal(); var release = Signal(); int starts = 0;
        using var fixture = new Fixture(async (r, ct) =>
        {
            if (r.Status == 1 && Interlocked.Increment(ref starts) == 2)
            { entered.TrySetResult(); await release.Task.WaitAsync(ct); }
            return Reply();
        });
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short(intervalMs: 60));
        await scope.StartAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(135);
        Assert(fixture.Statuses.Length == 2 && fixture.MaximumConcurrent == 1, "Ticks that occur during a refresh must not start another request.");
        var releasedAt = fixture.Elapsed; release.TrySetResult();
        await WaitUntilAsync(() => scope.RefreshAttempts >= 2);
        await scope.DisposeAsync();
        var third = fixture.Requests.Where(r => r.Status == 1).Skip(2).First();
        Assert(third.Elapsed - releasedAt >= TimeSpan.FromMilliseconds(15) && fixture.MaximumConcurrent == 1,
            "Missed ticks must be skipped, not replayed as an immediate catch-up refresh.");
    }

    private static async Task StopJoinsCancelledRefreshAsync()
    {
        var entered = Signal(); bool refreshExited = false; int starts = 0;
        using var fixture = new Fixture(async (r, ct) =>
        {
            if (r.Status == 1 && Interlocked.Increment(ref starts) == 2)
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                finally { refreshExited = true; }
            }
            if (r.Status == 2) Assert(refreshExited, "CANCEL must follow completion of the cancelled active START.");
            return Reply();
        });
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short());
        await scope.StartAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await scope.DisposeAsync();
        Assert(fixture.Statuses.SequenceEqual(new[] { 1, 1, 2 }) && fixture.MaximumConcurrent == 1 && scope.CancelAccepted,
            "Stop must cancel an in-flight refresh, join it, then send exactly one CANCEL.");
    }

    private static async Task RepeatedStopIsIdempotentAsync()
    {
        using var fixture = new Fixture();
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short());
        await scope.StartAsync(); await WaitUntilAsync(() => scope.RefreshAttempts >= 1);
        await Task.WhenAll(scope.StopAsync(), scope.StopAsync(), scope.DisposeAsync().AsTask(), scope.DisposeAsync().AsTask());
        var count = fixture.Statuses.Length; await Task.Delay(100);
        Assert(fixture.Statuses.Count(s => s == 2) == 1 && fixture.Statuses.Length == count,
            "Concurrent/repeated STOP and disposal must send one CANCEL and leave no post-stop refresh.");
        await ThrowsAsync<InvalidOperationException>(() => scope.StartAsync());
    }

    private static async Task InitialFailureStillCleansUpAsync()
    {
        using var fixture = new Fixture((r, _) => Task.FromResult(Reply(r.Status == 1 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)));
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short());
        var error = await ThrowsAsync<ApiException>(() => scope.StartAsync()); Safe(error);
        await scope.DisposeAsync();
        Assert(fixture.Statuses.SequenceEqual(new[] { 1, 2 }) && !scope.StartAccepted && scope.CancelAccepted && scope.RefreshAttempts == 0,
            "A generic initial failure remains visible and is not retried; legacy finally still sends CANCEL once.");
    }

    private static async Task InitialAuthNeverCancelsAsync()
    {
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden })
        {
            using var fixture = new Fixture((_, _) => Task.FromResult(Reply(status)));
            var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short());
            var start = await ThrowsAsync<ApiException>(() => scope.StartAsync());
            var stop = await ThrowsAsync<ApiException>(() => scope.DisposeAsync().AsTask()); Safe(start); Safe(stop);
            Assert(start.SessionExpired && stop.SessionExpired && scope.AuthenticationCancellation.IsCancellationRequested && fixture.Statuses.SequenceEqual(new[] { 1 }),
                "Initial authentication failure must remain observable and prohibit refresh and CANCEL.");
        }
    }

    private static async Task RefreshAuthStopsAllWorkAsync()
    {
        int starts = 0;
        using var fixture = new Fixture((r, _) => Task.FromResult(Reply(r.Status == 1 && Interlocked.Increment(ref starts) == 2 ? HttpStatusCode.Forbidden : HttpStatusCode.OK)));
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short());
        await scope.StartAsync(); await WaitUntilAsync(() => scope.AuthenticationCancellation.IsCancellationRequested);
        var error = await ThrowsAsync<ApiException>(() => scope.DisposeAsync().AsTask()); Safe(error);
        await Task.Delay(90);
        Assert(error.SessionExpired && fixture.Statuses.SequenceEqual(new[] { 1, 1 }) && !scope.CancelAccepted,
            "Refresh auth rejection must cancel the local operation and prevent any later refresh/CANCEL.");
    }

    private static async Task SuccessResetsFailureGuardAsync()
    {
        int starts = 0; var warnings = new List<string>();
        using var fixture = new Fixture((r, _) => Task.FromResult(Reply(r.Status == 1 && Interlocked.Increment(ref starts) is 2 or 4 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short(), warnings.Add);
        await scope.StartAsync(); await WaitUntilAsync(() => scope.RefreshAttempts >= 4 && scope.ConsecutiveRefreshFailures == 0);
        await scope.DisposeAsync();
        Assert(scope.RefreshFailures == 2 && !scope.RefreshStoppedAfterErrors && warnings.Count == 2 && scope.CancelAccepted,
            "A successful refresh must reset the consecutive-failure guard, permitting later scheduled refreshes.");
    }

    private static async Task TwoFailuresKeepTtlAsync()
    {
        int starts = 0;
        using var fixture = new Fixture((r, _) => r.Status == 1 && Interlocked.Increment(ref starts) > 1 ?
            Task.FromException<HttpResponseMessage>(new HttpRequestException(Token + Ticket)) : Task.FromResult(Reply()));
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short(ttlMs: 210));
        await scope.StartAsync(); await WaitUntilAsync(() => scope.RefreshStoppedAfterErrors);
        Assert(scope.RefreshAttempts == 2 && scope.RefreshFailures == 2 && fixture.Statuses.Count(s => s == 2) == 0,
            "Two consecutive failures must stop only keepalive, without prematurely sending CANCEL.");
        await WaitUntilAsync(() => scope.CancelAccepted);
        await scope.DisposeAsync();
        Assert(fixture.Statuses.SequenceEqual(new[] { 1, 1, 1, 2 }), "TTL must still send one CANCEL after the refresh guard trips.");
        Safe(scope.LastRefreshError!);
    }

    private static async Task TtlThenDisposeDoesNotCancelAgainAsync()
    {
        using var fixture = new Fixture();
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short(ttlMs: 125));
        await scope.StartAsync(); await WaitUntilAsync(() => scope.CancelAccepted);
        var count = fixture.Statuses.Length;
        await scope.DisposeAsync(); await scope.StopAsync(); await Task.Delay(80);
        Assert(fixture.Statuses.Count(s => s == 2) == 1 && count == fixture.Statuses.Length,
            "TTL closure must not deadlock on its own task or produce a second CANCEL during explicit disposal.");
    }

    private static async Task CleanupTimeoutIsObservableAsync()
    {
        using var fixture = new Fixture(async (r, ct) => { if (r.Status == 2) await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Reply(); });
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short(cleanupMs: 65));
        await scope.StartAsync(); var watch = Stopwatch.StartNew();
        var error = await ThrowsAsync<ApiException>(() => scope.DisposeAsync().AsTask()); Safe(error);
        Assert(error.IsTransient && scope.StopError == error && !scope.CancelAccepted && watch.Elapsed < TimeSpan.FromSeconds(2) && fixture.Statuses.SequenceEqual(new[] { 1, 2 }),
            "A cancelled cleanup deadline must be a visible safe API failure, not user-cancellation success or a retry.");
        await ThrowsAsync<ApiException>(() => scope.StopAsync());
        Assert(fixture.Statuses.Length == 2, "Even failed cleanup is idempotent and must not send a second CANCEL.");
    }

    private static async Task UserCancellationStillCleansUpAsync()
    {
        var entered = Signal();
        using var fixture = new Fixture(async (r, ct) =>
        { if (r.Status == 1) { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); } return Reply(); });
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short());
        using var user = new CancellationTokenSource();
        var starting = scope.StartAsync(user.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); user.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => starting); await scope.DisposeAsync();
        Assert(fixture.Statuses.SequenceEqual(new[] { 1, 2 }) && scope.CancelAccepted,
            "User cancellation of an attempted START must retain a separate bounded finally-CANCEL token.");
    }

    private static async Task UncooperativeRefreshHasBoundedCleanupAsync()
    {
        var entered = Signal(); var release = Signal(); int starts = 0;
        using var fixture = new Fixture(async (r, _) =>
        {
            if (r.Status == 1 && Interlocked.Increment(ref starts) == 2)
            { entered.TrySetResult(); await release.Task; return Reply(HttpStatusCode.Forbidden); }
            return Reply();
        });
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short(cleanupMs: 65));
        await scope.StartAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var watch = Stopwatch.StartNew(); var error = await ThrowsAsync<ApiException>(() => scope.DisposeAsync().AsTask()); Safe(error);
        Assert(error.IsTransient && watch.Elapsed < TimeSpan.FromSeconds(2) && fixture.Statuses.SequenceEqual(new[] { 1, 1 }) && !scope.CancelAccepted,
            "A transport ignoring cancellation must not defeat bounded cleanup or permit a concurrent CANCEL.");
        release.TrySetResult(); await WaitUntilAsync(() => scope.AuthenticationCancellation.IsCancellationRequested);
        await Task.Delay(70);
        Assert(fixture.Statuses.SequenceEqual(new[] { 1, 1 }), "Late auth completion after cleanup timeout must be safely observed without disposed-token faults or new HTTP.");
    }

    private static async Task UncooperativeCancelLateSuccessAsync() =>
        _ = await CheckUncooperativeCancelAsync("success");

    private static async Task UncooperativeCancelLateAuthAsync() =>
        _ = await CheckUncooperativeCancelAsync("auth");

    private static async Task UncooperativeCancelLateFaultAsync()
    {
        var unobserved = 0;
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        { Interlocked.Increment(ref unobserved); args.SetObserved(); }
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            var released = await CheckUncooperativeCancelAsync("fault");
            // Faulted shared STOP was awaited; its single late CANCEL must also
            // have an observer even though cleanup's WaitAsync already timed out.
            for (var attempt = 0; attempt < 4 && released.IsAlive; attempt++)
            { GC.Collect(); GC.WaitForPendingFinalizers(); await Task.Delay(15); }
            GC.Collect(); GC.WaitForPendingFinalizers();
            Assert(!released.IsAlive && Volatile.Read(ref unobserved) == 0,
                "Completed late CANCEL faults must be observed and release the scope without unobserved-task exceptions.");
        }
        finally { TaskScheduler.UnobservedTaskException -= OnUnobserved; }
    }

    private static async Task<WeakReference> CheckUncooperativeCancelAsync(string outcome)
    {
        var entered = Signal(); var release = Signal(); var finished = Signal();
        using var fixture = new Fixture(async (r, _) =>
        {
            if (r.Status == 2)
            {
                entered.TrySetResult();
                await release.Task; // Deliberately ignore the cancelled transport token.
                try
                {
                    if (outcome == "fault") throw new HttpRequestException(Token + Ticket);
                    return Reply(outcome == "auth" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
                }
                finally { finished.TrySetResult(); }
            }
            return Reply();
        });
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short(cleanupMs: 65));
        var weak = new WeakReference(scope);
        await scope.StartAsync(); var watch = Stopwatch.StartNew();
        var stopping = scope.StopAsync(); var repeated = scope.StopAsync();
        Assert(ReferenceEquals(stopping, repeated), "All callers must share the same one-attempt STOP task.");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var error = await ThrowsAsync<ApiException>(() => stopping); Safe(error);
        var disposeError = await ThrowsAsync<ApiException>(() => scope.DisposeAsync().AsTask());
        Assert(ReferenceEquals(error, disposeError) && error.IsTransient && scope.StopError == error &&
            watch.Elapsed < TimeSpan.FromSeconds(2) && fixture.Statuses.SequenceEqual(new[] { 1, 2 }) && !scope.CancelAccepted,
            "Even an uncooperative CANCEL must finish local cleanup within budget, fail visibly and never be retried.");
        release.TrySetResult(); await finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (outcome == "auth")
            await WaitUntilAsync(() => scope.AuthenticationCancellation.IsCancellationRequested);
        await Task.Delay(75);
        var lateResult = await ThrowsAsync<ApiException>(() => scope.StopAsync()); Safe(lateResult);
        Assert(ReferenceEquals(error, lateResult) && scope.StopError == error && !scope.CancelAccepted && fixture.Statuses.SequenceEqual(new[] { 1, 2 }),
            "Late success/auth/fault must preserve the failed shared STOP result, produce no retry and make no confirmed-cancel claim.");
        return weak;
    }

    private static async Task StopFailureDoesNotRetryAsync()
    {
        using var fixture = new Fixture((r, _) => r.Status == 2 ? Task.FromException<HttpResponseMessage>(new HttpRequestException(Token + Ticket)) : Task.FromResult(Reply()));
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short());
        await scope.StartAsync(); var error = await ThrowsAsync<ApiException>(() => scope.DisposeAsync().AsTask()); Safe(error);
        await ThrowsAsync<ApiException>(() => scope.StopAsync()); await Task.Delay(90);
        Assert(error.IsTransient && scope.StopError == error && !scope.CancelAccepted && fixture.Statuses.SequenceEqual(new[] { 1, 2 }),
            "A final CANCEL transport error must stay observable, without implicit retry, START or successful-cancel claim.");
    }

    private static async Task RetryAfterAndDiagnosticsAreSafeAsync()
    {
        var entered = Signal(); int starts = 0; var warnings = new List<string>();
        using var fixture = new Fixture((r, _) =>
        {
            var response = Reply();
            if (r.Status == 1 && Interlocked.Increment(ref starts) == 2)
            {
                response.StatusCode = HttpStatusCode.TooManyRequests;
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                entered.TrySetResult();
            }
            return Task.FromResult(response);
        });
        var scope = fixture.Client.CreateTypingLifecycle(Session(), Ticket, Short(ttlMs: 240), warnings.Add);
        await scope.StartAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); await scope.DisposeAsyncAfterDelay(100);
        Assert(scope.RefreshAttempts == 1 && scope.RefreshFailures == 1 && fixture.Statuses.SequenceEqual(new[] { 1, 1, 2 }),
            "RetryAfter must defer later periodic refreshes, while explicit final CANCEL remains an independent one-attempt request.");
        Safe(scope.LastRefreshError!);
        Assert(warnings.Count == 1 && warnings.All(w => !w.Contains(Token, StringComparison.Ordinal) && !w.Contains(Ticket, StringComparison.Ordinal)),
            "Warnings must expose no credentials or transport payload, including errors whose raw message contains secrets.");
    }

    private static Task InvalidLocalConfigurationUsesNoHttpAsync()
    {
        using var fixture = new Fixture();
        foreach (var options in new[] { Short(intervalMs: 0), Short(ttlMs: -1), Short(cleanupMs: 0) })
            Throws<ArgumentOutOfRangeException>(() => fixture.Client.CreateTypingLifecycle(Session(), Ticket, options));
        var broken = Session(); broken.BotToken = "";
        Throws<InvalidOperationException>(() => fixture.Client.CreateTypingLifecycle(broken, Ticket));
        Throws<ArgumentException>(() => fixture.Client.CreateTypingLifecycle(Session(), " "));
        Assert(fixture.Statuses.Length == 0, "Invalid local configuration/binding/ticket must fail before any HTTP or cleanup attempt.");
        return Task.CompletedTask;
    }

    private static async Task DisposeAsyncAfterDelay(this TypingLifecycle scope, int milliseconds)
    { await Task.Delay(milliseconds); await scope.DisposeAsync(); }
    private static TypingLifecycleOptions Short(int intervalMs = 35, int ttlMs = 1000, int cleanupMs = 250) => new()
    { KeepaliveInterval = TimeSpan.FromMilliseconds(intervalMs), TimeToLive = TimeSpan.FromMilliseconds(ttlMs), CleanupTimeout = TimeSpan.FromMilliseconds(cleanupMs) };
    private static BotSession Session() => new() { BotToken = Token, BotId = "fixture-bot", UserId = "fixture-peer" };
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static HttpResponseMessage Reply(HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(Token + Ticket, Encoding.UTF8, "application/json") };
    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(3)) throw new InvalidOperationException("Timed out waiting for offline lifecycle evidence.");
            await Task.Delay(5);
        }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Safe(Exception error) => Assert(error.InnerException is null && !error.ToString().Contains(Token, StringComparison.Ordinal) && !error.ToString().Contains(Ticket, StringComparison.Ordinal),
        "Lifecycle failures must exclude credentials and raw inner exceptions.");
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T error) { return error; } throw new InvalidOperationException("Expected " + typeof(T).Name); }

    private sealed record Request(int Status, string Peer, string Ticket, string? Authorization, string Path, TimeSpan Elapsed);
    private sealed class Fixture : IDisposable
    {
        private readonly object gate = new();
        private readonly List<Request> requests = [];
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly HttpClient http;
        private int active, maximumConcurrent;
        public ILinkClient Client { get; }
        public Request[] Requests { get { lock (gate) return requests.ToArray(); } }
        public int[] Statuses => Requests.Select(r => r.Status).ToArray();
        public int MaximumConcurrent => Volatile.Read(ref maximumConcurrent);
        public TimeSpan Elapsed => clock.Elapsed;
        public Fixture(Func<Request, CancellationToken, Task<HttpResponseMessage>>? reply = null)
        {
            http = new HttpClient(new Handler(async (request, ct) =>
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var record = new Request(body.RootElement.GetProperty("status").GetInt32(), body.RootElement.GetProperty("ilink_user_id").GetString()!,
                    body.RootElement.GetProperty("typing_ticket").GetString()!, request.Headers.Authorization?.ToString(), request.RequestUri!.AbsolutePath, clock.Elapsed);
                lock (gate) requests.Add(record);
                var concurrent = Interlocked.Increment(ref active);
                Interlocked.Exchange(ref maximumConcurrent, Math.Max(maximumConcurrent, concurrent));
                try { return reply is null ? Reply() : await reply(record, ct); }
                finally { Interlocked.Decrement(ref active); }
            })) { Timeout = Timeout.InfiniteTimeSpan };
            Client = new ILinkClient(null, http);
        }
        public void Dispose() { Client.Dispose(); http.Dispose(); }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
