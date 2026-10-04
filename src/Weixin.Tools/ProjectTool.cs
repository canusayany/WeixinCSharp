using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Weixin.Protocol;

namespace Weixin.Tools;

/// <summary>Build, test and package the repository without another language runtime.</summary>
public static class ProjectTool
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (arguments.Length == 0 || arguments is ["--help"] or ["help"])
        {
            Console.WriteLine("Weixin.Tools (.NET 10)\n  test-all --output <new directory>\n  render-report [--input docs/REPORT.md] [--output docs/REPORT.html]\n  package [--skip-build]\n  audit [--state <private DPAPI file>] [--private-media <directory>] --output <new JSON file>\nRun from the repository directory. All tests use offline fixtures.");
            return 0;
        }
        try
        {
            string root = FindRoot();
            var options = Options.Parse(arguments[1..], arguments[0] == "package" ? ["--skip-build"] : []);
            switch (arguments[0])
            {
                case "render-report":
                    options.RequireOnly("--input", "--output");
                    string input = Resolve(root, options.Get("--input", "docs/REPORT.md"));
                    string htmlOutput = Resolve(root, options.Get("--output", "docs/REPORT.html"));
                    Directory.CreateDirectory(Path.GetDirectoryName(htmlOutput)!);
                    await File.WriteAllTextAsync(htmlOutput,
                        ReportRenderer.RenderDocument(await File.ReadAllTextAsync(input), ReadVersion(root)), new UTF8Encoding(false));
                    Console.WriteLine("Rendered " + Path.GetRelativePath(root, htmlOutput));
                    break;
                case "test-all":
                    options.RequireOnly("--output");
                    await TestAllAsync(root, Resolve(root, options.Required("--output")));
                    break;
                case "package":
                    options.RequireOnly("--skip-build");
                    if (!options.Has("--skip-build"))
                        await TestAllAsync(root, Path.Combine(root, "artifacts", "tests", "package-" + Guid.NewGuid().ToString("N")));
                    foreach (var package in DeliveryPackage.Create(root, ReadVersion(root)))
                        Console.WriteLine($"{package.File}: {package.Bytes} bytes, SHA256 {package.Sha256}");
                    break;
                case "audit":
                    options.RequireOnly("--state", "--private-media", "--output");
                    string auditOutput = Resolve(root, options.Required("--output"));
                    if (File.Exists(auditOutput)) throw new IOException("Use a new audit output file.");
                    string? statePath = options.Optional("--state") is { } state ? Resolve(root, state) : null;
                    string? mediaPath = options.Optional("--private-media") is { } media ? Resolve(root, media) : null;
                    if (mediaPath is not null && !Directory.Exists(mediaPath)) throw new DirectoryNotFoundException("Private media directory is missing.");
                    // StateVault owns the exclusive lifetime lock and clears decrypted serialization buffers.
                    // This tool never prints the state, account identifiers, credential values or private paths.
                    using (var vault = statePath is not null ? new StateVault(statePath) : null)
                    {
                        BotState? botState = vault is not null ? await vault.LoadAsync<BotState>() : null;
                        if (vault is not null && botState is null) throw new InvalidDataException("Private state is missing.");
                        string[] secrets = botState is not null
                            ? new[] { botState.Session.BotToken, botState.Session.BotId, botState.Session.UserId, botState.Session.ContextToken, botState.Session.Cursor }
                                .OfType<string>().Where(x => x.Length >= 8).Distinct(StringComparer.Ordinal).ToArray()
                            : [];
                        var result = await ArchiveAuditor.AuditAsync(root, ReadVersion(root), secrets, mediaPath, statePath is not null);
                        Directory.CreateDirectory(Path.GetDirectoryName(auditOutput)!);
                        await using var output = new FileStream(auditOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        await JsonSerializer.SerializeAsync(output, result, JsonOptions);
                    }
                    Console.WriteLine("Delivery audit passed: no private entry, supplied account secret or supplied private media found.");
                    break;
                default: throw new ArgumentException("Unknown command. Run --help.");
            }
            return 0;
        }
        catch (Exception error)
        {
            // Error messages from file/process APIs can contain private paths or command arguments.
            Console.Error.WriteLine("Tool failed (" + error.GetType().Name + "). No private state or response was printed.");
            return 1;
        }
    }

    public static string FindRoot()
    {
        foreach (string initial in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(initial); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) && File.Exists(Path.Combine(directory.FullName, "WeixinAssistant.slnx")))
                    return directory.FullName;
        throw new DirectoryNotFoundException("Repository root is missing.");
    }

    public static string ReadVersion(string root)
    {
        var versions = XDocument.Load(Path.Combine(root, "Directory.Build.props")).Descendants("Version").Select(x => x.Value.Trim()).ToArray();
        if (versions.Length != 1 || !System.Text.RegularExpressions.Regex.IsMatch(versions[0], @"^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$"))
            throw new InvalidDataException("One safe project version is required.");
        return versions[0];
    }

    private static string Resolve(string root, string path) => Path.GetFullPath(path, root);
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task TestAllAsync(string root, string output)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Use a fresh test output directory.");
        string published = Path.Combine(root, "artifacts", "win-x64-" + ReadVersion(root));
        if (Directory.Exists(published) || File.Exists(published)) throw new IOException("Use a fresh versioned publish directory; old runtime files must never be reused.");
        Directory.CreateDirectory(output);
        var stages = new List<object>();
        var started = DateTimeOffset.UtcNow;
        bool passed = false;
        try
        {
            await Stage("locked-restore", "restore", "WeixinAssistant.slnx", "--locked-mode", "--nologo");
            await Stage("release-build", "build", "WeixinAssistant.slnx", "-c", "Release", "--no-restore", "--nologo");
            await Stage("unit", "run", "--project", "tests/Weixin.Protocol.Tests", "-c", "Release", "--no-build", "--", "--output", Path.Combine(output, "unit-summary.json"));
            await Stage("managed-codec", "run", "--project", "tests/Weixin.VoiceCodec.Tests", "-c", "Release", "--no-build", "--", "--output", Path.Combine(output, "voice-codec"));
            await Stage("project-tools", "run", "--project", "tests/Weixin.Tools.Tests", "-c", "Release", "--no-build", "--", "--output", Path.Combine(output, "tools-summary.json"));
            await Stage("publish", "publish", "src/Weixin.Cli", "-c", "Release", "-r", "win-x64", "--self-contained", "true", "--no-restore", "-o", published, "--nologo");
            await Stage("published-cli-offline-e2e", "run", "--project", "tests/Weixin.Cli.E2ETests", "-c", "Release", "--no-build", "--", "--exe", Path.Combine(published, "weixin.exe"), "--output", Path.Combine(output, "cli-e2e"));
            await Stage("post-publish-locked-restore", "restore", "WeixinAssistant.slnx", "--locked-mode", "--nologo");
            passed = true;
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(output, "pipeline-summary.json"), JsonSerializer.Serialize(new
            {
                formatVersion = 1, scope = "offline-dotnet-pipeline", version = ReadVersion(root), startedUtc = started,
                completedUtc = DateTimeOffset.UtcNow, realWeChatAccountUsed = false, passed, stages
            }, JsonOptions));
        }
        async Task Stage(string name, params string[] arguments)
        {
            var clock = Stopwatch.StartNew();
            Console.WriteLine("Running " + name);
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            if (!process.Start()) throw new IOException(".NET process did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(); }
            catch { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } throw; }
            finally
            {
                await File.WriteAllTextAsync(Path.Combine(output, name + ".stdout.log"), await stdout, new UTF8Encoding(false));
                await File.WriteAllTextAsync(Path.Combine(output, name + ".stderr.log"), await stderr, new UTF8Encoding(false));
            }
            stages.Add(new { name, exitCode = process.ExitCode, durationMilliseconds = clock.ElapsedMilliseconds });
            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine(name + " failed; diagnostics saved in " + Path.GetRelativePath(root, output));
                throw new InvalidOperationException("Offline .NET stage failed.");
            }
        }
    }

    private sealed class Options(Dictionary<string, string?> values)
    {
        public static Options Parse(string[] arguments, string[] flags)
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (int i = 0; i < arguments.Length; i++)
            {
                string name = arguments[i];
                if (!name.StartsWith("--", StringComparison.Ordinal) || values.ContainsKey(name)) throw new ArgumentException("Invalid or duplicate option.");
                string? value = null;
                if (!flags.Contains(name, StringComparer.Ordinal))
                {
                    if (++i >= arguments.Length || arguments[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("An option value is required.");
                    value = arguments[i];
                }
                values.Add(name, value);
            }
            return new(values);
        }
        public void RequireOnly(params string[] allowed) { if (values.Keys.Any(x => !allowed.Contains(x, StringComparer.Ordinal))) throw new ArgumentException("Unknown option."); }
        public bool Has(string key) => values.ContainsKey(key);
        public string? Optional(string key) => values.GetValueOrDefault(key);
        public string Required(string key) => Optional(key) ?? throw new ArgumentException("Required option missing.");
        public string Get(string key, string fallback) => Optional(key) ?? fallback;
    }
}
