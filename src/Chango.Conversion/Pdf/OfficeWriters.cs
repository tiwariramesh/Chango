using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace Chango.Conversion.Pdf;

/// <summary>Minimal, standards-valid Office files written with the Open XML SDK (no Office needed).</summary>
public static class OfficeWriters
{
    // ------------------------------------------------------------------ Excel

    /// <summary>A workbook with one sheet per table ("Page 1" …); numbers are stored as numbers, the rest as text.</summary>
    public static void WriteWorkbook(Stream stream, IReadOnlyList<List<string[]>> sheets)
    {
        using var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
        var book = doc.AddWorkbookPart();
        book.Workbook = new S.Workbook();
        var list = book.Workbook.AppendChild(new S.Sheets());
        for (int i = 0; i < sheets.Count; i++)
        {
            var part = book.AddNewPart<WorksheetPart>();
            var data = new S.SheetData();
            var rows = sheets[i];
            int columns = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
            var worksheet = new S.Worksheet();
            if (columns > 0)
            {
                // Width from the longest text in each column (Excel's unit is roughly one character).
                var cols = new S.Columns();
                for (int c = 0; c < columns; c++)
                {
                    int longest = rows.Max(r => c < r.Length ? r[c].Length : 0);
                    cols.Append(new S.Column { Min = (uint)c + 1, Max = (uint)c + 1, Width = Math.Clamp(longest + 2, 8, 60), CustomWidth = true });
                }
                worksheet.Append(cols);
            }
            for (int r = 0; r < rows.Count; r++)
            {
                var row = new S.Row { RowIndex = (uint)r + 1 };
                for (int c = 0; c < rows[r].Length; c++)
                {
                    string text = rows[r][c];
                    if (text.Length == 0) continue;
                    string reference = ColumnName(c) + (r + 1);
                    row.Append(PdfText.AsNumber(text) is double n
                        ? new S.Cell { CellReference = reference, CellValue = new S.CellValue(n), DataType = S.CellValues.Number }
                        : new S.Cell { CellReference = reference, DataType = S.CellValues.InlineString, InlineString = new S.InlineString(new S.Text(text)) });
                }
                data.Append(row);
            }
            worksheet.Append(data);
            part.Worksheet = worksheet;
            list.Append(new S.Sheet { Id = book.GetIdOfPart(part), SheetId = (uint)i + 1, Name = $"Page {i + 1}" });
        }
        book.Workbook.Save();
    }

    public static string ColumnName(int index)
    {
        string name = "";
        for (int n = index + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }

    // ------------------------------------------------------------------ PowerPoint

    private const long EmuPerPoint = 12700;

    /// <summary>
    /// A presentation with one full-slide picture per page. The slide size follows the first page (clamped to what
    /// PowerPoint allows); pages of another shape are fitted and centred.
    /// </summary>
    public static void WritePictureDeck(Stream stream, IReadOnlyList<(byte[] Png, double WidthPt, double HeightPt)> pages)
    {
        if (pages.Count == 0) throw new InvalidDataException("The PDF has no pages.");
        long cx = Math.Clamp((long)(pages[0].WidthPt * EmuPerPoint), 914400, 51206400);
        long cy = Math.Clamp((long)(pages[0].HeightPt * EmuPerPoint), 914400, 51206400);

        using var doc = PresentationDocument.Create(stream, PresentationDocumentType.Presentation);
        var presentation = doc.AddPresentationPart();
        presentation.Presentation = new P.Presentation();

        var master = presentation.AddNewPart<SlideMasterPart>("rId1");
        var theme = master.AddNewPart<ThemePart>("rId5");
        theme.Theme = Theme();
        var layout = master.AddNewPart<SlideLayoutPart>("rId1");
        layout.SlideLayout = new P.SlideLayout(new P.CommonSlideData(EmptyTree()) { Name = "Blank" }, new P.ColorMapOverride(new A.MasterColorMapping()))
            { Type = P.SlideLayoutValues.Blank };
        layout.AddPart(master, "rId1");
        master.SlideMaster = new P.SlideMaster(
            new P.CommonSlideData(EmptyTree()),
            new P.ColorMap
            {
                Background1 = A.ColorSchemeIndexValues.Light1, Text1 = A.ColorSchemeIndexValues.Dark1, Background2 = A.ColorSchemeIndexValues.Light2,
                Text2 = A.ColorSchemeIndexValues.Dark2, Accent1 = A.ColorSchemeIndexValues.Accent1, Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3, Accent4 = A.ColorSchemeIndexValues.Accent4, Accent5 = A.ColorSchemeIndexValues.Accent5,
                Accent6 = A.ColorSchemeIndexValues.Accent6, Hyperlink = A.ColorSchemeIndexValues.Hyperlink, FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink,
            },
            new P.SlideLayoutIdList(new P.SlideLayoutId { Id = 2147483649U, RelationshipId = "rId1" }),
            new P.TextStyles(new P.TitleStyle(), new P.BodyStyle(), new P.OtherStyle()));

        var slideIds = new P.SlideIdList();
        for (int i = 0; i < pages.Count; i++)
        {
            var slide = presentation.AddNewPart<SlidePart>($"rId{i + 10}");
            slide.AddPart(layout, "rId1");
            var image = slide.AddImagePart(ImagePartType.Png, "rId2");
            using (var ms = new MemoryStream(pages[i].Png)) image.FeedData(ms);

            // Fit the page inside the slide, keeping its shape.
            double k = Math.Min(cx / (pages[i].WidthPt * EmuPerPoint), cy / (pages[i].HeightPt * EmuPerPoint));
            long w = (long)(pages[i].WidthPt * EmuPerPoint * k), h = (long)(pages[i].HeightPt * EmuPerPoint * k);
            var picture = new P.Picture(
                new P.NonVisualPictureProperties(
                    new P.NonVisualDrawingProperties { Id = 2U, Name = $"Page {i + 1}" },
                    new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true }),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.BlipFill(new A.Blip { Embed = "rId2" }, new A.Stretch(new A.FillRectangle())),
                new P.ShapeProperties(
                    new A.Transform2D(new A.Offset { X = (cx - w) / 2, Y = (cy - h) / 2 }, new A.Extents { Cx = w, Cy = h }),
                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
            var tree = EmptyTree();
            tree.Append(picture);
            slide.Slide = new P.Slide(new P.CommonSlideData(tree), new P.ColorMapOverride(new A.MasterColorMapping()));
            slideIds.Append(new P.SlideId { Id = (uint)(256 + i), RelationshipId = $"rId{i + 10}" });
        }

        presentation.Presentation.Append(
            new P.SlideMasterIdList(new P.SlideMasterId { Id = 2147483648U, RelationshipId = "rId1" }),
            slideIds,
            new P.SlideSize { Cx = (int)cx, Cy = (int)cy },
            new P.NotesSize { Cx = 6858000, Cy = 9144000 },
            new P.DefaultTextStyle());
        presentation.Presentation.Save();
    }

    private static P.ShapeTree EmptyTree() => new(
        new P.NonVisualGroupShapeProperties(
            new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
            new P.NonVisualGroupShapeDrawingProperties(),
            new P.ApplicationNonVisualDrawingProperties()),
        new P.GroupShapeProperties(new A.TransformGroup()));

    /// <summary>The smallest complete theme PowerPoint accepts: Office colours, Calibri, plain fills and lines.</summary>
    private static A.Theme Theme()
    {
        static A.SolidFill Scheme() => new(new A.SchemeColor { Val = A.SchemeColorValues.PhColor });
        static A.Outline Line(int w) => new(Scheme()) { Width = w };
        static A.EffectStyle Effect() => new(new A.EffectList());
        return new A.Theme(
            new A.ThemeElements(
                new A.ColorScheme(
                    new A.Dark1Color(new A.SystemColor { Val = A.SystemColorValues.WindowText, LastColor = "000000" }),
                    new A.Light1Color(new A.SystemColor { Val = A.SystemColorValues.Window, LastColor = "FFFFFF" }),
                    new A.Dark2Color(new A.RgbColorModelHex { Val = "1F2827" }),
                    new A.Light2Color(new A.RgbColorModelHex { Val = "E6EFEF" }),
                    new A.Accent1Color(new A.RgbColorModelHex { Val = "0EA5A4" }),
                    new A.Accent2Color(new A.RgbColorModelHex { Val = "0A7C7B" }),
                    new A.Accent3Color(new A.RgbColorModelHex { Val = "2DD4CF" }),
                    new A.Accent4Color(new A.RgbColorModelHex { Val = "5B4BFF" }),
                    new A.Accent5Color(new A.RgbColorModelHex { Val = "D61F69" }),
                    new A.Accent6Color(new A.RgbColorModelHex { Val = "16A34A" }),
                    new A.Hyperlink(new A.RgbColorModelHex { Val = "0A7C7B" }),
                    new A.FollowedHyperlinkColor(new A.RgbColorModelHex { Val = "5B4BFF" })) { Name = "Chango" },
                new A.FontScheme(
                    new A.MajorFont(new A.LatinFont { Typeface = "Calibri Light" }, new A.EastAsianFont { Typeface = "" }, new A.ComplexScriptFont { Typeface = "" }),
                    new A.MinorFont(new A.LatinFont { Typeface = "Calibri" }, new A.EastAsianFont { Typeface = "" }, new A.ComplexScriptFont { Typeface = "" }))
                    { Name = "Chango" },
                new A.FormatScheme(
                    new A.FillStyleList(Scheme(), Scheme(), Scheme()),
                    new A.LineStyleList(Line(9525), Line(25400), Line(38100)),
                    new A.EffectStyleList(Effect(), Effect(), Effect()),
                    new A.BackgroundFillStyleList(Scheme(), Scheme(), Scheme())) { Name = "Chango" }),
            new A.ObjectDefaults(),
            new A.ExtraColorSchemeList()) { Name = "Chango" };
    }
}
