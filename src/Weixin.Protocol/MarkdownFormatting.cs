using System.Text;

namespace Weixin.Protocol;

public static class MarkdownFormatting
{
    // Tencent channel capabilities advertise textChunkLimit: 4000. This is the
    // JavaScript UTF-16 string length used by the client, not a server byte limit.
    public const int DefaultTextChunkLimit = 4000;

    /// <summary>Apply Tencent's supported-markup filter; this does not enable native Markdown rendering.</summary>
    public static string ConvertToPlainText(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var filter = new StreamingMarkdownFilter();
        return filter.Feed(markdown) + filter.Flush();
    }

    /// <summary>Losslessly split by UTF-16 length, keeping each surrogate pair together.</summary>
    public static IReadOnlyList<string> ChunkText(string text, int maximumLength = DefaultTextChunkLimit)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maximumLength < 2) throw new ArgumentOutOfRangeException(nameof(maximumLength), "段长至少为 2，以容纳一个代理对。");
        var chunks = new List<string>();
        for (int start = 0; start < text.Length;)
        {
            int count = Math.Min(maximumLength, text.Length - start);
            if (start + count < text.Length && char.IsHighSurrogate(text[start + count - 1]) && char.IsLowSurrogate(text[start + count])) count--;
            chunks.Add(text.Substring(start, count));
            start += count;
        }
        return chunks;
    }
}

/// <summary>
/// C# port of Tencent's StreamingMarkdownFilter, pinned to commit
/// 24de5c9eb0dd5e595d7e2d090ed8a3f82870d42c/src/messaging/markdown-filter.ts.
/// Copyright (C) 2026 Tencent; MIT license (see THIRD-PARTY-NOTICES.md).
/// </summary>
public sealed class StreamingMarkdownFilter
{
    private string buffer = "";
    private bool fence;
    private bool startOfLine = true;
    private InlineState? inline;
    private sealed class InlineState(string type) { public string Type { get; } = type; public string Accumulated { get; set; } = ""; }

    public string Feed(string delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        buffer += delta;
        return Pump(false);
    }
    public string Flush() => Pump(true);

    private string Pump(bool eof)
    {
        var output = new StringBuilder();
        while (buffer.Length != 0)
        {
            int length = buffer.Length; bool sol = startOfLine, fenced = fence; var previousInline = inline;
            output.Append(fence ? PumpFence(eof) : inline is not null ? PumpInline() : startOfLine ? PumpStartOfLine(eof) : PumpBody(eof));
            if (buffer.Length == length && startOfLine == sol && fence == fenced && ReferenceEquals(inline, previousInline)) break;
        }
        if (eof && inline is not null)
        {
            output.Append(Marker(inline.Type)).Append(inline.Accumulated);
            inline = null;
        }
        return output.ToString();
    }

    private string PumpFence(bool eof)
    {
        if (startOfLine)
        {
            if (buffer.Length < 3 && !eof) return "";
            if (buffer.StartsWith("```", StringComparison.Ordinal))
            {
                int newline = buffer.IndexOf('\n', 3);
                if (newline >= 0)
                {
                    fence = false; string line = Consume(newline + 1); startOfLine = true; return line;
                }
                if (eof) { fence = false; return Consume(buffer.Length); }
                return "";
            }
            startOfLine = false;
        }
        int index = buffer.IndexOf('\n');
        if (index >= 0) { string chunk = Consume(index + 1); startOfLine = true; return chunk; }
        return Consume(buffer.Length);
    }

    private string PumpStartOfLine(bool eof)
    {
        var b = buffer;
        if (b[0] == '\n') return Consume(1);
        if (b[0] == '`')
        {
            if (b.Length < 3 && !eof) return "";
            if (b.StartsWith("```", StringComparison.Ordinal))
            {
                int newline = b.IndexOf('\n', 3);
                if (newline >= 0) { fence = true; string line = Consume(newline + 1); startOfLine = true; return line; }
                if (eof) return Consume(buffer.Length);
                return "";
            }
            startOfLine = false; return "";
        }
        if (b[0] == '>') { startOfLine = false; return ""; }
        if (b[0] == '#')
        {
            int n = 0; while (n < b.Length && b[n] == '#') n++;
            if (n == b.Length && !eof) return "";
            if (n is >= 5 and <= 6 && n < b.Length && b[n] == ' ') buffer = b[(n + 1)..];
            startOfLine = false; return "";
        }
        if (b[0] is ' ' or '\t')
        {
            if (b.All(c => c is ' ' or '\t') && !eof) return "";
            startOfLine = false; return "";
        }
        if (b[0] is '-' or '*' or '_')
        {
            char c = b[0]; int j = 0; while (j < b.Length && (b[j] == c || b[j] == ' ')) j++;
            if (j == b.Length && !eof) return "";
            if (j == b.Length || b[j] == '\n')
            {
                int count = 0; for (int k = 0; k < j; k++) if (b[k] == c) count++;
                if (count >= 3)
                {
                    if (j < b.Length) { string line = Consume(j + 1); startOfLine = true; return line; }
                    return Consume(buffer.Length);
                }
            }
        }
        startOfLine = false; return "";
    }

    private string PumpBody(bool eof)
    {
        int i = 0;
        while (i < buffer.Length)
        {
            char c = buffer[i];
            if (c == '\n') { string line = Consume(i + 1); startOfLine = true; return line; }
            if (c == '!' && i + 1 < buffer.Length && buffer[i + 1] == '[')
                return EnterInline(i, 2, "image");
            if (c == '~') { i++; continue; }
            if (c is '*' or '_')
            {
                if (i + 2 < buffer.Length && buffer[i + 1] == c && buffer[i + 2] == c)
                    return EnterInline(i, 3, c == '*' ? "bold3" : "ubold3");
                if (i + 1 < buffer.Length && buffer[i + 1] == c) { i += 2; continue; }
                if (i + 1 < buffer.Length && buffer[i + 1] is not (' ' or '\n'))
                    return EnterInline(i, 1, c == '*' ? "italic" : "uitalic");
            }
            i++;
        }
        int hold = 0;
        if (!eof)
        {
            if (buffer.EndsWith("**", StringComparison.Ordinal) || buffer.EndsWith("__", StringComparison.Ordinal)) hold = 2;
            else if (buffer.EndsWith('*') || buffer.EndsWith('_') || buffer.EndsWith('!')) hold = 1;
        }
        return Consume(buffer.Length - hold);
    }

    private string PumpInline()
    {
        if (inline is null) return "";
        inline.Accumulated += buffer; buffer = "";
        string content = inline.Accumulated, type = inline.Type;
        if (type is "bold3" or "ubold3")
        {
            string marker = Marker(type); int index = content.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0) return "";
            buffer = content[(index + 3)..]; inline = null;
            var body = content[..index]; return ContainsCjk(body) ? body : marker + body + marker;
        }
        if (type is "italic" or "uitalic")
        {
            char marker = type == "italic" ? '*' : '_';
            for (int j = 0; j < content.Length; j++)
            {
                if (content[j] == '\n')
                {
                    string line = marker + content[..(j + 1)]; buffer = content[(j + 1)..]; inline = null; startOfLine = true; return line;
                }
                if (content[j] == marker)
                {
                    if (j + 1 < content.Length && content[j + 1] == marker) { j++; continue; }
                    var body = content[..j]; buffer = content[(j + 1)..]; inline = null;
                    return ContainsCjk(body) ? body : marker + body + marker;
                }
            }
            return "";
        }
        if (type == "image")
        {
            int closeBracket = content.IndexOf(']');
            if (closeBracket < 0 || closeBracket + 1 >= content.Length) return "";
            if (content[closeBracket + 1] != '(')
            {
                string value = "![" + content[..(closeBracket + 1)]; buffer = content[(closeBracket + 1)..]; inline = null; return value;
            }
            int closeParenthesis = content.IndexOf(')', closeBracket + 2);
            if (closeParenthesis >= 0) { buffer = content[(closeParenthesis + 1)..]; inline = null; }
        }
        return "";
    }

    private string EnterInline(int index, int markerLength, string type)
    {
        string output = buffer[..index]; buffer = buffer[(index + markerLength)..]; inline = new(type); return output;
    }
    private string Consume(int count) { string output = buffer[..count]; buffer = buffer[count..]; return output; }
    private static string Marker(string type) => type switch { "image" => "![", "bold3" => "***", "ubold3" => "___", "italic" => "*", "uitalic" => "_", _ => "" };
    private static bool ContainsCjk(string text) => text.Any(c => c is >= '\u2E80' and <= '\u9FFF' or >= '\uAC00' and <= '\uD7AF' or >= '\uF900' and <= '\uFAFF');
}
