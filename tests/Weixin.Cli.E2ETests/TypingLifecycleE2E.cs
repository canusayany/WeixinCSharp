using System.Diagnostics;

internal static partial class CliSuite
{
    private static async Task TypingLifecycleCasesAsync()
    {
        await Case("typing_timed_keepalive_refreshes_after_five_seconds_then_cancels_once", TypingKeepsAliveAsync);
        await Case("typing_stop_cancels_inflight_keepalive_without_late_restart", TypingStopsInflightRefreshAsync);
        await Case("typing_two_consecutive_refresh_failures_stop_ticks_and_still_cancel_once", TypingRefreshFailuresAsync);
        await Case("typing_refresh_auth_failure_exits_two_without_cancel_or_chat", TypingRefreshAuthFailureAsync);
        await Case("typing_cancel_deadline_exits_two_without_success_or_retry", TypingCancelDeadlineAsync);
    }

    private static async Task TypingKeepsAliveAsync()
    {
        var t = await TestDirectory.CreateAsync([TypingConfigStep(), TypingStep(true), TypingStep(true), TypingStep(false)]);
        await t.SaveAsync(State());
        var before = await HashAsync(t.State);
        var result = await RunAsync(t, "typing", ["--typing-status", "start", "--run-for", "6.2"], 0);
        Assert(result.Stdout.Contains("取消", StringComparison.Ordinal), "Timed typing did not confirm its final cancellation.");
        Assert(!result.Stdout.Contains(TypingTicket, StringComparison.Ordinal) && !result.Stderr.Contains(TypingTicket, StringComparison.Ordinal), "Lifecycle leaked the ticket to application output.");
        Assert(await HashAsync(t.State) == before && (await t.LoadAsync()).Outbox.Count == 0, "Keepalive changed durable state or sent chat.");
        t.AssertRequests("getconfig", "sendtyping", "sendtyping", "sendtyping");
        AssertFiveSecondRefresh(t, 1, 2);
    }

    private static async Task TypingStopsInflightRefreshAsync()
    {
        var refresh = TypingStep(true);
        refresh.DelayMs = 10_000;
        refresh.WaitMarker = "keepalive-in-flight.marker";
        var t = await TestDirectory.CreateAsync([TypingConfigStep(), TypingStep(true), refresh, TypingStep(false)]);
        await t.SaveAsync(State());
        var before = await HashAsync(t.State);
        using var child = Start(t, "typing", ["--typing-status", "start", "--run-for", "6.2"]);
        var watch = Stopwatch.StartNew();
        await WaitFileAsync(Path.Combine(t.Directory, "keepalive-in-flight.marker"), child, TimeSpan.FromSeconds(10));
        var result = await child.CompleteAsync(TimeSpan.FromSeconds(15));
        Assert(result.ExitCode == 0 && watch.Elapsed < TimeSpan.FromSeconds(15), "Stopping did not bound and cancel its in-flight refresh.");
        Assert(result.Stdout.Contains("取消", StringComparison.Ordinal), "Stopping an in-flight refresh did not confirm CANCEL.");
        Assert(!result.Stdout.Contains(TypingTicket, StringComparison.Ordinal) && !result.Stderr.Contains(TypingTicket, StringComparison.Ordinal), "Refresh cleanup leaked the ticket.");
        Assert(await HashAsync(t.State) == before && (await t.LoadAsync()).Outbox.Count == 0, "Refresh cleanup changed durable state or sent chat.");
        t.AssertRequests("getconfig", "sendtyping", "sendtyping", "sendtyping");
        AssertFiveSecondRefresh(t, 1, 2);
        var trace = t.Trace();
        var refreshEvents = trace.Where(e => e.GetProperty("step").GetInt32() == 2).ToArray();
        Assert(refreshEvents.Count(e => e.GetProperty("phase").GetString() == "canceled") == 1 &&
            !refreshEvents.Any(e => e.GetProperty("phase").GetString() is "response" or "disconnect"),
            "In-flight keepalive was not actually canceled before process exit.");
        var canceledAt = trace.FindIndex(e => e.GetProperty("step").GetInt32() == 2 && e.GetProperty("phase").GetString() == "canceled");
        var stopAt = trace.FindIndex(e => e.GetProperty("step").GetInt32() == 3 && e.GetProperty("phase").GetString() == "request");
        Assert(canceledAt >= 0 && stopAt > canceledAt, "CANCEL was posted before the in-flight START completed cancellation.");
    }

    private static async Task TypingRefreshFailuresAsync()
    {
        var first = TypingStep(true); first.Fault = "disconnect";
        var second = TypingStep(true); second.Fault = "disconnect";
        var t = await TestDirectory.CreateAsync([TypingConfigStep(), TypingStep(true), first, second, TypingStep(false)]);
        await t.SaveAsync(State());
        var before = await HashAsync(t.State);
        var result = await RunAsync(t, "typing", ["--typing-status", "start", "--run-for", "16.2"], 0);
        Assert(result.Stdout.Contains("取消", StringComparison.Ordinal), "After failed refreshes, final CANCEL was not confirmed.");
        Assert(!result.Stdout.Contains(TypingTicket, StringComparison.Ordinal) && !result.Stderr.Contains(TypingTicket, StringComparison.Ordinal), "Refresh failure diagnostics leaked the ticket.");
        Assert(await HashAsync(t.State) == before && (await t.LoadAsync()).Outbox.Count == 0, "Failed keepalives changed durable state or sent chat.");
        // Exact fixture subsets also assert status=1 for each refresh and status=2
        // only for the final call; no third failed refresh or chat POST is accepted.
        t.AssertRequests("getconfig", "sendtyping", "sendtyping", "sendtyping", "sendtyping");
        AssertFiveSecondRefresh(t, 1, 2);
        AssertFiveSecondRefresh(t, 2, 3);
    }

    private static void AssertFiveSecondRefresh(TestDirectory t, int previousIndex, int refreshIndex)
    {
        var requests = t.Requests();
        var elapsed = requests[refreshIndex].GetProperty("utc").GetDateTimeOffset() -
            requests[previousIndex].GetProperty("utc").GetDateTimeOffset();
        // Bound scheduling jitter without allowing immediate, six-second or
        // overlapping refresh implementations to masquerade as the 5s policy.
        Assert(elapsed >= TimeSpan.FromSeconds(4.5) && elapsed <= TimeSpan.FromSeconds(5.9),
            "Keepalive request timing differs from the official five-second default.");
    }

    private static async Task TypingRefreshAuthFailureAsync()
    {
        var t = await TestDirectory.CreateAsync([TypingConfigStep(), TypingStep(true), TypingStep(true, 403)]);
        await t.SaveAsync(State());
        var before = await HashAsync(t.State);
        var watch = Stopwatch.StartNew();
        var result = await RunAsync(t, "typing", ["--typing-status", "start", "--run-for", "16.2"], 2);
        Assert(watch.Elapsed < TimeSpan.FromSeconds(12), "Refresh authentication rejection did not interrupt the local wait.");
        Assert(result.Stderr.Contains("会话失效", StringComparison.Ordinal) &&
            !result.Stdout.Contains("已发送打字取消请求", StringComparison.Ordinal),
            "Refresh authentication rejection was swallowed as successful user cancellation or sent CANCEL.");
        Assert(new[] { TypingTicket, Token, Context }.All(value =>
            !result.Stdout.Contains(value, StringComparison.Ordinal) && !result.Stderr.Contains(value, StringComparison.Ordinal)),
            "Refresh authentication diagnostics leaked binding secrets or the typing ticket.");
        Assert(await HashAsync(t.State) == before && (await t.LoadAsync()).Outbox.Count == 0,
            "Refresh authentication rejection changed durable state or sent chat.");
        t.AssertRequests("getconfig", "sendtyping", "sendtyping");
        AssertFiveSecondRefresh(t, 1, 2);
    }

    private static async Task TypingCancelDeadlineAsync()
    {
        var cancel = TypingStep(false); cancel.DelayMs = 12_000;
        var t = await TestDirectory.CreateAsync([TypingConfigStep(), TypingStep(true), cancel]);
        await t.SaveAsync(State());
        var before = await HashAsync(t.State);
        var watch = Stopwatch.StartNew();
        var result = await RunAsync(t, "typing", ["--typing-status", "start", "--run-for", "0.2"], 2);
        Assert(watch.Elapsed >= TimeSpan.FromSeconds(8.5) && watch.Elapsed < TimeSpan.FromSeconds(16),
            "Timed CANCEL did not enforce its ten-second cleanup deadline.");
        Assert(result.Stderr.Contains("超时", StringComparison.Ordinal) && !result.Stdout.Contains("已发送打字取消请求", StringComparison.Ordinal),
            "Timed-out CANCEL was reported as successful or silent.");
        Assert(new[] { TypingTicket, Token, Context }.All(value =>
            !result.Stdout.Contains(value, StringComparison.Ordinal) && !result.Stderr.Contains(value, StringComparison.Ordinal)),
            "Timed CANCEL deadline exposed binding secrets or the typing ticket.");
        Assert(await HashAsync(t.State) == before && (await t.LoadAsync()).Outbox.Count == 0,
            "Timed CANCEL deadline changed durable state or sent chat.");
        t.AssertRequests("getconfig", "sendtyping", "sendtyping");
        var events = t.Trace().Where(e => e.GetProperty("step").GetInt32() == 2).ToArray();
        // WaitAsync may win its deadline race and terminate this CLI before the
        // handler's asynchronous cancellation-trace append reaches disk. The
        // real exit 2, timeout diagnostic, elapsed time and absent response are
        // the observable process contract, rather than a durable trace guarantee.
        Assert(events.Count(e => e.GetProperty("phase").GetString() == "canceled") <= 1 &&
            !events.Any(e => e.GetProperty("phase").GetString() is "response" or "disconnect"),
            "CANCEL deadline fabricated a success response or a different fault.");
    }
}
