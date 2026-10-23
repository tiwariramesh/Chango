// Flipr's entry in File Explorer's right-click menu: a minimal IExplorerCommand.
//
// Explorer loads this DLL only to show "Flipr" in the menu. Choosing it hands the selected paths to the running
// Flipr (WM_COPYDATA to its message window), starting Flipr first if needed. Nothing else happens in
// Explorer: no file is opened or read here, and the work all runs in Flipr itself.
//
// Registered by the Microsoft Store package's manifest (Windows 11's main menu) or, for the GitHub version, by
// Flipr under HKEY_CURRENT_USER (the classic menu). Built by build/shell/build-shell.ps1 (static CRT, x64).

#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <appmodel.h>
#include <new>
#include <string>
#include <vector>

// {29BADCB7-5BF2-4D63-84F8-67D64C5194F5}
static const CLSID CLSID_FliprCommand = { 0x29badcb7, 0x5bf2, 0x4d63, { 0x84, 0xf8, 0x67, 0xd6, 0x4c, 0x51, 0x94, 0xf5 } };

static const wchar_t* const MessageWindowTitle = L"Flipr.MessageWindow"; // Flipr's hidden message window
static const ULONG_PTR OpenFilesMessage = 0x0D0F;                           // COPYDATASTRUCT.dwData: "open these"
static const DWORD MaxItems = 500;

static HMODULE g_module = nullptr;
static LONG g_refs = 0; // live objects, plus hand-overs still running: the DLL stays loaded until they finish

static std::wstring ModuleDirectory()
{
    wchar_t path[MAX_PATH * 4];
    DWORD n = GetModuleFileNameW(g_module, path, ARRAYSIZE(path));
    std::wstring s(path, n);
    size_t slash = s.find_last_of(L'\\');
    return slash == std::wstring::npos ? s : s.substr(0, slash);
}

// Settings → Shortcuts → "Show in the right-click menu" (Flipr writes 0 when it's off).
static bool EnabledInSettings()
{
    DWORD value = 1, size = sizeof(value);
    if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\Flipr", L"ContextMenu", RRF_RT_REG_DWORD, nullptr, &value, &size) != ERROR_SUCCESS)
        return true;
    return value != 0;
}

static std::vector<std::wstring> PathsOf(IShellItemArray* items)
{
    std::vector<std::wstring> paths;
    DWORD count = 0;
    if (!items || FAILED(items->GetCount(&count))) return paths;
    for (DWORD i = 0; i < count && i < MaxItems; i++)
    {
        IShellItem* item = nullptr;
        if (FAILED(items->GetItemAt(i, &item))) continue;
        PWSTR path = nullptr;
        if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path)
        {
            paths.emplace_back(path);
            CoTaskMemFree(path);
        }
        item->Release();
    }
    return paths;
}

static bool SendPaths(const std::vector<std::wstring>& paths)
{
    HWND window = FindWindowW(nullptr, MessageWindowTitle);
    if (!window) return false;
    std::wstring payload;
    for (const auto& p : paths) { payload += p; payload.push_back(L'\0'); }
    COPYDATASTRUCT data{ OpenFilesMessage, static_cast<DWORD>((payload.size() + 1) * sizeof(wchar_t)), payload.data() };
    DWORD_PTR result = 0;
    AllowSetForegroundWindow(ASFW_ANY); // the wheel opens in front
    return SendMessageTimeoutW(window, WM_COPYDATA, 0, reinterpret_cast<LPARAM>(&data), SMTO_ABORTIFHUNG, 5000, &result) && result;
}

// Starts Flipr quietly: through its package when it's the Store version (so it runs as itself), otherwise the
// Flipr.exe next to this DLL.
static bool StartFlipr()
{
    UINT32 length = 0;
    if (GetCurrentPackageFamilyName(&length, nullptr) == ERROR_INSUFFICIENT_BUFFER)
    {
        std::wstring family(length, L'\0');
        if (GetCurrentPackageFamilyName(&length, family.data()) == ERROR_SUCCESS)
        {
            family.resize(wcslen(family.c_str()));
            std::wstring aumid = family + L"!Flipr";
            IApplicationActivationManager* manager = nullptr;
            bool ok = false;
            if (SUCCEEDED(CoCreateInstance(CLSID_ApplicationActivationManager, nullptr, CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(&manager))))
            {
                DWORD pid = 0;
                ok = SUCCEEDED(manager->ActivateApplication(aumid.c_str(), L"--background", AO_NONE, &pid));
                manager->Release();
            }
            return ok;
        }
    }
    std::wstring exe = ModuleDirectory() + L"\\Flipr.exe";
    std::wstring command = L"\"" + exe + L"\" --background";
    STARTUPINFOW si{ sizeof(si) };
    PROCESS_INFORMATION pi{};
    if (!CreateProcessW(exe.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &si, &pi)) return false;
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return true;
}

struct Handover { std::vector<std::wstring> paths; };

static DWORD WINAPI HandOver(void* parameter)
{
    auto* work = static_cast<Handover*>(parameter);
    HRESULT co = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (!SendPaths(work->paths) && StartFlipr())
    {
        // Wait (up to 15 s) for Flipr to be ready, then hand the files over.
        for (int i = 0; i < 150 && !SendPaths(work->paths); i++) Sleep(100);
    }
    if (SUCCEEDED(co)) CoUninitialize();
    delete work;
    InterlockedDecrement(&g_refs);
    return 0;
}

class FliprCommand final : public IExplorerCommand
{
    LONG _refs = 1;
public:
    FliprCommand() { InterlockedIncrement(&g_refs); }
    ~FliprCommand() { InterlockedDecrement(&g_refs); }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == __uuidof(IExplorerCommand)) { *ppv = static_cast<IExplorerCommand*>(this); AddRef(); return S_OK; }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_refs); }
    IFACEMETHODIMP_(ULONG) Release() override { LONG r = InterlockedDecrement(&_refs); if (!r) delete this; return r; }

    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* name) override { return SHStrDupW(L"Flipr", name); }
    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override { return SHStrDupW((ModuleDirectory() + L"\\Flipr.exe,0").c_str(), icon); }
    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tip) override { *tip = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetCanonicalName(GUID* name) override { *name = CLSID_FliprCommand; return S_OK; }
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override { *flags = ECF_DEFAULT; return S_OK; }
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override { *commands = nullptr; return E_NOTIMPL; }

    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
    {
        *state = ECS_HIDDEN;
        if (!EnabledInSettings() || !items) return S_OK;
        DWORD count = 0;
        if (FAILED(items->GetCount(&count)) || count == 0) return S_OK;
        IShellItem* first = nullptr;
        if (SUCCEEDED(items->GetItemAt(0, &first)))
        {
            SFGAOF attributes = 0;
            if (SUCCEEDED(first->GetAttributes(SFGAO_FILESYSTEM, &attributes)) && (attributes & SFGAO_FILESYSTEM)) *state = ECS_ENABLED;
            first->Release();
        }
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        auto paths = PathsOf(items);
        if (paths.empty()) return S_OK;
        // Hand over on a thread of its own: starting Flipr can take a moment, and Explorer must never wait.
        auto* work = new (std::nothrow) Handover{ std::move(paths) };
        if (!work) return E_OUTOFMEMORY;
        InterlockedIncrement(&g_refs);
        HANDLE thread = CreateThread(nullptr, 0, HandOver, work, 0, nullptr);
        if (!thread) { delete work; InterlockedDecrement(&g_refs); return HRESULT_FROM_WIN32(GetLastError()); }
        CloseHandle(thread);
        return S_OK;
    }
};

class ClassFactory final : public IClassFactory
{
    LONG _refs = 1;
public:
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IClassFactory) { *ppv = static_cast<IClassFactory*>(this); AddRef(); return S_OK; }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_refs); }
    IFACEMETHODIMP_(ULONG) Release() override { LONG r = InterlockedDecrement(&_refs); if (!r) delete this; return r; }

    IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override
    {
        *ppv = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        auto* command = new (std::nothrow) FliprCommand();
        if (!command) return E_OUTOFMEMORY;
        HRESULT hr = command->QueryInterface(riid, ppv);
        command->Release();
        return hr;
    }
    IFACEMETHODIMP LockServer(BOOL lock) override { lock ? InterlockedIncrement(&g_refs) : InterlockedDecrement(&g_refs); return S_OK; }
};

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** ppv)
{
    *ppv = nullptr;
    if (clsid != CLSID_FliprCommand) return CLASS_E_CLASSNOTAVAILABLE;
    auto* factory = new (std::nothrow) ClassFactory();
    if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}

STDAPI DllCanUnloadNow() { return g_refs == 0 ? S_OK : S_FALSE; }

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) { g_module = module; DisableThreadLibraryCalls(module); }
    return TRUE;
}
