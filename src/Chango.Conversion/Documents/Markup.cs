using System.Net;
using System.Text;
using Markdig;

namespace Chango.Conversion.Documents;

/// <summary>
/// Markdown and plain text as documents: to a styled HTML page (which Word or LibreOffice then lays out as DOCX or
/// PDF), and Markdown to plain text. Markdig does the parsing (GitHub-style tables, task lists, footnotes …).
/// </summary>
public static class Markup
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static bool IsMarkdown(string path) => Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown";

    /// <summary>The document's body as HTML: Markdown rendered, or plain text as paragraphs (blank lines separate them).</summary>
    public static string ToHtmlBody(string text, bool markdown)
    {
        if (markdown) return Markdown.ToHtml(text, Pipeline);
        var html = new StringBuilder();
        foreach (var para in text.Replace("\r\n", "\n").Split("\n\n"))
        {
            if (string.IsNullOrWhiteSpace(para)) continue;
            html.Append("<p>").Append(string.Join("<br>\n", para.Trim('\n').Split('\n').Select(WebUtility.HtmlEncode))).Append("</p>\n");
        }
        return html.ToString();
    }

    /// <summary>A complete page with print-friendly styling, so DOCX and PDF results look like a written document.</summary>
    public static string ToHtmlPage(string text, string title, bool markdown) => $$"""
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>
        body { font-family: Calibri, "Segoe UI", Arial, sans-serif; font-size: 11pt; line-height: 1.4; color: #1a1a1a; }
        h1, h2, h3, h4, h5, h6 { font-family: "Segoe UI", Calibri, Arial, sans-serif; color: #0A7C7B; margin: 14pt 0 6pt; }
        h1 { font-size: 20pt; } h2 { font-size: 16pt; } h3 { font-size: 13pt; }
        code, pre { font-family: Consolas, "Courier New", monospace; font-size: 10pt; }
        pre { background: #F3F6F6; padding: 8pt; border: 1px solid #DDE5E5; }
        table { border-collapse: collapse; margin: 6pt 0; }
        th, td { border: 1px solid #BFCACA; padding: 4pt 6pt; vertical-align: top; }
        th { background: #DFF5F4; }
        blockquote { margin-left: 12pt; padding-left: 8pt; border-left: 3px solid #0EA5A4; color: #444; }
        </style></head>
        <body>
        {{ToHtmlBody(text, markdown)}}
        </body></html>
        """;

    /// <summary>A standalone HTML file from Markdown or text (no app needed).</summary>
    public static string ToHtmlFile(string path) =>
        ToHtmlPage(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path), IsMarkdown(path));

    /// <summary>Markdown without its markup: the words, as someone would read them.</summary>
    public static string ToPlainText(string markdown) => Markdown.ToPlainText(markdown, Pipeline);
}
