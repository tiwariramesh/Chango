using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;

namespace Chango.Conversion.Documents;

/// <summary>
/// A Word document (.docx) as Markdown, read directly with the Open XML SDK (no Word needed): headings, bulleted and
/// numbered lists (nested), bold / italic / strikethrough / code, links, tables, quotes, code blocks and pictures
/// (embedded as data URIs, so the one .md file is complete). Older formats are turned into DOCX by Word first.
/// </summary>
public static class DocxToMarkdown
{
    public static string Convert(string docx)
    {
        using var doc = WordprocessingDocument.Open(docx, false);
        var main = doc.MainDocumentPart ?? throw new InvalidDataException("The document has no body.");
        var writer = new Writer(main);
        foreach (var element in main.Document.Body?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
        {
            switch (element)
            {
                case Paragraph p: writer.Paragraph(p); break;
                case Table t: writer.Table(t); break;
                case SdtBlock sdt: // content controls (e.g. a table of contents) hold ordinary paragraphs
                    foreach (var p in sdt.Descendants<Paragraph>()) writer.Paragraph(p);
                    break;
            }
        }
        return writer.Finish();
    }

    private sealed class Writer(MainDocumentPart main)
    {
        private readonly StringBuilder _md = new();
        private enum Block { None, Paragraph, List, Code }
        private Block _last = Block.None;
        private readonly Dictionary<(int NumId, int Level), int> _counters = [];
        private int _listNumId;

        public string Finish()
        {
            CloseCode();
            return _md.ToString().TrimEnd() + Environment.NewLine;
        }

        public void Paragraph(Paragraph p)
        {
            string style = StyleName(p);
            string text = Inline(p);

            if (IsCode(p, style))
            {
                if (_last != Block.Code) { Separate(); _md.AppendLine("```"); }
                _md.AppendLine(PlainText(p));
                _last = Block.Code;
                return;
            }
            CloseCode();
            if (string.IsNullOrWhiteSpace(text)) return; // empty paragraphs are spacing, not content

            if (HeadingLevel(p, style) is int level)
            {
                Separate();
                _md.Append('#', level).Append(' ').AppendLine(text.Trim());
                _last = Block.Paragraph;
                return;
            }
            if (ListInfo(p) is { } list)
            {
                if (_last != Block.List || (list.Level == 0 && list.NumId != _listNumId)) _md.AppendLine();
                if (list.Level == 0) _listNumId = list.NumId;
                string marker = list.Ordered ? $"{Next(list.NumId, list.Level)}." : "-";
                _md.Append(' ', list.Level * 4).Append(marker).Append(' ').AppendLine(text.Trim());
                _last = Block.List;
                return;
            }
            Separate();
            if (style.Contains("quote")) _md.Append("> ");
            _md.AppendLine(text.Trim());
            _last = Block.Paragraph;
        }

        public void Table(Table table)
        {
            CloseCode();
            var rows = table.Elements<TableRow>()
                .Select(r => r.Elements<TableCell>().Select(c => string.Join("<br>", c.Elements<Paragraph>().Select(Inline).Where(s => s.Length > 0)).Replace("|", "\\|")).ToList())
                .Where(r => r.Count > 0).ToList();
            if (rows.Count == 0) return;
            // The first row is the header, which Markdown already sets in bold.
            rows[0] = rows[0].Select(c => c.Length > 4 && c.StartsWith("**") && c.EndsWith("**") && !c[2..^2].Contains("**") ? c[2..^2] : c).ToList();
            int columns = rows.Max(r => r.Count);
            Separate();
            for (int i = 0; i < rows.Count; i++)
            {
                var cells = rows[i].Concat(Enumerable.Repeat("", columns - rows[i].Count));
                _md.Append("| ").Append(string.Join(" | ", cells)).AppendLine(" |");
                if (i == 0) _md.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", columns))).AppendLine();
            }
            _last = Block.Paragraph;
        }

        private void Separate()
        {
            if (_last != Block.None) _md.AppendLine();
        }

        private void CloseCode()
        {
            if (_last != Block.Code) return;
            _md.AppendLine("```");
            _last = Block.Paragraph;
        }

        private int Next(int numId, int level)
        {
            // Restart deeper levels when a shallower item appears, as Word does.
            foreach (var key in _counters.Keys.Where(k => k.NumId == numId && k.Level > level).ToList()) _counters.Remove(key);
            _counters[(numId, level)] = _counters.GetValueOrDefault((numId, level)) + 1;
            return _counters[(numId, level)];
        }

        // ---------------------------------------------------------------- inline content

        private sealed record Span(string Text, bool Bold, bool Italic, bool Strike, bool Code, string? Link);

        private string Inline(Paragraph p)
        {
            var spans = new List<Span>();
            foreach (var child in p.ChildElements)
            {
                if (child is Run run) AddRun(spans, run, null);
                else if (child is Hyperlink link)
                {
                    string? url = link.Id?.Value is { } id ? main.HyperlinkRelationships.FirstOrDefault(r => r.Id == id)?.Uri.ToString() : null;
                    url ??= link.Anchor?.Value is { } anchor ? "#" + anchor : null;
                    foreach (var r in link.Elements<Run>()) AddRun(spans, r, url);
                }
                else if (child is SimpleField or InsertedRun)
                    foreach (var r in child.Elements<Run>()) AddRun(spans, r, null);
            }
            return Render(Merge(spans));
        }

        private void AddRun(List<Span> spans, Run run, string? link)
        {
            var rp = run.RunProperties;
            // Direct formatting wins; otherwise the run's character style decides (Word applies <strong> as "Strong").
            var style = rp?.RunStyle?.Val?.Value is { } id ? Style(id)?.StyleRunProperties : null;
            bool bold = Flag(rp?.Bold) ?? Flag(style?.Bold) ?? false, italic = Flag(rp?.Italic) ?? Flag(style?.Italic) ?? false;
            bool strike = Flag(rp?.Strike) ?? Flag(style?.Strike) ?? false;
            bool code = (rp?.RunFonts?.Ascii?.Value ?? style?.RunFonts?.Ascii?.Value) is { } font && IsMonospace(font);
            foreach (var part in run.ChildElements)
            {
                switch (part)
                {
                    case Text t: spans.Add(new(t.Text, bold, italic, strike, code, link)); break;
                    case TabChar: spans.Add(new(" ", bold, italic, strike, code, link)); break;
                    case Break: spans.Add(new("  \n", false, false, false, false, null)); break;
                    case Drawing d when Picture(d) is { } image: spans.Add(new(image, false, false, false, false, null)); break;
                }
            }
        }

        private static List<Span> Merge(List<Span> spans)
        {
            var merged = new List<Span>();
            foreach (var s in spans)
            {
                if (merged.Count > 0 && merged[^1] is var prev && prev with { Text = "" } == s with { Text = "" })
                    merged[^1] = prev with { Text = prev.Text + s.Text };
                else merged.Add(s);
            }
            return merged;
        }

        private static string Render(List<Span> spans)
        {
            var sb = new StringBuilder();
            foreach (var s in spans)
            {
                if (s.Text.StartsWith("![", StringComparison.Ordinal) || s.Text == "  \n") { sb.Append(s.Text); continue; }
                string core = s.Text.Trim();
                if (core.Length == 0) { sb.Append(s.Text); continue; }
                // Markers hug the words: "**bold** text", never "** bold **".
                string lead = s.Text[..s.Text.IndexOf(core[0])], trail = s.Text[(s.Text.LastIndexOf(core[^1]) + 1)..];
                string body = s.Code ? $"`{core.Replace("`", "\\`")}`" : Escape(core);
                if (s.Strike) body = $"~~{body}~~";
                if (s.Italic) body = $"*{body}*";
                if (s.Bold) body = $"**{body}**";
                if (s.Link is { } url) body = $"[{body}]({url.Replace(" ", "%20").Replace(")", "%29")})";
                sb.Append(lead).Append(body).Append(trail);
            }
            return sb.ToString();
        }

        private static string Escape(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c is '\\' or '*' or '_' or '`' or '[' or ']' or '<' or '>') sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        private string? Picture(Drawing drawing)
        {
            string? embed = drawing.Descendants<A.Blip>().FirstOrDefault()?.Embed?.Value;
            if (embed is null || main.GetPartById(embed) is not ImagePart image) return null;
            using var stream = image.GetStream();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            string alt = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>().FirstOrDefault()?.Description?.Value ?? "";
            return $"![{Escape(alt)}](data:{image.ContentType};base64,{System.Convert.ToBase64String(ms.ToArray())})";
        }

        private static string PlainText(Paragraph p) => string.Concat(p.Descendants().Select(e => e switch
        {
            Text t => t.Text,
            TabChar => "\t",
            Break => "\n",
            _ => "",
        }));

        // ---------------------------------------------------------------- paragraph properties

        private string StyleName(Paragraph p)
        {
            string? id = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            if (id is null) return "";
            var style = Style(id);
            return (style?.StyleName?.Val?.Value ?? id).ToLowerInvariant();
        }

        private Style? Style(string id) =>
            main.StyleDefinitionsPart?.Styles?.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == id);

        private int? HeadingLevel(Paragraph p, string style)
        {
            if (style == "title") return 1;
            if (style.StartsWith("heading ", StringComparison.Ordinal) && int.TryParse(style[8..], out int n) && n is >= 1 and <= 6) return n;
            // Localized style names: fall back to the outline level the style or paragraph declares (0-based).
            int? outline = p.ParagraphProperties?.OutlineLevel?.Val?.Value
                ?? (p.ParagraphProperties?.ParagraphStyleId?.Val?.Value is { } id ? Style(id)?.StyleParagraphProperties?.OutlineLevel?.Val?.Value : null);
            return outline is int o and < 6 ? o + 1 : null;
        }

        private sealed record ListItem(int NumId, int Level, bool Ordered);

        private ListItem? ListInfo(Paragraph p)
        {
            var numPr = p.ParagraphProperties?.NumberingProperties
                ?? (p.ParagraphProperties?.ParagraphStyleId?.Val?.Value is { } id ? Style(id)?.StyleParagraphProperties?.NumberingProperties : null);
            int? numId = numPr?.NumberingId?.Val?.Value;
            if (numId is not int nid || nid == 0) return null;
            int level = numPr!.NumberingLevelReference?.Val?.Value ?? 0;
            var numbering = main.NumberingDefinitionsPart?.Numbering;
            var instance = numbering?.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == nid);
            int? abstractId = instance?.AbstractNumId?.Val?.Value;
            var abstractNum = numbering?.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId);
            var format = abstractNum?.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == level)?.NumberingFormat?.Val?.Value;
            return new ListItem(nid, Math.Clamp(level, 0, 8), format is not null && format != NumberFormatValues.Bullet && format != NumberFormatValues.None);
        }

        private static bool IsCode(Paragraph p, string style)
        {
            if (style.Contains("code") || style is "html preformatted" or "plain text" or "source code") return true;
            var runs = p.Elements<Run>().Where(r => r.GetFirstChild<Text>() is { Text.Length: > 0 }).ToList();
            return runs.Count > 0 && runs.All(r => r.RunProperties?.RunFonts?.Ascii?.Value is { } f && IsMonospace(f));
        }

        private static bool IsMonospace(string font) =>
            font.Contains("Consolas", StringComparison.OrdinalIgnoreCase) || font.Contains("Courier", StringComparison.OrdinalIgnoreCase)
            || font.Contains("Mono", StringComparison.OrdinalIgnoreCase) || font.Contains("Cascadia", StringComparison.OrdinalIgnoreCase);

        private static bool? Flag(OnOffType? flag) => flag is null ? null : flag.Val is null || flag.Val.Value;
    }
}
