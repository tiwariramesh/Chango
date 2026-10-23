// FliprCdr.exe: reads CorelDRAW drawings (.cdr, and Corel's .cmx) and writes each page as an SVG.
//
//   FliprCdr.exe <drawing> <output folder>
//
// Writes page-1.svg, page-2.svg … and prints how many. Exit codes: 0 done; 1 bad arguments or a file problem;
// 2 not a CorelDRAW drawing it can read (or encrypted); 3 the drawing couldn't be read.
//
// Built on libcdr and librevenge (MPL 2.0, The Document Foundation: the same reader Inkscape and LibreOffice use),
// with lcms2 (MIT), zlib (zlib licence) and Windows' own ICU. See build/cdr/build-cdr.ps1 and THIRD-PARTY-NOTICES.md.
//
// Flipr runs this as a separate process with a time limit, so a damaged or hostile file can only ever fail its
// own conversion: it can't crash or freeze Flipr.

#include <windows.h>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include <librevenge/librevenge.h>
#include <librevenge-stream/librevenge-stream.h>
#include <libcdr/libcdr.h>

static const unsigned long long MaxInput = 512ull * 1024 * 1024;

// The whole file in memory: works with any file name (librevenge's own file stream takes narrow paths only).
static bool ReadAll(const wchar_t* path, std::vector<unsigned char>& data)
{
    HANDLE file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER size{};
    bool ok = GetFileSizeEx(file, &size) && size.QuadPart > 0 && static_cast<unsigned long long>(size.QuadPart) <= MaxInput;
    if (ok)
    {
        data.resize(static_cast<size_t>(size.QuadPart));
        size_t done = 0;
        while (ok && done < data.size())
        {
            DWORD chunk = static_cast<DWORD>(std::min<size_t>(data.size() - done, 64u * 1024 * 1024)), read = 0;
            ok = ReadFile(file, data.data() + done, chunk, &read, nullptr) && read > 0;
            done += read;
        }
    }
    CloseHandle(file);
    return ok;
}

static bool WriteAll(const std::wstring& path, const char* text, size_t length)
{
    FILE* f = _wfopen(path.c_str(), L"wb");
    if (!f) return false;
    static const char header[] = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";
    bool ok = fwrite(header, 1, sizeof(header) - 1, f) == sizeof(header) - 1 && fwrite(text, 1, length, f) == length;
    return fclose(f) == 0 && ok;
}

int wmain(int argc, wchar_t** argv)
{
    // Never show Windows' crash or "insert disk" dialogs: a failure is just an exit code.
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    if (argc != 3) { fputs("Usage: FliprCdr <drawing> <output folder>\n", stderr); return 1; }

    std::vector<unsigned char> data;
    if (!ReadAll(argv[1], data)) { fputs("Can't read the file.\n", stderr); return 1; }

    librevenge::RVNGStringStream input(data.data(), static_cast<unsigned>(data.size()));
    librevenge::RVNGStringVector pages;
    librevenge::RVNGSVGDrawingGenerator generator(pages, ""); // plain <svg>, no namespace prefix
    bool parsed = false;
    try
    {
        if (libcdr::CDRDocument::isSupported(&input)) parsed = libcdr::CDRDocument::parse(&input, &generator);
        else if (libcdr::CMXDocument::isSupported(&input)) parsed = libcdr::CMXDocument::parse(&input, &generator);
        else { fputs("Not a CorelDRAW drawing this version can read, or it's encrypted.\n", stderr); return 2; }
    }
    catch (...) { parsed = false; }
    if (!parsed || pages.empty()) { fputs("The drawing couldn't be read.\n", stderr); return 3; }

    std::wstring folder = argv[2];
    for (unsigned i = 0; i < pages.size(); i++)
    {
        std::wstring out = folder + L"\\page-" + std::to_wstring(i + 1) + L".svg";
        if (!WriteAll(out, pages[i].cstr(), pages[i].size())) { fputs("Can't write the result.\n", stderr); return 1; }
    }
    printf("%u\n", pages.size());
    return 0;
}
