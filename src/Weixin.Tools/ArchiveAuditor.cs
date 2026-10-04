using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Weixin.Tools;

public sealed record PackageAudit(string File, int EntryCount, int TextEntriesChecked, string Sha256,
    bool PrivateEntryFound = false, bool KnownPrivateValueFound = false, bool PrivateMediaFound = false);
public sealed record DeliveryAudit(int FormatVersion, DateTimeOffset CompletedUtc, bool PrivateStateChecked,
    int PrivateMediaChecked, bool ContainsCredentialsOrAccountIds, IReadOnlyList<PackageAudit> Packages);

/// <summary>Checks ZIP contents against supplied secrets and private media without emitting those inputs.</summary>
public static class ArchiveAuditor
{
    public static async Task<DeliveryAudit> AuditAsync(string root, string version, IReadOnlyList<string> secrets,
        string? privateMediaDirectory = null, bool privateStateChecked = false)
    {
        var mediaHashes = new HashSet<string>(StringComparer.Ordinal);
        if (privateMediaDirectory is not null)
            foreach (string path in Directory.EnumerateFiles(privateMediaDirectory, "*", SearchOption.AllDirectories))
            {
                await using var input = File.OpenRead(path);
                mediaHashes.Add(Convert.ToHexString(await SHA256.HashDataAsync(input)));
            }
        var audits = new List<PackageAudit>();
        foreach (string suffix in new[] { "source", "win-x64" })
        {
            string name = "WeixinCSharp-" + version + "-" + suffix + ".zip";
            string path = Path.Combine(root, "artifacts", name);
            using var input = File.OpenRead(path);
            string sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(input));
            input.Position = 0;
            using var zip = new ZipArchive(input, ZipArchiveMode.Read);
            int textCount = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                if (!names.Add(entry.FullName) || DeliveryPackage.IsPrivateOrBuildPath(entry.FullName)) throw new InvalidDataException("Private, unsafe or duplicate ZIP entry found.");
                if (DeliveryPackage.IsResearchBinary(entry.FullName)) throw new InvalidDataException("Research ZIP entry contains a downloaded binary/runtime archive.");
                if (suffix == "win-x64" && DeliveryPackage.IsLegacyRuntimePath(entry.FullName)) throw new InvalidDataException("Runtime ZIP contains a non-.NET runtime entry.");
                if (entry.Length == 0) continue;
                if (mediaHashes.Count > 0)
                {
                    await using var bytes = entry.Open();
                    string entryHash = Convert.ToHexString(await SHA256.HashDataAsync(bytes));
                    if (mediaHashes.Contains(entryHash)) throw new InvalidDataException("Supplied private media found in ZIP.");
                }
                if (Regex.IsMatch(entry.FullName, @"(?i)\.(cs|csproj|slnx|props|json|md|html|txt|ps1|py|mjs|cjs|js|ts|c|h|cpp|yml|yaml)$", RegexOptions.CultureInvariant)
                    || Path.GetFileName(entry.FullName) is "LICENSE" or ".gitignore" or ".gitattributes")
                {
                    using var reader = new StreamReader(entry.Open());
                    string content = await reader.ReadToEndAsync();
                    string unicodeDecoded = Regex.Replace(content, @"\\u([0-9a-fA-F]{4})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString(), RegexOptions.CultureInvariant);
                    string jsonDecoded = unicodeDecoded.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\/", "/", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
                    foreach (string secret in secrets)
                        if (!string.IsNullOrEmpty(secret) && (content.Contains(secret, StringComparison.Ordinal) || unicodeDecoded.Contains(secret, StringComparison.Ordinal) || jsonDecoded.Contains(secret, StringComparison.Ordinal)))
                            throw new InvalidDataException("Supplied private credential or account identifier found in ZIP text.");
                    textCount++;
                }
            }
            audits.Add(new(name, zip.Entries.Count, textCount, sha));
        }
        return new(1, DateTimeOffset.UtcNow, privateStateChecked, mediaHashes.Count, false, audits);
    }
}
