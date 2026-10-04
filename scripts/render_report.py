"""Render the repository report as standalone offline HTML; standard library only."""
from pathlib import Path
import html
import re

ROOT = Path(__file__).resolve().parent.parent


def inline(text):
    tokens = []

    def stash(value):
        tokens.append(value)
        return f"\x00{len(tokens) - 1}\x00"

    text = re.sub(r"`([^`]+)`", lambda m: stash("<code>" + html.escape(m[1]) + "</code>"), text)
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", lambda m: stash(
        f'<a href="{html.escape(m[2], quote=True)}">{html.escape(m[1])}</a>'), text)
    text = html.escape(text)
    text = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", text)
    return re.sub(r"\x00(\d+)\x00", lambda m: tokens[int(m[1])], text)


def render(markdown):
    lines = markdown.splitlines()
    output, navigation = [], []
    i, section = 0, 0
    while i < len(lines):
        line = lines[i]
        if not line.strip():
            i += 1
            continue
        if line.startswith("```"):
            language = line[3:].strip()
            code = []
            i += 1
            while i < len(lines) and not lines[i].startswith("```"):
                code.append(lines[i])
                i += 1
            output.append(f'<pre data-language="{html.escape(language)}"><code>{html.escape(chr(10).join(code))}</code></pre>')
            i += 1
            continue
        heading = re.match(r"^(#{1,6}) (.+)$", line)
        if heading:
            level = len(heading[1])
            anchor = ""
            if level == 2:
                section += 1
                anchor = f' id="section-{section}"'
                navigation.append(f'<a href="#section-{section}">{inline(heading[2])}</a>')
            output.append(f"<h{level}{anchor}>{inline(heading[2])}</h{level}>")
            i += 1
            continue
        if line.startswith("|"):
            rows = []
            while i < len(lines) and lines[i].startswith("|"):
                cells = [x.strip() for x in lines[i].strip().strip("|").split("|")]
                if not all(re.fullmatch(r"[-: ]+", x) for x in cells):
                    rows.append(cells)
                i += 1
            head = "".join("<th>" + inline(x) + "</th>" for x in rows[0])
            body = "".join("<tr>" + "".join("<td>" + inline(x) + "</td>" for x in row) + "</tr>" for row in rows[1:])
            output.append(f'<div class="table"><table><thead><tr>{head}</tr></thead><tbody>{body}</tbody></table></div>')
            continue
        if re.match(r"^\d+\. ", line):
            items = []
            while i < len(lines) and re.match(r"^\d+\. ", lines[i]):
                items.append("<li>" + inline(re.sub(r"^\d+\. ", "", lines[i])) + "</li>")
                i += 1
            output.append("<ol>" + "".join(items) + "</ol>")
            continue
        paragraph = []
        while i < len(lines) and lines[i].strip() and not lines[i].startswith(("#", "|", "```")):
            paragraph.append(lines[i])
            i += 1
        # Unrecognized block-looking text must still consume a line. Otherwise
        # e.g. '#literal' leaves i unchanged and grows empty paragraphs forever.
        if not paragraph:
            paragraph.append(lines[i])
            i += 1
        output.append("<p>" + inline(" ".join(paragraph)) + "</p>")
    return "\n".join(output), "\n".join(navigation)


CONTENT, NAV = render((ROOT / "docs/REPORT.md").read_text(encoding="utf-8"))
TEMPLATE = """<!doctype html>
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
.table{overflow-x:auto;margin:20px 0}table{border-collapse:collapse;width:100%;font-size:13px;line-height:1.75}th{background:#eaf1ec;color:#244e35;text-align:left;font-weight:650}td,th{padding:11px 12px;border:1px solid var(--border);vertical-align:top;overflow-wrap:anywhere}tbody tr:nth-child(even){background:#f8faf8}ol{padding-left:25px}li{padding:4px 0}
footer{max-width:1280px;margin:0 auto;padding:0 28px 32px;color:var(--muted);font-size:12px;text-align:right}
@media(max-width:900px){.layout{grid-template-columns:1fr;padding:18px}.hero{padding:25px}.hero h1{font-size:26px}aside{position:static;display:none}main{padding:24px}h2{font-size:21px}}
@media print{body{background:white;font-size:10pt;line-height:1.7}.hero{background:white;color:#17382d;padding:0 0 14px;border-bottom:2px solid #17382d}.hero .eyebrow,.hero p{color:#53735e}.hero h1{font-size:22pt}.layout{display:block;padding:0;margin:0}aside{display:none}main{padding:0;border:0;box-shadow:none}h2{font-size:15pt;margin:24px 0 10px;break-after:avoid}h3{break-after:avoid}table{font-size:9pt}pre{white-space:pre-wrap;word-break:break-word;background:#f1f4f1;color:#17382d;break-inside:avoid}tr{break-inside:avoid}.table{overflow:visible}footer{padding:12px 0}@page{size:A4;margin:18mm}}
</style></head><body><header class="hero"><div class="eyebrow">Protocol research / C# implementation</div><h1>微信助理协议研究<br>与 C# 实现报告</h1><p>官方 iLink 通道 · WorkBuddy 机制对照 · 账号风险与验证边界</p></header>
<div class="layout"><aside><p>报告目录</p>__NAV__</aside><main>__CONTENT__</main></div><footer>本地离线报告 · 2026-10-04 · 版本 1.2.1</footer></body></html>"""
(ROOT / "docs/REPORT.html").write_text(TEMPLATE.replace("__NAV__", NAV).replace("__CONTENT__", CONTENT), encoding="utf-8")
print("Rendered docs/REPORT.html")
