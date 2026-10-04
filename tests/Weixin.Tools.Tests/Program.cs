using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Weixin.Tools;

string? output = args is ["--output", var path] ? Path.GetFullPath(path) : args.Length == 0 ? null : throw new ArgumentException("Use --output <new summary JSON file>.");
if (output is not null && File.Exists(output)) throw new IOException("Use a new summary file.");
string fixtureParent = output is not null ? Path.GetDirectoryName(output)! : Path.Combine(ProjectTool.FindRoot(), "artifacts", "tool-migration");
Directory.CreateDirectory(fixtureParent);
string fixtureRoot = Path.Combine(fixtureParent, "tools-fixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixtureRoot);
var results = new List<object>();
var started = DateTimeOffset.UtcNow;
int failures = 0;

await Case("headings_h1_through_h6_and_navigation", () =>
{
    var rendered = ReportRenderer.Render("# One\n## Two\n### Three\n#### Four\n##### Five\n###### Six");
    for (int level = 1; level <= 6; level++) Assert(rendered.Content.Contains("<h" + level), "A heading level is missing.");
    Assert(rendered.Navigation.Contains("#section-1") && rendered.Content.Contains("id=\"section-1\""), "Navigation anchor mismatch.");
    return Task.CompletedTask;
});
await Case("malformed_heading_lines_always_advance", async () =>
{
    string input = string.Join('\n', Enumerable.Range(0, 1000).Select(x => "#literal-" + x)) + "\n#### Four\n####### invalid";
    var rendered = await Task.Run(() => ReportRenderer.Render(input)).WaitAsync(TimeSpan.FromSeconds(2));
    Assert(rendered.Content.Contains("#literal-999") && rendered.Content.Contains("<h4>Four</h4>") && rendered.Content.Contains("####### invalid"), "Malformed text or heading was lost.");
});
await Case("raw_html_and_attribute_content_are_encoded", () =>
{
    var rendered = ReportRenderer.Render("<script>alert(1)</script>\n\n```\" onmouseover=\"run()\n<script>bad</script>\n```");
    Assert(!rendered.Content.Contains("<script>") && rendered.Content.Contains("&lt;script&gt;"), "Raw HTML executed as markup.");
    Assert(!rendered.Content.Contains("data-language=\"\" onmouseover="), "Language attribute was not encoded.");
    return Task.CompletedTask;
});
await Case("unsafe_url_schemes_are_removed", () =>
{
    string links = "[a](javascript:alert) [b](data:text/html,bad) [c](file:///secret) [d](//example.test/path) [e](\\\\example.test\\path) [f](https://name:secret@example.test/)";
    Assert(!ReportRenderer.Inline(links).Contains("href="), "An unsafe link was emitted.");
    string safe = ReportRenderer.Inline("[one](https://example.test/path?a=1&b=2) [two](evidence/test.json) [three](#section-1)");
    Assert(safe.Contains("https://example.test/path?a=1&amp;b=2") && safe.Contains("evidence/test.json") && safe.Contains("#section-1"), "Safe links were dropped or not encoded.");
    string codeLabel = ReportRenderer.Inline("[`source.cs`](evidence/file.cs) `literal [x](javascript:bad)`");
    Assert(codeLabel.Contains("<a href=\"evidence/file.cs\"><code>source.cs</code></a>") && !codeLabel.Contains('\0') && codeLabel.Contains("<code>literal [x](javascript:bad)</code>"), "Inline code/link precedence differs.");
    return Task.CompletedTask;
});
await Case("table_lists_code_bold_and_internal_marker", () =>
{
    var rendered = ReportRenderer.Render("| A | B |\n|---|:---:|\n| **bold** | `code` |\n\n1. first\n2. second\n\n- red\n- blue\n\n\0" + "0\0");
    Assert(rendered.Content.Contains("<table>") && rendered.Content.Contains("<strong>bold</strong>") && rendered.Content.Contains("<code>code</code>"), "Table inline syntax missing.");
    Assert(rendered.Content.Contains("<ol><li>first</li>") && rendered.Content.Contains("<ul><li>red</li>"), "Lists missing.");
    Assert(!rendered.Content.Contains('\0'), "Internal marker accepted from input.");
    _ = ReportRenderer.Render("|---|---|\n\n```csharp\nConsole.WriteLine(\"x\");");
    return Task.CompletedTask;
});
await Case("document_version_date_and_offline_security_policy", () =>
{
    string document = ReportRenderer.RenderDocument("# Test\n## Details\ntext", "1.3.0", new DateOnly(2026, 10, 4));
    Assert(document.Contains("版本 1.3.0") && document.Contains("2026-10-04") && document.Contains("default-src 'none'"), "Document metadata or offline policy mismatch.");
    Assert(!document.Contains("__CONTENT__") && !document.Contains("<script"), "Template placeholders or scripts present.");
    return Task.CompletedTask;
});

string packageRoot = Path.Combine(fixtureRoot, "repository");
InitializeRepository(packageRoot);
IReadOnlyList<PackageResult>? packages = null;
await Case("package_allowlist_excludes_private_build_and_old_runtime", () =>
{
    packages = DeliveryPackage.Create(packageRoot, ProjectTool.ReadVersion(packageRoot));
    using var source = ZipFile.OpenRead(Path.Combine(packageRoot, "artifacts", packages[0].File));
    var entries = source.Entries.Select(x => x.FullName).ToArray();
    Assert(entries.Contains(".gitignore") && entries.Contains(".gitattributes") && entries.Contains("src/Example.cs") && entries.Contains("research/upstream-snapshot/original.ts"), "Public root/source/evidence files missing.");
    Assert(entries.Contains("research/managed-silk-provenance.json") && entries.Contains("research/legacy-codec-licenses/LICENSE.txt") && entries.Contains("tests/Golden/synthetic.silk"), "Codec provenance, old license snapshot or synthetic golden was not included.");
    Assert(entries.All(x => !x.Contains("secret", StringComparison.OrdinalIgnoreCase) && !x.StartsWith("scripts/", StringComparison.Ordinal) && !x.StartsWith("runtime/", StringComparison.Ordinal)), "Private, ignored or legacy paths leaked.");
    Assert(entries.All(x => !DeliveryPackage.IsResearchBinary(x) && !x.EndsWith("codec-smoke.mjs", StringComparison.Ordinal)), "Downloaded research runtimes or scripts leaked into source ZIP.");
    using var runtime = ZipFile.OpenRead(Path.Combine(packageRoot, "artifacts", packages[1].File));
    Assert(runtime.Entries.Any(x => x.FullName == "docs/evidence/public.json") && runtime.Entries.Any(x => x.FullName == "weixin.exe"), "Runtime docs or executable missing.");
    Assert(runtime.Entries.Any(x => x.FullName == "docs/SilkCodec.NET-LICENSE.txt") && runtime.Entries.Any(x => x.FullName == "docs/SilkCodec.NET-Greepar-LICENSE.txt"), "Both managed codec license candidates must accompany the runtime.");
    Assert(runtime.Entries.All(x => !DeliveryPackage.IsLegacyRuntimePath(x.FullName)), "Runtime contains legacy executable files.");
    return Task.CompletedTask;
});
await Case("package_rejects_stale_node_without_replacing_archive", () =>
{
    string source = Path.Combine(packageRoot, "artifacts", "WeixinCSharp-1.3.0-source.zip");
    byte[] before = SHA256.HashData(File.ReadAllBytes(source));
    Write(packageRoot, "artifacts/win-x64-1.3.0/runtime/voice/node.exe", "not a real executable");
    Throws<InvalidDataException>(() => DeliveryPackage.Create(packageRoot, "1.3.0"));
    Assert(SHA256.HashData(File.ReadAllBytes(source)).AsSpan().SequenceEqual(before), "Rejected manifest changed the existing archive.");
    return Task.CompletedTask;
});
await Case("private_path_filter_catches_traversal_environment_and_qr", () =>
{
    foreach (string path in new[] { "../escape", "C:/Windows/absolute.txt", "bin/build.dll", "tests/obj/a.json", "x/.env", "x/.env.local", "received-bot/secret.mp4", "x/bind.dpapi", "x/private.login-qr.png", "x/private.pem", "artifacts/a.txt" })
        Assert(DeliveryPackage.IsPrivateOrBuildPath(path), "Unsafe path accepted.");
    return Task.CompletedTask;
});
await Case("package_rejects_non_csharp_application_source", () =>
{
    string nonCSharpRoot = Path.Combine(fixtureRoot, "non-csharp-repository");
    InitializeRepository(nonCSharpRoot);
    Write(nonCSharpRoot, "tests/legacy-interop.mjs", "// must not be executed or published as an application test");
    Throws<InvalidDataException>(() => DeliveryPackage.Create(nonCSharpRoot, "1.3.0"));
    Assert(!File.Exists(Path.Combine(nonCSharpRoot, "artifacts", "WeixinCSharp-1.3.0-source.zip")), "Rejected source created a partial delivery archive.");
    return Task.CompletedTask;
});
await Case("audit_clean_packages_metadata_only", async () =>
{
    var audit = await ArchiveAuditor.AuditAsync(packageRoot, "1.3.0", ["synthetic-secret-never-present"]);
    Assert(audit.Packages.Count == 2 && !audit.ContainsCredentialsOrAccountIds && audit.Packages.All(x => x.TextEntriesChecked > 0), "Clean audit summary mismatch.");
});
await Case("audit_detects_unicode_escaped_secret_without_echoing_value", async () =>
{
    string zipPath = Path.Combine(packageRoot, "artifacts", "WeixinCSharp-1.3.0-source.zip");
    byte[] original = File.ReadAllBytes(zipPath);
    try
    {
        Append(zipPath, "docs/escaped.json", Encoding.UTF8.GetBytes("{\"test\":\"\\u0073ynthetic-secret-token\"}"));
        var error = await ThrowsAsync<InvalidDataException>(() => ArchiveAuditor.AuditAsync(packageRoot, "1.3.0", ["synthetic-secret-token"]));
        Assert(!error.Message.Contains("synthetic-secret-token"), "Secret echoed in error.");
    }
    finally { File.WriteAllBytes(zipPath, original); }
});
await Case("audit_detects_matching_private_media_bytes", async () =>
{
    string zipPath = Path.Combine(packageRoot, "artifacts", "WeixinCSharp-1.3.0-source.zip");
    byte[] original = File.ReadAllBytes(zipPath);
    string privateMedia = Path.Combine(fixtureRoot, "synthetic-private-media");
    Directory.CreateDirectory(privateMedia);
    byte[] bytes = [0, 10, 50, 77, 201, 34, 71];
    File.WriteAllBytes(Path.Combine(privateMedia, "synthetic.dat"), bytes);
    try
    {
        Append(zipPath, "docs/renamed.dat", bytes);
        await ThrowsAsync<InvalidDataException>(() => ArchiveAuditor.AuditAsync(packageRoot, "1.3.0", [], privateMedia));
    }
    finally { File.WriteAllBytes(zipPath, original); }
});
await Case("audit_rejects_private_and_duplicate_zip_entries", async () =>
{
    string zipPath = Path.Combine(packageRoot, "artifacts", "WeixinCSharp-1.3.0-source.zip");
    byte[] original = File.ReadAllBytes(zipPath);
    try
    {
        Append(zipPath, "artifacts/hidden.dpapi", [1]);
        await ThrowsAsync<InvalidDataException>(() => ArchiveAuditor.AuditAsync(packageRoot, "1.3.0", []));
        File.WriteAllBytes(zipPath, original);
        Append(zipPath, "README.md", [1]);
        await ThrowsAsync<InvalidDataException>(() => ArchiveAuditor.AuditAsync(packageRoot, "1.3.0", []));
    }
    finally { File.WriteAllBytes(zipPath, original); }
});

var summary = new { formatVersion = 1, scope = "offline-csharp-project-tools", startedUtc = started, completedUtc = DateTimeOffset.UtcNow,
    realWeChatAccountUsed = false, total = results.Count, passed = results.Count - failures, failed = failures, cases = results };
if (output is not null) await File.WriteAllTextAsync(output, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
Console.WriteLine($"Project tools tests: {results.Count - failures}/{results.Count} passed.");
return failures == 0 ? 0 : 1;

async Task Case(string name, Func<Task> run)
{
    var clock = Stopwatch.StartNew();
    try { await run(); results.Add(new { name, passed = true, durationMilliseconds = clock.ElapsedMilliseconds }); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; results.Add(new { name, passed = false, durationMilliseconds = clock.ElapsedMilliseconds, errorType = error.GetType().Name }); Console.WriteLine("FAIL " + name + " (" + error.GetType().Name + ")"); }
}
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected rejection did not occur."); }
static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T error) { return error; } throw new InvalidOperationException("Expected rejection did not occur."); }
static void Write(string root, string relative, string text) { string file = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, text, new UTF8Encoding(false)); }
static void Append(string archive, string name, byte[] bytes) { using var zip = ZipFile.Open(archive, ZipArchiveMode.Update); using var entry = zip.CreateEntry(name).Open(); entry.Write(bytes); }
static void InitializeRepository(string root)
{
    foreach (string file in new[] { "README.md", "LICENSE", "THIRD-PARTY-NOTICES.md", "global.json", "WeixinAssistant.slnx", ".gitignore", ".gitattributes", "research/official-evidence.md", "research/github-evidence.md", "research/provenance.json" }) Write(root, file, "public synthetic fixture");
    Write(root, "Directory.Build.props", "<Project><PropertyGroup><Version>1.3.0</Version></PropertyGroup></Project>");
    Write(root, "src/Example.cs", "// synthetic source fixture");
    Write(root, "src/bin/secret.dll", "synthetic build output");
    Write(root, "src/obj/secret.json", "synthetic build output");
    Write(root, "tests/private.dpapi", "synthetic ignored state");
    Write(root, "docs/REPORT.html", "<!doctype html><p>synthetic report</p>");
    Write(root, "docs/evidence/public.json", "{\"synthetic\":true}");
    Write(root, "docs/SilkCodec.NET-LICENSE.txt", "synthetic managed decoder license");
    Write(root, "docs/SilkCodec.NET-Greepar-LICENSE.txt", "synthetic full encoder license");
    Write(root, "research/managed-silk-provenance.json", "{\"synthetic\":true}");
    Write(root, "tests/Golden/synthetic.silk", "synthetic codec golden vector");
    Write(root, "docs/.env", "synthetic ignored secret");
    Write(root, "docs/test.login-qr.png", "synthetic ignored QR");
    Write(root, "research/upstream-snapshot/original.ts", "// immutable upstream evidence");
    Write(root, "research/runtime-dependencies/node-runtime.zip", "synthetic downloaded runtime archive, never executed");
    Write(root, "research/runtime-dependencies/codec-smoke.mjs", "// historical executable research script");
    Write(root, "research/references/secret.txt", "synthetic ignored reference");
    Write(root, "research/legacy-codec-licenses/LICENSE.txt", "synthetic legacy license snapshot");
    Write(root, "scripts/old.py", "# legacy executable is outside the source allowlist");
    Write(root, "runtime/voice/node.exe", "legacy runtime is outside source allowlist");
    Write(root, "artifacts/win-x64-1.3.0/weixin.exe", "synthetic .NET app host, never executed");
    Write(root, "artifacts/win-x64-1.3.0/weixin.dll", "synthetic managed assembly, never executed");
}
