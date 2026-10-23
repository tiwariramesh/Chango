using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace Chango.Conversion.Pdf;

/// <summary>
/// A PDF's text layer as plain text, Markdown or table rows, read with PdfPig (no Office needed). Scanned PDFs have no
/// text layer; those fail with a message saying so rather than producing an empty file.
/// <para>
/// Layout model shared by Markdown and Excel: words are grouped into lines by baseline, lines are split into cells
/// where the gap between words is wider than a couple of characters, and consecutive multi-cell lines form a
/// <see cref="Region"/> whose columns come from the vertical gaps no cell crosses. A region is then read as a list
/// (first column all bullets or numbers), a table (short cells) or text set in columns (long cells).
/// </para>
/// </summary>
public static partial class PdfText
{
    public const string NoTextMessage = "This PDF has no text to extract (it's probably a scan).";

    public static PigDocument Open(string path)
    {
        try { return PigDocument.Open(path); }
        catch (UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException) { throw new InvalidDataException("This PDF is password-protected."); }
    }

    /// <summary>Every page's text in reading order, pages separated by a blank line.</summary>
    public static string ToPlainText(string path, Action<double>? fraction = null, CancellationToken ct = default)
    {
        using var pdf = Open(path);
        var pages = new List<string>();
        int count = pdf.NumberOfPages;
        foreach (var page in pdf.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            pages.Add(ContentOrderTextExtractor.GetText(page, true).Trim());
            fraction?.Invoke((double)page.Number / count);
        }
        string text = string.Join(Environment.NewLine + Environment.NewLine, pages.Where(p => p.Length > 0));
        if (text.Length == 0) throw new InvalidDataException(NoTextMessage);
        return text.ReplaceLineEndings() + Environment.NewLine;
    }

    // ------------------------------------------------------------------ layout

    private sealed record Line(List<Word> Words, List<List<Word>> Cells, double Baseline, double Size)
    {
        public string Text => string.Join(" ", Words.Select(w => w.Text));
        public double Left => Words[0].BoundingBox.Left;
    }

    /// <summary>Lines read together: one plain line (no <see cref="Bounds"/>), or a run of multi-cell lines and their column separators.</summary>
    private sealed record Region(List<Line> Lines, List<double> Bounds)
    {
        public bool Tabular => Bounds.Count > 0;

        /// <summary>A line's cells placed in the region's columns ("" where a column is empty).</summary>
        public string[] Row(Line line)
        {
            var row = new string?[Bounds.Count + 1];
            foreach (var cell in line.Cells)
            {
                string text = string.Join(" ", cell.Select(w => w.Text));
                double centre = (cell[0].BoundingBox.Left + cell[^1].BoundingBox.Right) / 2;
                int column = Bounds.Count(b => b < centre);
                row[column] = row[column] is null ? text : row[column] + " " + text;
            }
            return row.Select(c => c ?? "").ToArray();
        }
    }

    private sealed record PageLayout(List<Region> Regions, double CharWidth, double LineStep);

    private static PageLayout Layout(Page page)
    {
        var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters).Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        if (words.Count == 0) return new([], 0, 0);
        double charWidth = Median(words.Select(w => w.BoundingBox.Width / Math.Max(1, w.Text.Length)));
        double lineHeight = Median(words.Select(w => w.BoundingBox.Height));

        // Lines, top to bottom (PDF y grows upwards); words left to right.
        var groups = new List<List<Word>>();
        foreach (var w in words.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            var last = groups.LastOrDefault();
            if (last is not null && Math.Abs(last[0].BoundingBox.Bottom - w.BoundingBox.Bottom) < lineHeight * 0.5) last.Add(w);
            else groups.Add([w]);
        }
        double gap = Math.Max(charWidth * 1.8, 4); // wider than the space between words of a sentence
        var lines = groups.Select(g =>
        {
            g.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
            var cells = new List<List<Word>> { new() { g[0] } };
            for (int i = 1; i < g.Count; i++)
            {
                if (g[i].BoundingBox.Left - g[i - 1].BoundingBox.Right > gap) cells.Add([g[i]]);
                else cells[^1].Add(g[i]);
            }
            return new Line(g, cells, g[0].BoundingBox.Bottom, Median(g.SelectMany(w => w.Letters).Select(l => l.PointSize)));
        }).ToList();

        // The usual distance between baselines: a larger one starts a new paragraph.
        double step = Median(lines.Zip(lines.Skip(1), (a, b) => a.Baseline - b.Baseline));

        var regions = new List<Region>();
        foreach (var line in lines)
        {
            var open = regions.LastOrDefault();
            // Rows of one table or list have the same number of cells (so a list right above a table stays apart).
            bool continues = line.Cells.Count > 1 && open is { Tabular: true } && open.Lines[^1].Cells.Count == line.Cells.Count
                && open.Lines[^1].Baseline - line.Baseline <= Math.Max(step, lineHeight) * 3;
            if (continues) open!.Lines.Add(line);
            else regions.Add(new Region([line], line.Cells.Count > 1 ? [double.NaN] : [])); // NaN: bounds still to compute
        }
        // Columns per region; lines whose cells overlap (no clean gap) are read as plain lines after all.
        var result = new List<Region>();
        foreach (var region in regions)
        {
            if (!region.Tabular) { result.Add(region); continue; }
            var bounds = Columns(region.Lines, charWidth);
            if (bounds.Count > 0) result.Add(region with { Bounds = bounds });
            else result.AddRange(region.Lines.Select(l => new Region([l], [])));
        }
        return new(result, charWidth, step);
    }

    /// <summary>Column separators: the middle of every x-range that no cell of these lines covers.</summary>
    private static List<double> Columns(List<Line> lines, double charWidth)
    {
        var bounds = new List<double>();
        double reach = double.NegativeInfinity;
        foreach (var (left, right) in lines.SelectMany(l => l.Cells).Select(c => (c[0].BoundingBox.Left, c[^1].BoundingBox.Right)).OrderBy(r => r.Item1))
        {
            if (reach > double.NegativeInfinity && left - reach > charWidth * 0.8) bounds.Add((reach + left) / 2);
            reach = Math.Max(reach, right);
        }
        return bounds;
    }

    private static List<PageLayout> Pages(PigDocument pdf, Action<double>? fraction, double share, CancellationToken ct)
    {
        int count = pdf.NumberOfPages;
        var pages = new List<PageLayout>();
        foreach (var page in pdf.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            pages.Add(Layout(page));
            fraction?.Invoke(share * page.Number / count);
        }
        if (pages.All(p => p.Regions.Count == 0)) throw new InvalidDataException(NoTextMessage);
        return pages;
    }

    // ------------------------------------------------------------------ tables (Excel)

    /// <summary>Each page as rows of cells; lines of running text stay whole in the first column.</summary>
    public static List<List<string[]>> ToTables(string path, Action<double>? fraction = null, CancellationToken ct = default)
    {
        using var pdf = Open(path);
        return Pages(pdf, fraction, 1, ct)
            .Select(p => p.Regions.SelectMany(r => r.Tabular ? r.Lines.Select(r.Row) : new[] { new[] { r.Lines[0].Text } }).ToList())
            .ToList();
    }

    // ------------------------------------------------------------------ Markdown

    /// <summary>
    /// Markdown from the page layout: lines set larger than the body text become headings (by how much larger), bullet
    /// and numbered lines become list items, tables become pipe tables, text set in columns is read column by column,
    /// and lines of a paragraph are joined (hyphenated breaks included).
    /// </summary>
    public static string ToMarkdown(string path, Action<double>? fraction = null, CancellationToken ct = default)
    {
        using var pdf = Open(path);
        var pages = Pages(pdf, fraction, 0.9, ct);
        var sizes = pages.SelectMany(p => p.Regions).SelectMany(r => r.Lines).SelectMany(l => l.Words).SelectMany(w => w.Letters)
            .Select(l => Math.Round(l.PointSize * 2) / 2).Where(s => s > 0).ToList();
        double body = sizes.GroupBy(s => s).MaxBy(g => g.Count())!.Key; // the size most letters are set in

        var md = new MarkdownWriter(body);
        foreach (var page in pages)
        {
            foreach (var region in page.Regions)
            {
                if (!region.Tabular) { md.Text(region.Lines[0].Text, region.Lines[0], page); continue; }
                var rows = region.Lines.Select(region.Row).ToList();
                if (rows.All(r => BulletOnly().IsMatch(r[0])))
                    foreach (var r in rows) md.Item("-", string.Join(" ", r.Skip(1).Where(c => c.Length > 0)), region.Bounds[0]);
                else if (rows.All(r => NumberOnly().Match(r[0]).Success))
                    foreach (var r in rows) md.Item(NumberOnly().Match(r[0]).Groups["n"].Value + ".", string.Join(" ", r.Skip(1).Where(c => c.Length > 0)), region.Bounds[0]);
                else if (rows.Count >= 4 && rows.SelectMany(r => r).Where(c => c.Length > 0).Average(c => c.Length) > 30)
                {
                    // Long text side by side is a page set in columns: read each column top to bottom.
                    for (int c = 0; c <= region.Bounds.Count; c++)
                        for (int i = 0; i < rows.Count; i++)
                            if (rows[i][c].Length > 0) md.Text(rows[i][c], region.Lines[i], page);
                    md.Flush();
                }
                else md.Table(rows);
            }
            md.Flush();
        }
        fraction?.Invoke(1);
        return md.ToString();
    }

    private sealed class MarkdownWriter(double body)
    {
        private readonly StringBuilder _md = new();
        private readonly List<string> _paragraph = [], _heading = [];
        private readonly List<string> _items = [];
        private int _headingLevel;
        private double _itemTextLeft;
        private Line? _previous;

        public void Text(string text, Line line, PageLayout page)
        {
            text = text.Trim();
            if (text.Length == 0) return;
            if (Bullet().Match(text) is { Success: true } b) { Item("-", text[b.Length..], line.Left + 2 * page.CharWidth); _previous = line; return; }
            if (Numbered().Match(text) is { Success: true } n) { Item(n.Groups["n"].Value + ".", text[n.Length..], line.Left + 2 * page.CharWidth); _previous = line; return; }

            bool near = _previous is not null && _previous.Baseline - line.Baseline is > 0 and var d && d <= page.LineStep * 1.35;
            if (line.Size >= body * 1.15 && text.Length <= 200)
            {
                int level = line.Size >= body * 1.8 ? 1 : line.Size >= body * 1.4 ? 2 : 3;
                if (_heading.Count == 0 || level != _headingLevel || !near) { Flush(); _headingLevel = level; }
                _heading.Add(text);
            }
            else if (_items.Count > 0 && near && line.Left >= _itemTextLeft - page.CharWidth * 2)
                _items[^1] = Join([_items[^1], Escape(text)]); // a list item's wrapped line
            else
            {
                if (_paragraph.Count == 0 || !near || _heading.Count > 0 || _items.Count > 0 || Math.Abs(line.Size - _previous!.Size) > 0.6) Flush();
                _paragraph.Add(text);
            }
            _previous = line;
        }

        public void Item(string marker, string text, double textLeft)
        {
            if (_items.Count == 0) Flush();
            _items.Add($"{marker} {Escape(text.Trim())}");
            _itemTextLeft = textLeft;
        }

        public void Table(List<string[]> rows)
        {
            Flush();
            int columns = rows.Max(r => r.Length);
            string Cell(string[] r, int c) => c < r.Length ? Escape(r[c]).Replace("|", "\\|") : "";
            for (int i = 0; i < rows.Count; i++)
            {
                _md.Append("| ").Append(string.Join(" | ", Enumerable.Range(0, columns).Select(c => Cell(rows[i], c)))).AppendLine(" |");
                if (i == 0) _md.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", columns))).AppendLine();
            }
            _md.AppendLine();
            _previous = null;
        }

        public void Flush()
        {
            if (_heading.Count > 0) _md.Append('#', _headingLevel).Append(' ').AppendLine(Escape(Join(_heading))).AppendLine();
            if (_paragraph.Count > 0) _md.AppendLine(Escape(Join(_paragraph))).AppendLine();
            if (_items.Count > 0)
            {
                foreach (var item in _items) _md.AppendLine(item);
                _md.AppendLine();
            }
            _heading.Clear(); _paragraph.Clear(); _items.Clear();
        }

        public override string ToString()
        {
            Flush();
            return _md.ToString().TrimEnd().ReplaceLineEndings() + Environment.NewLine;
        }
    }

    /// <summary>Lines of one paragraph as running text; "exam-" + "ple" → "example".</summary>
    private static string Join(IReadOnlyList<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (sb.Length > 0 && sb[^1] == '-' && sb.Length > 1 && char.IsLetter(sb[^2]) && line.Length > 0 && char.IsLower(line[0])) sb.Length--;
            else if (sb.Length > 0) sb.Append(' ');
            sb.Append(line);
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
        // A line that starts like Markdown syntax ("# ", "1. ", "- ") would change meaning: escape the first character.
        return Regex.Replace(sb.ToString(), @"^(#|\d+\.|[-+]) ", m => "\\" + m.Value);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Where(v => v > 0).Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    // Bullets as fonts draw them, including the Symbol and Wingdings private-use glyphs Word uses. Glyph bullets may
    // touch their text; "-", "*" and "–" only count with a space after them ("-7" is a number).
    private const string Glyphs = "•◦▪▫●○■□‣⁃";

    [GeneratedRegex($"^([{Glyphs}]\\s*|[–\\-*]\\s+)(?=\\S)")]
    private static partial Regex Bullet();

    [GeneratedRegex($"^[{Glyphs}–\\-*]$")]
    private static partial Regex BulletOnly();

    [GeneratedRegex(@"^(?<n>\d{1,3})[.)]\s+")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"^(?<n>\d{1,3})[.)]$")]
    private static partial Regex NumberOnly();

    /// <summary>"1,234.50", "-7", "12%" … as a number for a spreadsheet cell; null when the text isn't one.</summary>
    public static double? AsNumber(string text)
    {
        string t = text.Trim();
        if (t.Length == 0 || t.Length > 30) return null;
        bool percent = t.EndsWith('%');
        if (percent) t = t[..^1];
        bool negative = t.StartsWith('(') && t.EndsWith(')');
        if (negative) t = t[1..^1];
        if (!Regex.IsMatch(t, @"^[-+]?(\d{1,3}(,\d{3})+|\d+)(\.\d+)?$")) return null;
        double v = double.Parse(t.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture);
        if (negative) v = -v;
        return percent ? v / 100 : v;
    }
}
