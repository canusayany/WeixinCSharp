using System.Net;
using System.Text.RegularExpressions;

namespace Weixin.Tools;

/// <summary>A small offline Markdown renderer. Raw HTML is always text; links use safe schemes.</summary>
public static class ReportRenderer
{
    private static string Encode(string text) => WebUtility.HtmlEncode(text);
    public static (string Content, string Navigation) Render(string markdown)
    {
        string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var blocks = new List<string>();
        var navigation = new List<string>();
        int index = 0, section = 0;
        while (index < lines.Length)
        {
            string line = lines[index];
            if (string.IsNullOrWhiteSpace(line)) { index++; continue; }
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                string language = line[3..].Trim();
                var code = new List<string>();
                index++;
                while (index < lines.Length && !lines[index].StartsWith("```", StringComparison.Ordinal)) code.Add(lines[index++]);
                blocks.Add($"<pre data-language=\"{Encode(language)}\"><code>{Encode(string.Join('\n', code))}</code></pre>");
                if (index < lines.Length) index++;
                continue;
            }
            var heading = Regex.Match(line, @"^(#{1,6}) (.+)$", RegexOptions.CultureInvariant);
            if (heading.Success)
            {
                int level = heading.Groups[1].Length;
                string anchor = "";
                if (level == 2)
                {
                    section++;
                    anchor = $" id=\"section-{section}\"";
                    navigation.Add($"<a href=\"#section-{section}\">{Inline(heading.Groups[2].Value)}</a>");
                }
                blocks.Add($"<h{level}{anchor}>{Inline(heading.Groups[2].Value)}</h{level}>");
                index++;
                continue;
            }
            if (line.StartsWith('|'))
            {
                var rows = new List<string[]>();
                while (index < lines.Length && lines[index].StartsWith('|'))
                {
                    string[] cells = lines[index++].Trim().Trim('|').Split('|').Select(x => x.Trim()).ToArray();
                    if (!cells.All(x => Regex.IsMatch(x, @"^[-: ]+$", RegexOptions.CultureInvariant))) rows.Add(cells);
                }
                if (rows.Count > 0)
                {
                    string head = string.Concat(rows[0].Select(x => "<th>" + Inline(x) + "</th>"));
                    string body = string.Concat(rows.Skip(1).Select(row => "<tr>" + string.Concat(row.Select(x => "<td>" + Inline(x) + "</td>")) + "</tr>"));
                    blocks.Add($"<div class=\"table\"><table><thead><tr>{head}</tr></thead><tbody>{body}</tbody></table></div>");
                }
                continue;
            }
            bool ordered = Regex.IsMatch(line, @"^\d+\. ", RegexOptions.CultureInvariant);
            bool unordered = Regex.IsMatch(line, @"^[-*+] ", RegexOptions.CultureInvariant);
            if (ordered || unordered)
            {
                string pattern = ordered ? @"^\d+\. " : @"^[-*+] ";
                var items = new List<string>();
                while (index < lines.Length && Regex.IsMatch(lines[index], pattern, RegexOptions.CultureInvariant))
                    items.Add("<li>" + Inline(Regex.Replace(lines[index++], pattern, "", RegexOptions.CultureInvariant)) + "</li>");
                string tag = ordered ? "ol" : "ul";
                blocks.Add($"<{tag}>{string.Concat(items)}</{tag}>");
                continue;
            }
            var paragraph = new List<string> { lines[index++] };
            while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]) && !IsBlock(lines[index])) paragraph.Add(lines[index++]);
            // The first paragraph line was consumed unconditionally, including malformed '#literal'.
            blocks.Add("<p>" + Inline(string.Join(' ', paragraph)) + "</p>");
        }
        return (string.Join('\n', blocks), string.Join('\n', navigation));
    }

    private static bool IsBlock(string line) => line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith('|')
        || Regex.IsMatch(line, @"^(#{1,6}) .+|^\d+\. |^[-*+] ", RegexOptions.CultureInvariant);

    public static string Inline(string text)
    {
        var tokens = new List<string>();
        string Stash(string value) { tokens.Add(value); return "\0" + (tokens.Count - 1) + "\0"; }
        // Callers cannot forge the internal token marker.
        text = text.Replace('\0', '\uFFFD');
        text = Regex.Replace(text, @"`([^`]+)`|\[([^\]]+)\]\(([^)]+)\)", m =>
        {
            if (m.Groups[1].Success) return Stash("<code>" + Encode(m.Groups[1].Value) + "</code>");
            string label = Inline(m.Groups[2].Value), url = m.Groups[3].Value;
            return Stash(IsSafeUrl(url) ? $"<a href=\"{Encode(url)}\">{label}</a>" : label);
        }, RegexOptions.CultureInvariant);
        text = Encode(text);
        text = Regex.Replace(text, @"\*\*([^*]+)\*\*", "<strong>$1</strong>", RegexOptions.CultureInvariant);
        return Regex.Replace(text, "\0(\\d+)\0", m => tokens[int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)], RegexOptions.CultureInvariant);
    }

    private static bool IsSafeUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(char.IsControl) || value.Contains('\\') || value.StartsWith("//", StringComparison.Ordinal)) return false;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return uri.Scheme is "https" or "http" && !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
        // Any colon would introduce a scheme or a Windows absolute path in a local link.
        return !value.Contains(':');
    }

    public static string RenderDocument(string markdown, string version, DateOnly? date = null)
    {
        var (content, navigation) = Render(markdown);
        DateOnly reportDate = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).DateTime);
        return Template.Replace("__NAV__", navigation, StringComparison.Ordinal)
            .Replace("__CONTENT__", content, StringComparison.Ordinal)
            .Replace("__VERSION__", Encode(version), StringComparison.Ordinal)
            .Replace("__DATE__", reportDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private const string Template = """
<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
<title>微信助理协议研究与 C# 实现报告</title>
<style>
:root{color-scheme:light;--ink:#18302e;--muted:#637570;--green:#087f5b;--border:#dce6df}
*{box-sizing:border-box}body{margin:0;background:#f3f6f3;color:var(--ink);font:16px/1.85 'Segoe UI','Microsoft YaHei',sans-serif}
.hero{background:#163c31;color:#fff;padding:34px max(28px,calc((100vw - 1280px)/2));border-bottom:5px solid #71d598}
.eyebrow{font-size:12px;letter-spacing:.16em;text-transform:uppercase;color:#a2d2b6}.hero p{margin:10px 0 0;font-size:16px;color:#d4e7da}
.hero h1{margin:8px 0 0;font-size:32px;line-height:1.35}.layout{max-width:1280px;margin:auto;display:grid;grid-template-columns:238px minmax(0,1fr);gap:28px;padding:28px}
aside{position:sticky;top:24px;align-self:start;font-size:13px}aside p{font-weight:700;margin:0 0 12px;color:var(--muted)}aside a{display:block;text-decoration:none;color:var(--muted);padding:6px 10px;border-left:2px solid var(--border);line-height:1.6}aside a:hover{border-color:var(--green);color:var(--green);background:#e7f0e8}
main{background:white;border:1px solid var(--border);border-radius:12px;padding:34px 42px;box-shadow:0 5px 24px #12301b06;min-width:0}
main>h1{display:none}main>p:first-of-type{margin-top:0;color:var(--muted);font-size:13px;border-bottom:1px solid var(--border);padding-bottom:22px}
h2{font-size:24px;line-height:1.5;margin:44px 0 16px;padding-top:12px;border-top:1px solid var(--border);scroll-margin-top:24px}h2:first-of-type{border-top:0;margin-top:20px}h3{font-size:19px;margin:28px 0 12px}
p{margin:14px 0}a{color:var(--green);text-underline-offset:3px;overflow-wrap:anywhere}strong{font-weight:650}code{font-family:Consolas,monospace;font-size:.88em;background:#edf3ef;padding:2px 5px;border-radius:4px;overflow-wrap:anywhere}
pre{position:relative;background:#163029;color:#e7f5ec;border-radius:8px;padding:28px 18px 18px;overflow:auto;font:13px/1.75 Consolas,monospace;max-width:100%}pre::before{content:attr(data-language);position:absolute;top:5px;right:14px;font:10px 'Segoe UI',sans-serif;color:#87b299;text-transform:uppercase}pre code{background:none;padding:0;border-radius:0;overflow-wrap:normal;font-size:inherit}
.table{overflow-x:auto;margin:20px 0}table{border-collapse:collapse;width:100%;font-size:13px;line-height:1.75}th{background:#eaf1ec;color:#244e35;text-align:left;font-weight:650}td,th{padding:11px 12px;border:1px solid var(--border);vertical-align:top;overflow-wrap:anywhere}tbody tr:nth-child(even){background:#f8faf8}ol,ul{padding-left:25px}li{padding:4px 0}
footer{max-width:1280px;margin:0 auto;padding:0 28px 32px;color:var(--muted);font-size:12px;text-align:right}
@media(max-width:900px){.layout{grid-template-columns:1fr;padding:18px}.hero{padding:25px}.hero h1{font-size:26px}aside{position:static;display:none}main{padding:24px}h2{font-size:21px}}
@media print{body{background:white;font-size:10pt;line-height:1.7}.hero{background:white;color:#17382d;padding:0 0 14px;border-bottom:2px solid #17382d}.hero .eyebrow,.hero p{color:#53735e}.hero h1{font-size:22pt}.layout{display:block;padding:0;margin:0}aside{display:none}main{padding:0;border:0;box-shadow:none}h2{font-size:15pt;margin:24px 0 10px;break-after:avoid}h3{break-after:avoid}table{font-size:9pt}pre{white-space:pre-wrap;word-break:break-word;background:#f1f4f1;color:#17382d;break-inside:avoid}tr{break-inside:avoid}.table{overflow:visible}footer{padding:12px 0}@page{size:A4;margin:18mm}}
</style></head><body><header class="hero"><div class="eyebrow">Protocol research / C# implementation</div><h1>微信助理协议研究<br>与 C# 实现报告</h1><p>官方 iLink 通道 · WorkBuddy 机制对照 · 账号风险与验证边界</p></header>
<div class="layout"><aside><p>报告目录</p>__NAV__</aside><main>__CONTENT__</main></div><footer>本地离线报告 · __DATE__ · 版本 __VERSION__</footer></body></html>
""";
}
