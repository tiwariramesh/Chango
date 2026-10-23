using System.IO;
using System.Text;
using Chango.Windows.DragDrop;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Chango.Tests;

/// <summary>Reading the dragged files from apps that don't provide CF_HDROP (VS Code's Explorer and other Chromium apps).</summary>
public class DragDataTests
{
    /// <summary>Chromium's custom web data, the way Chromium writes it (base::Pickle).</summary>
    private static byte[] Pickle(params (string Type, string Value)[] entries)
    {
        var body = new MemoryStream();
        var w = new BinaryWriter(body);
        w.Write((uint)entries.Length);
        void Str(string s)
        {
            w.Write(s.Length);
            w.Write(Encoding.Unicode.GetBytes(s));
            int pad = (4 - s.Length * 2 % 4) % 4;
            for (int i = 0; i < pad; i++) w.Write((byte)0);
        }
        foreach (var (t, v) in entries) { Str(t); Str(v); }
        w.Flush();
        var all = new MemoryStream();
        new BinaryWriter(all).Write((uint)body.Length);
        body.WriteTo(all);
        return all.ToArray();
    }

    private static string TempFile(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "chango-dragtests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Parses_chromium_custom_data()
    {
        var parsed = DataObjectReader.ParseChromiumCustomData(Pickle(("codefiles", "[\"C:\\\\a.png\"]"), ("odd", "x")));
        Assert.Equal(2, parsed.Count);
        Assert.Equal(("codefiles", "[\"C:\\\\a.png\"]"), parsed[0]);
        Assert.Equal(("odd", "x"), parsed[1]);
    }

    [Fact]
    public void Truncated_custom_data_is_ignored_safely()
    {
        var bytes = Pickle(("codefiles", "[\"C:\\\\a.png\"]"));
        Assert.Empty(DataObjectReader.ParseChromiumCustomData(bytes[..10]));
        Assert.Empty(DataObjectReader.ParseChromiumCustomData([]));
    }

    [Fact]
    public void File_uris_become_paths()
    {
        var paths = DataObjectReader.PathsFromUris(["file:///c%3A/Users/me/my%20photo.png", "# comment", "https://example.com/x.png", ""]);
        Assert.Equal([@"c:\Users\me\my photo.png"], paths);
    }

    [Fact]
    public void Text_is_a_file_list_only_when_every_line_is_an_existing_full_path()
    {
        Func<string, bool> exists = p => p is @"C:\a.png" or @"C:\b.png";
        Assert.Equal([@"C:\a.png", @"C:\b.png"], DataObjectReader.PathsFromText("C:\\a.png\r\nC:\\b.png\r\n", exists));
        Assert.Empty(DataObjectReader.PathsFromText("C:\\a.png\r\nhello", exists));
        Assert.Empty(DataObjectReader.PathsFromText("a.png", _ => true)); // relative
        Assert.Empty(DataObjectReader.PathsFromText("", exists));
    }

    [Fact]
    public void Reads_vs_code_style_drag_without_cf_hdrop()
    {
        string a = TempFile("photo.png"), b = TempFile("notes.txt");
        string json = System.Text.Json.JsonSerializer.Serialize(new[] { a, b });
        var data = new System.Windows.DataObject();
        data.SetData("Chromium Web Custom MIME Data Format", new MemoryStream(Pickle(("codefiles", json), ("resourceurls", "[]"))));
        data.SetData(System.Windows.DataFormats.UnicodeText, "not a path");
        Assert.Equal([a, b], DataObjectReader.ReadFileDrop((ComIDataObject)data));
    }

    [Fact]
    public void Falls_back_to_paths_in_plain_text()
    {
        string a = TempFile("clip.mp4");
        var data = new System.Windows.DataObject();
        data.SetData(System.Windows.DataFormats.UnicodeText, a + "\r\n");
        Assert.Equal([a], DataObjectReader.ReadFileDrop((ComIDataObject)data));
    }

    [Fact]
    public void Plain_text_that_is_not_a_path_gives_no_files()
    {
        var data = new System.Windows.DataObject();
        data.SetData(System.Windows.DataFormats.UnicodeText, "const x = 1;");
        Assert.Empty(DataObjectReader.ReadFileDrop((ComIDataObject)data));
    }

    /// <summary>What VS Code's drag looked like on a real PC: CF_HDROP is listed, but asking for it fails.</summary>
    private sealed class BrokenHDrop(ComIDataObject inner) : ComIDataObject
    {
        private const short CF_HDROP = 15;
        public void GetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC f, out System.Runtime.InteropServices.ComTypes.STGMEDIUM m)
        {
            if (f.cfFormat == CF_HDROP) throw new System.Runtime.InteropServices.COMException("Invalid FORMATETC structure", unchecked((int)0x80040064));
            inner.GetData(ref f, out m);
        }
        public int QueryGetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC f) => f.cfFormat == CF_HDROP ? 0 : inner.QueryGetData(ref f);
        public void GetDataHere(ref System.Runtime.InteropServices.ComTypes.FORMATETC f, ref System.Runtime.InteropServices.ComTypes.STGMEDIUM m) => inner.GetDataHere(ref f, ref m);
        public int GetCanonicalFormatEtc(ref System.Runtime.InteropServices.ComTypes.FORMATETC i, out System.Runtime.InteropServices.ComTypes.FORMATETC o) => inner.GetCanonicalFormatEtc(ref i, out o);
        public void SetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC f, ref System.Runtime.InteropServices.ComTypes.STGMEDIUM m, bool release) => inner.SetData(ref f, ref m, release);
        public System.Runtime.InteropServices.ComTypes.IEnumFORMATETC EnumFormatEtc(System.Runtime.InteropServices.ComTypes.DATADIR d) => inner.EnumFormatEtc(d);
        public int DAdvise(ref System.Runtime.InteropServices.ComTypes.FORMATETC f, System.Runtime.InteropServices.ComTypes.ADVF a, System.Runtime.InteropServices.ComTypes.IAdviseSink s, out int c) => inner.DAdvise(ref f, a, s, out c);
        public void DUnadvise(int c) => inner.DUnadvise(c);
        public int EnumDAdvise(out System.Runtime.InteropServices.ComTypes.IEnumSTATDATA? e) => inner.EnumDAdvise(out e);
    }

    [Fact]
    public void A_failing_cf_hdrop_does_not_hide_the_other_formats()
    {
        string a = TempFile("Bahrain Chamber Template.docx");
        var data = new System.Windows.DataObject();
        data.SetData(System.Windows.DataFormats.UnicodeText, a);
        Assert.Equal([a], DataObjectReader.ReadFileDrop(new BrokenHDrop((ComIDataObject)data)));
    }

    /// <summary>VS Code's Explorer, as its source writes it: text paths (files and folders), ResourceURLs (files only),
    /// and a DownloadURL whose CF_HDROP can't be read.</summary>
    [Fact]
    public void Reads_vs_code_explorer_drag_with_a_folder()
    {
        string file = TempFile("Bahrain Chamber Template.docx");
        string folder = Path.GetDirectoryName(file)!;
        var data = new System.Windows.DataObject();
        data.SetData(System.Windows.DataFormats.UnicodeText, file + "\r\n" + folder);
        string uris = System.Text.Json.JsonSerializer.Serialize(new[] { new Uri(file).AbsoluteUri });
        data.SetData("Chromium Web Custom MIME Data Format", new MemoryStream(Pickle(("resourceurls", uris), ("downloadurl", "application/octet-stream:x:file:///x"))));
        Assert.Equal([file, folder], DataObjectReader.ReadFileDrop(new BrokenHDrop((ComIDataObject)data)));
    }

    [Fact]
    public void Uses_resource_urls_when_the_text_is_not_a_path_list()
    {
        string file = TempFile("deck.pdf");
        var data = new System.Windows.DataObject();
        data.SetData(System.Windows.DataFormats.UnicodeText, "deck.pdf");
        string uris = System.Text.Json.JsonSerializer.Serialize(new[] { "file:///" + file.Replace('\\', '/').Replace(":", "%3A").Replace(" ", "%20") });
        data.SetData("Chromium Web Custom MIME Data Format", new MemoryStream(Pickle(("resourceurls", uris))));
        Assert.Equal([file], DataObjectReader.ReadFileDrop(new BrokenHDrop((ComIDataObject)data)));
    }

    [Fact]
    public void Vs_code_file_links_become_paths()
    {
        Assert.Equal([@"c:\Users\me\a b.png"], DataObjectReader.PathsFromUris(["vscode-file://vscode-app/c%3A/Users/me/a%20b.png"]));
    }

    [Fact]
    public void Explorer_file_drop_still_wins()
    {
        string a = TempFile("a.jpg");
        var data = new System.Windows.DataObject();
        data.SetData(System.Windows.DataFormats.FileDrop, new[] { a });
        Assert.Equal([a], DataObjectReader.ReadFileDrop((ComIDataObject)data));
    }
}
