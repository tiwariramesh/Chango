using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Chango.Conversion.Documents;

public enum DocumentKind { Text, Sheet, Slides, Drawing, Pdf, Markup }

/// <summary>
/// The apps that do the actual conversion, found on this PC: Microsoft Word, Excel and PowerPoint (driven through COM,
/// invisibly, files opened read-only) and LibreOffice (headless, with its own private profile); Office is preferred for
/// its own formats. CorelDRAW drawings need neither: Chango reads them itself (<see cref="CorelDraw"/>). Markdown and
/// plain text are turned into HTML first (<see cref="Markup"/>), which either app then lays out. Results go to a
/// private temp folder that is deleted on <see cref="Dispose"/>; the provider copies them to their place with SafeOutput.
/// </summary>
public sealed class DocumentEngines : IDisposable
{
    private static readonly Dictionary<string, DocumentKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".doc"] = DocumentKind.Text, [".docx"] = DocumentKind.Text, [".docm"] = DocumentKind.Text, [".rtf"] = DocumentKind.Text,
        [".odt"] = DocumentKind.Text,
        [".xls"] = DocumentKind.Sheet, [".xlsx"] = DocumentKind.Sheet, [".xlsm"] = DocumentKind.Sheet, [".ods"] = DocumentKind.Sheet,
        [".csv"] = DocumentKind.Sheet, [".tsv"] = DocumentKind.Sheet,
        [".ppt"] = DocumentKind.Slides, [".pptx"] = DocumentKind.Slides, [".pptm"] = DocumentKind.Slides, [".pps"] = DocumentKind.Slides,
        [".ppsx"] = DocumentKind.Slides, [".odp"] = DocumentKind.Slides,
        [".cdr"] = DocumentKind.Drawing, [".cmx"] = DocumentKind.Drawing,
        [".pdf"] = DocumentKind.Pdf, // to Word: Word (or LibreOffice) rebuilds the text and layout as an editable document
        [".md"] = DocumentKind.Markup, [".markdown"] = DocumentKind.Markup, [".txt"] = DocumentKind.Markup,
    };

    /// <summary>Some password is needed so Office fails instead of asking; a file without one ignores it.</summary>
    private const string NoPassword = "chango-no-password";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);
    private const int Utf8 = 65001; // msoEncodingUTF8

    private static readonly Lazy<bool> Word = new(() => HasProgId("Word.Application")), Excel = new(() => HasProgId("Excel.Application")),
        PowerPoint = new(() => HasProgId("PowerPoint.Application"));
    private static readonly AppFinder LibreOffice = new(() => ExternalTools.Find("soffice.exe", @"LibreOffice\program\soffice.exe", @"scoop\apps\libreoffice\current\LibreOffice\program\soffice.exe"));

    public static DocumentKind? KindOf(string path) => Kinds.TryGetValue(Path.GetExtension(path), out var k) ? k : null;

    public static bool CanOpen(DocumentKind kind) => kind switch
    {
        DocumentKind.Text or DocumentKind.Pdf or DocumentKind.Markup => Word.Value || LibreOffice.Value is not null,
        DocumentKind.Sheet => Excel.Value || LibreOffice.Value is not null,
        DocumentKind.Slides => PowerPoint.Value || LibreOffice.Value is not null,
        DocumentKind.Drawing => CorelDraw.Available,
        _ => false,
    };

    /// <summary>The apps found, for Settings → About's capability list.</summary>
    public static string[] Describe() => new (bool Found, string Name)[]
    {
        (Word.Value, "Word"), (Excel.Value, "Excel"), (PowerPoint.Value, "PowerPoint"), (LibreOffice.Value is not null, "LibreOffice"),
    }.Where(a => a.Found).Select(a => a.Name).ToArray();

    // Short names all the way down: a long file name inside a deep temp folder passes Windows' 260-character path
    // limit, which LibreOffice and Office can't write to. The real name is only used for the final result.
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "Chango", "docs", Guid.NewGuid().ToString("N")[..8]);
    private int _jobs;

    /// <summary>A fresh, short-named folder inside this conversion's private temp folder (deleted with it).</summary>
    public string NewWorkFolder()
    {
        string dir = Path.Combine(_temp, (++_jobs).ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Converts <paramref name="file"/> to <paramref name="extension"/> in a private temp folder; returns the new file.</summary>
    public async Task<string> ConvertToTempAsync(string file, string extension, CancellationToken ct)
    {
        var kind = KindOf(file) ?? throw new NotSupportedException(Path.GetExtension(file));
        if (!File.Exists(file)) throw new FileNotFoundException(null, file);
        string dir = NewWorkFolder();
        string sourceExt = Path.GetExtension(file).ToLowerInvariant();
        string input = Path.Combine(dir, "source" + sourceExt);
        if (kind == DocumentKind.Markup)
        {
            // Markdown and text become a styled HTML page, which Word and LibreOffice open as a regular document.
            input = Path.Combine(dir, "source.html");
            File.WriteAllText(input, Markup.ToHtmlPage(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file), Markup.IsMarkdown(file)), Encoding.UTF8);
            kind = DocumentKind.Text;
        }
        else File.Copy(file, input);
        string outDir = Path.Combine(dir, "out"); // apart from the input, so "csv → tsv" can't collide with it
        Directory.CreateDirectory(outDir);
        string output = Path.Combine(outDir, "source" + extension);

        string? office = kind switch
        {
            DocumentKind.Text or DocumentKind.Pdf when Word.Value => "Word.Application",
            DocumentKind.Sheet when Excel.Value => "Excel.Application",
            DocumentKind.Slides when PowerPoint.Value => "PowerPoint.Application",
            _ => null,
        };
        if (office is not null)
            await StaWorker.RunAsync(() => ConvertWithOffice(office, kind, input, output), "Chango Office");
        else if (kind != DocumentKind.Drawing && LibreOffice.Value is { } soffice)
            await ConvertWithLibreOfficeAsync(soffice, input, output, ct);
        else throw new NotSupportedException("No app on this PC can open this file.");

        if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new IOException("The conversion didn't produce a file.");
        if (extension == ".html") InlineImages(output);
        return output;
    }

    // ------------------------------------------------------------------ Microsoft Office (late-bound COM, on an STA thread)

    private static readonly object PowerPointGate = new();

    /// <summary>
    /// Runs <paramref name="work"/> with PowerPoint to itself. Word and Excel start a fresh copy for each conversion,
    /// but PowerPoint is one shared instance: two jobs at once would quit it under each other.
    /// </summary>
    public static void WithPowerPoint(Action work)
    {
        lock (PowerPointGate) work();
    }

    private static void ConvertWithOffice(string progId, DocumentKind kind, string file, string output)
    {
        if (kind == DocumentKind.Slides) WithPowerPoint(() => ConvertWithOfficeApp(progId, kind, file, output));
        else ConvertWithOfficeApp(progId, kind, file, output);
    }

    private static void ConvertWithOfficeApp(string progId, DocumentKind kind, string file, string output)
    {
        string ext = Path.GetExtension(output).ToLowerInvariant();
        // PowerPoint runs as one shared instance: if the user has it open, leave it running afterwards.
        bool wasRunning = kind == DocumentKind.Slides && Process.GetProcessesByName("POWERPNT").Length > 0;
        dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID(progId)!)!;
        try
        {
            switch (kind)
            {
                case DocumentKind.Text or DocumentKind.Pdf:
                    app.DisplayAlerts = 0; // wdAlertsNone
                    dynamic doc = app.Documents.Open(file, false, true, false, NoPassword); // ConfirmConversions, ReadOnly, AddToRecentFiles
                    try
                    {
                        switch (ext)
                        {
                            case ".pdf": doc.ExportAsFixedFormat(output, 17); break;                     // wdExportFormatPDF
                            case ".docx": doc.SaveAs2(output, 16); break;                                // wdFormatDocumentDefault
                            case ".txt": doc.SaveAs2(output, 2, Encoding: Utf8, LineEnding: 0); break;   // wdFormatText, CRLF
                            case ".html": doc.SaveAs2(output, 10, Encoding: Utf8); break;                // wdFormatFilteredHTML
                            case ".rtf": doc.SaveAs2(output, 6); break;                                  // wdFormatRTF
                            case ".odt": doc.SaveAs2(output, 23); break;                                 // wdFormatOpenDocumentText
                            default: throw new NotSupportedException(ext);
                        }
                    }
                    finally { doc.Close(0); Release(doc); }
                    break;

                case DocumentKind.Sheet:
                    app.DisplayAlerts = false;
                    dynamic book = OpenWorkbook(app, file);
                    string? utf16 = null;
                    try
                    {
                        switch (ext)
                        {
                            case ".pdf": book.ExportAsFixedFormat(0, output); break; // xlTypePDF
                            case ".xlsx": book.SaveAs(output, 51); break;            // xlOpenXMLWorkbook
                            case ".csv": book.SaveAs(output, 62); break;             // xlCSVUTF8 (the active sheet)
                            case ".ods": book.SaveAs(output, 60); break;             // xlOpenDocumentSpreadsheet
                            case ".tsv":
                                utf16 = Path.ChangeExtension(output, ".txt");
                                book.SaveAs(utf16, 42); // xlUnicodeText
                                break;
                            default: throw new NotSupportedException(ext);
                        }
                    }
                    finally { book.Close(false); Release(book); }
                    // Excel's tab-separated text is UTF-16 ("Unicode text"); everything else expects UTF-8.
                    if (utf16 is not null) File.WriteAllText(output, CleanTsv(File.ReadAllText(utf16, Encoding.Unicode)), new UTF8Encoding(false));
                    break;

                case DocumentKind.Slides:
                    dynamic deck = app.Presentations.Open(file, -1, 0, 0); // ReadOnly, not Untitled, no window
                    try
                    {
                        if (ext is not (".pdf" or ".pptx")) throw new NotSupportedException(ext);
                        deck.SaveAs(output, ext == ".pdf" ? 32 : 24); // ppSaveAsPDF / ppSaveAsOpenXMLPresentation
                    }
                    finally { deck.Close(); Release(deck); }
                    break;
            }
        }
        finally
        {
            try
            {
                if (kind is DocumentKind.Text or DocumentKind.Pdf) app.Quit(0);
                else if (kind == DocumentKind.Sheet) app.Quit();
                else if (!wasRunning && app.Presentations.Count == 0) app.Quit();
            }
            catch (COMException) { /* already gone */ }
            Release(app);
        }
    }

    /// <summary>
    /// Workbooks open read-only. CSV and TSV are read as UTF-8 with an explicit delimiter (Excel's own CSV opening
    /// guesses the encoding and uses the regional list separator); OpenText only honours that for a .txt name.
    /// </summary>
    private static dynamic OpenWorkbook(dynamic app, string file)
    {
        string ext = Path.GetExtension(file).ToLowerInvariant();
        if (ext is not (".csv" or ".tsv"))
            return app.Workbooks.Open(file, 0, true, Missing.Value, NoPassword); // UpdateLinks: no, ReadOnly
        string text = Path.ChangeExtension(file, ".txt");
        File.Move(file, text);
        bool tab = ext == ".tsv";
        app.Workbooks.OpenText(Filename: text, Origin: Utf8, StartRow: 1, DataType: 1, TextQualifier: 1, // xlDelimited, xlTextQualifierDoubleQuote
            ConsecutiveDelimiter: false, Tab: tab, Semicolon: false, Comma: !tab, Space: false, Other: false, Local: false);
        return app.ActiveWorkbook;
    }

    /// <summary>
    /// Excel quotes any field with a comma even in tab-separated text; only tabs, line breaks and quotes need it.
    /// Re-quotes each field by that rule, so a field quoted for a real reason stays quoted.
    /// </summary>
    public static string CleanTsv(string text)
    {
        var sb = new StringBuilder(text.Length);
        var field = new StringBuilder();
        bool quoted = false, inQuotes = false;
        void EndField()
        {
            string f = field.ToString();
            if (f.IndexOfAny(['\t', '\n', '\r', '"']) >= 0) sb.Append('"').Append(f.Replace("\"", "\"\"")).Append('"');
            else sb.Append(f);
            field.Clear();
            quoted = false;
        }
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0 && !quoted) inQuotes = quoted = true;
            else if (c == '\t') { EndField(); sb.Append('\t'); }
            else if (c is '\r' or '\n')
            {
                EndField();
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                sb.Append("\r\n");
            }
            else field.Append(c);
        }
        if (field.Length > 0 || quoted) EndField();
        return sb.ToString();
    }

    private static void Release(object o)
    {
        try { Marshal.FinalReleaseComObject(o); } catch (ArgumentException) { }
    }

    private static bool HasProgId(string progId)
    {
        try { return Type.GetTypeFromProgID(progId) is not null; }
        catch { return false; }
    }

    // ------------------------------------------------------------------ LibreOffice (command line)

    private static async Task ConvertWithLibreOfficeAsync(string soffice, string input, string output, CancellationToken ct)
    {
        string inExt = Path.GetExtension(input).ToLowerInvariant(), outExt = Path.GetExtension(output).ToLowerInvariant();
        string outDir = Path.GetDirectoryName(output)!;
        // A profile of its own: works while the user has LibreOffice open, and never touches their settings.
        string profile = new Uri(Path.Combine(Path.GetTempPath(), "Chango", "libreoffice-profile")).AbsoluteUri;
        string? inFilter = inExt switch
        {
            ".pdf" => "writer_pdf_import", // open a PDF in Writer, not Draw
            ".html" => "HTML (StarWriter)", // a document, not a web page
            ".csv" => "CSV:44,34,76",       // comma, double quote, UTF-8
            ".tsv" => "CSV:9,34,76",
            _ => null,
        };
        string[] import = inFilter is null ? [] : [$"--infilter={inFilter}"];
        await ExternalTools.RunAsync(soffice, ["--headless", "--norestore", "--nologo", "--nodefault", "--nolockcheck", $"-env:UserInstallation={profile}",
            .. import, "--convert-to", LibreOfficeFilter(outExt), "--outdir", outDir, input], Timeout, ct);
        // LibreOffice names the result after the input and its filter's own extension (tab-separated text comes out as .csv).
        string produced = Path.Combine(outDir, "source" + (outExt == ".tsv" ? ".csv" : outExt));
        if (produced != output && File.Exists(produced)) File.Move(produced, output);
    }

    private static string LibreOfficeFilter(string extension) => extension switch
    {
        ".docx" => "docx:MS Word 2007 XML",
        ".xlsx" => "xlsx:Calc MS Excel 2007 XML",
        ".pptx" => "pptx:Impress MS PowerPoint 2007 XML",
        ".txt" => "txt:Text (encoded):UTF8",
        ".html" => "html:HTML (StarWriter)",
        ".csv" => "csv:Text - txt - csv (StarCalc):44,34,76,1",
        ".tsv" => "csv:Text - txt - csv (StarCalc):9,34,76,1",
        _ => extension.TrimStart('.'),
    };

    // ------------------------------------------------------------------ HTML results

    /// <summary>
    /// Word and LibreOffice save a document's pictures as files beside the HTML; they're embedded instead, so the one
    /// HTML file Chango hands back shows them wherever it's opened.
    /// </summary>
    private static void InlineImages(string html)
    {
        string dir = Path.GetDirectoryName(html)!;
        string text = File.ReadAllText(html);
        string inlined = Regex.Replace(text, "(?<attr>\\bsrc=)(?<q>[\"'])(?<path>[^\"':]+?)\\k<q>", m =>
        {
            string path;
            try { path = Path.GetFullPath(Path.Combine(dir, Uri.UnescapeDataString(m.Groups["path"].Value).Replace('/', '\\'))); }
            catch { return m.Value; }
            if (!path.StartsWith(dir, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return m.Value;
            string mime = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".svg" => "image/svg+xml",
                ".bmp" => "image/bmp", ".webp" => "image/webp", _ => "application/octet-stream",
            };
            return $"{m.Groups["attr"].Value}{m.Groups["q"].Value}data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}{m.Groups["q"].Value}";
        }, RegexOptions.IgnoreCase);
        if (!ReferenceEquals(inlined, text)) File.WriteAllText(html, inlined, new UTF8Encoding(false));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true); } catch { /* best effort */ }
    }
}
