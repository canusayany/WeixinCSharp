using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Weixin.Tools;

public sealed record PackageResult(string File, long Bytes, string Sha256, int EntryCount);

/// <summary>Explicit public-source allowlist and separate versioned .NET publish output.</summary>
public static class DeliveryPackage
{
    private static readonly string[] RootFiles = ["README.md", "LICENSE", "THIRD-PARTY-NOTICES.md", "Directory.Build.props", "global.json", "WeixinAssistant.slnx", ".gitignore", ".gitattributes"];
    private static readonly string[] PublicDirectories = ["src", "tests", "docs", ".github", "research/upstream-snapshot", "research/typing-lifecycle-snapshot", "research/runtime-dependencies", "research/legacy-codec-licenses"];
    private static readonly string[] ResearchFiles = ["research/official-evidence.md", "research/github-evidence.md", "research/provenance.json"];

    public static bool IsPrivateOrBuildPath(string relativePath)
    {
        string path = relativePath.Replace('\\', '/');
        if (path.StartsWith('/') || path.Contains(':') || path.Any(char.IsControl) || path.Split('/').Any(x => x is ".." or ".")) return true;
        return Regex.IsMatch(path, @"(?i)(^|/)(artifacts|references|received[^/]*|bin|obj|__pycache__|\.git|\.vs|\.idea)(/|$)|\.dpapi|(^|/)\.env(?:\.|$)|(^|/)(?:.*\.)?login-qr\.(html|png)$|\.(tmp|lock|pyc|user|log|pfx|pem|key)$", RegexOptions.CultureInvariant);
    }

    public static bool IsLegacyRuntimePath(string relativePath)
    {
        string path = relativePath.Replace('\\', '/');
        return path.StartsWith("runtime/", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(path, @"(?i)(^|/)(node(?:\.exe)?|python(?:\d+(?:\.\d+)*)?(?:\.exe)?)$|\.(wasm|mjs|cjs|js|py|ps1|cmd|bat|sh)$", RegexOptions.CultureInvariant);
    }

    public static bool IsResearchBinary(string relativePath) => relativePath.Replace('\\', '/')
        .StartsWith("research/", StringComparison.OrdinalIgnoreCase) &&
        Regex.IsMatch(relativePath, @"(?i)\.(zip|tgz|gz|7z|exe|dll|so|dylib|wasm)$", RegexOptions.CultureInvariant);

    public static IReadOnlyList<PackageResult> Create(string root, string version)
    {
        root = Path.GetFullPath(root);
        string artifacts = Path.Combine(root, "artifacts");
        string published = Path.Combine(artifacts, "win-x64-" + version);
        if (!File.Exists(Path.Combine(published, "weixin.exe"))) throw new FileNotFoundException("Versioned published .NET executable is missing.");
        if (!File.Exists(Path.Combine(root, "docs", "REPORT.html"))) throw new FileNotFoundException("Rendered report is missing.");
        Directory.CreateDirectory(artifacts);
        var source = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string relative in RootFiles.Concat(ResearchFiles)) AddRequired(source, root, relative);
        // Additional pinned codec provenance manifests live beside the original protocol manifest.
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "research"), "*provenance.json", SearchOption.TopDirectoryOnly))
            AddRequired(source, root, Path.GetRelativePath(root, path));
        foreach (string relativeDirectory in PublicDirectories)
        {
            string directory = Path.Combine(root, relativeDirectory);
            if (!Directory.Exists(directory)) continue;
            foreach (string path in PublicFiles(directory, root))
            {
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                // Research ships source/licensing text, never downloaded runtime archives.
                if (IsResearchBinary(relative) || relative.StartsWith("research/", StringComparison.Ordinal) && IsLegacyRuntimePath(relative)) continue;
                // Static original-source/licensing snapshots are evidence, never invoked by this tool.
                if ((relative.StartsWith("src/", StringComparison.Ordinal) || relative.StartsWith("tests/", StringComparison.Ordinal))
                    && (IsLegacyRuntimePath(relative) || Regex.IsMatch(relative, @"(?i)\.(c|h|cpp|hpp|ts|tsx|jsx)$", RegexOptions.CultureInvariant)))
                    throw new InvalidDataException("Application/test source still contains a legacy executable script.");
                source.Add(relative, path);
            }
        }
        var runtime = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(published, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(published, path).Replace('\\', '/');
            if (IsPrivateOrBuildPath(relative) || IsLegacyRuntimePath(relative)) throw new InvalidDataException("Versioned publish output contains a private or non-.NET runtime entry.");
            EnsureOrdinaryFile(path);
            runtime.Add(relative, path);
        }
        foreach (string relative in new[] { "README.md", "LICENSE", "THIRD-PARTY-NOTICES.md" }) AddRequired(runtime, root, relative);
        foreach (string path in PublicFiles(Path.Combine(root, "docs"), root)) runtime[Path.GetRelativePath(root, path).Replace('\\', '/')] = path;

        // Validate both manifests before replacing either existing archive. Stage every completed ZIP.
        string sourcePath = Path.Combine(artifacts, "WeixinCSharp-" + version + "-source.zip");
        string runtimePath = Path.Combine(artifacts, "WeixinCSharp-" + version + "-win-x64.zip");
        string sourceStage = sourcePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string runtimeStage = runtimePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteZip(sourceStage, source);
            WriteZip(runtimeStage, runtime);
            File.Move(sourceStage, sourcePath, true);
            File.Move(runtimeStage, runtimePath, true);
        }
        finally
        {
            if (File.Exists(sourceStage)) File.Delete(sourceStage);
            if (File.Exists(runtimeStage)) File.Delete(runtimeStage);
        }
        var packages = new[] { Describe(sourcePath, source.Count), Describe(runtimePath, runtime.Count) };
        File.WriteAllText(Path.Combine(artifacts, "SHA256SUMS.txt"), string.Join('\n', packages.Select(x => x.Sha256 + "  " + x.File)) + "\n", Encoding.ASCII);
        return packages;
    }

    private static IEnumerable<string> PublicFiles(string directory, string root)
    {
        EnsureOrdinaryFile(directory);
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (IsPrivateOrBuildPath(relative)) continue;
            EnsureOrdinaryFile(path);
            if (Directory.Exists(path))
            {
                foreach (string child in PublicFiles(path, root)) yield return child;
            }
            else yield return path;
        }
    }

    private static void EnsureOrdinaryFile(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Symbolic links/reparse points are not packaged.");
    }

    private static void AddRequired(IDictionary<string, string> entries, string root, string relative)
    {
        string path = Path.Combine(root, relative);
        if (!File.Exists(path)) throw new FileNotFoundException("A required public packaging file is missing.");
        EnsureOrdinaryFile(path);
        entries[relative.Replace('\\', '/')] = path;
    }

    private static void WriteZip(string path, SortedDictionary<string, string> entries)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);
        foreach (var pair in entries) archive.CreateEntryFromFile(pair.Value, pair.Key, CompressionLevel.Optimal);
    }

    private static PackageResult Describe(string path, int entries)
    {
        using var input = File.OpenRead(path);
        return new(Path.GetFileName(path), input.Length, Convert.ToHexStringLower(SHA256.HashData(input)), entries);
    }
}
