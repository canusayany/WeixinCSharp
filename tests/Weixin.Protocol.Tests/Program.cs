using Weixin.Protocol.Tests;
using System.Diagnostics;
using System.Text.Json;

var started = DateTimeOffset.UtcNow;
var results = new List<object>();
var output = args.Length == 2 && args[0] == "--output" ? Path.GetFullPath(args[1]) : null;
try
{
    await RunSuite("StateVaultTests", StateVaultTests.RunAsync);
    await RunSuite("ILinkClientTests", ILinkClientTests.RunAsync);
    await RunSuite("PersistentBotRunnerTests", PersistentBotRunnerTests.RunAsync);
    await RunSuite("RunnerRegressionTests", RunnerRegressionTests.RunAsync);
    await RunSuite("MediaModelAndMarkdownTests", MediaModelAndMarkdownTests.RunAsync);
    await RunSuite("MediaClientTests", MediaClientTests.RunAsync);
    await RunSuite("TypingClientTests", TypingClientTests.RunAsync);
    await RunSuite("TypingLifecycleTests", TypingLifecycleTests.RunAsync);
    Console.WriteLine("ALL TESTS PASSED (offline HTTP fixtures + real Windows DPAPI).");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("UNIT TEST FAILED: " + ex.Message);
    return 1;
}
finally
{
    if (output is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            formatVersion = 1, level = "offline-unit-real-Windows-DPAPI", started,
            completed = DateTimeOffset.UtcNow, realWechatAccountUsed = false,
            suites = results
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

async Task RunSuite(string name, Func<Task> run)
{
    var clock = Stopwatch.StartNew();
    try { await run(); results.Add(new { name, status = "passed", elapsedMs = clock.ElapsedMilliseconds }); }
    catch (Exception ex)
    {
        results.Add(new { name, status = "failed", elapsedMs = clock.ElapsedMilliseconds, errorType = ex.GetType().Name });
        throw;
    }
}
