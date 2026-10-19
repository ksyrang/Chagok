#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shobjidl.h>
inline void* operator new(size_t, void* storage) noexcept { return storage; }

// 패키지 manifest의 CLSID와 함께 유지한다.
static const CLSID CommandId = {0xfd9b4304,0xac43,0x4c53,{0x84,0x25,0x1b,0x0a,0x14,0xa6,0x25,0x59}};
static LONG objects = 0;
static LONG locks = 0;

static HRESULT CopyText(const wchar_t* text, wchar_t** result)
{
    if (!result) return E_POINTER;
    const SIZE_T bytes = (wcslen(text) + 1) * sizeof(wchar_t);
    *result = static_cast<wchar_t*>(CoTaskMemAlloc(bytes));
    if (!*result) return E_OUTOFMEMORY;
    CopyMemory(*result, text, bytes); return S_OK;
}

static HRESULT ReadExecutable(wchar_t* value, DWORD count)
{
    DWORD bytes = count * sizeof(wchar_t);
    const LSTATUS status = RegGetValueW(HKEY_CURRENT_USER, L"Software\\ksyrang\\Chagok\\Shell", L"ExecutablePath",
        RRF_RT_REG_SZ, nullptr, value, &bytes);
    if (status != ERROR_SUCCESS) return HRESULT_FROM_WIN32(status);
    return value[0] ? S_OK : E_FAIL;
}

// CreateProcess의 따옴표/역슬래시 규칙을 적용한다. 셸 명령 해석기를 사용하지 않는다.
static wchar_t* QuoteArgument(const wchar_t* value)
{
    const SIZE_T length = wcslen(value);
    wchar_t* result = static_cast<wchar_t*>(HeapAlloc(GetProcessHeap(), 0, (length * 2 + 3) * sizeof(wchar_t)));
    if (!result) return nullptr;
    SIZE_T output = 0, slashes = 0; result[output++] = L'"';
    for (SIZE_T i = 0; i <= length; ++i)
    {
        const wchar_t c = value[i];
        if (c == L'\\') { ++slashes; continue; }
        const SIZE_T repeat = (c == L'"' || c == 0) ? slashes * 2 : slashes;
        for (SIZE_T j = 0; j < repeat; ++j) result[output++] = L'\\';
        slashes = 0;
        if (c == L'"') result[output++] = L'\\';
        if (c == 0) break;
        result[output++] = c;
    }
    result[output++] = L'"'; result[output] = 0; return result;
}

class ShellCommand final : public IExplorerCommand
{
    LONG references = 1;
public:
    ShellCommand() { InterlockedIncrement(&objects); }
    ~ShellCommand() { InterlockedDecrement(&objects); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** value) override
    {
        if (!value) return E_POINTER; *value = nullptr;
        if (IsEqualIID(id, IID_IUnknown) || IsEqualIID(id, IID_IExplorerCommand))
        { *value = static_cast<IExplorerCommand*>(this); AddRef(); return S_OK; }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&references); }
    ULONG STDMETHODCALLTYPE Release() override
    { const LONG count = InterlockedDecrement(&references); if (!count) { this->~ShellCommand(); HeapFree(GetProcessHeap(), 0, this); } return count; }
    HRESULT STDMETHODCALLTYPE GetTitle(IShellItemArray*, LPWSTR* value) override { return CopyText(L"차곡에 추가", value); }
    HRESULT STDMETHODCALLTYPE GetIcon(IShellItemArray*, LPWSTR* value) override
    {
        if (!value) return E_POINTER; *value = nullptr;
        wchar_t executable[32768]; HRESULT result = ReadExecutable(executable, ARRAYSIZE(executable)); if (FAILED(result)) return result;
        wchar_t* quoted = QuoteArgument(executable); if (!quoted) return E_OUTOFMEMORY;
        *value = static_cast<wchar_t*>(CoTaskMemAlloc((wcslen(quoted) + 3) * sizeof(wchar_t)));
        if (*value) { wcscpy(*value, quoted); wcscat(*value, L",0"); result = S_OK; }
        else result = E_OUTOFMEMORY;
        HeapFree(GetProcessHeap(), 0, quoted); return result;
    }
    HRESULT STDMETHODCALLTYPE GetToolTip(IShellItemArray*, LPWSTR* value) override { return CopyText(L"이 폴더로 이동하는 차곡 규칙을 추가합니다", value); }
    HRESULT STDMETHODCALLTYPE GetCanonicalName(GUID* value) override { if (!value) return E_POINTER; *value = CommandId; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* value) override
    {
        if (!value) return E_POINTER; *value = ECS_HIDDEN;
        DWORD count = 0; if (!items || FAILED(items->GetCount(&count)) || count != 1) return S_OK;
        SFGAOF attributes = 0;
        if (SUCCEEDED(items->GetAttributes(SIATTRIBFLAGS_AND, SFGAO_FOLDER | SFGAO_FILESYSTEM, &attributes))
            && (attributes & (SFGAO_FOLDER | SFGAO_FILESYSTEM)) == (SFGAO_FOLDER | SFGAO_FILESYSTEM)) *value = ECS_ENABLED;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Invoke(IShellItemArray* items, IBindCtx*) override
    {
        EXPCMDSTATE state; if (FAILED(GetState(items, FALSE, &state)) || state != ECS_ENABLED) return E_INVALIDARG;
        IShellItem* item = nullptr; HRESULT result = items->GetItemAt(0, &item); if (FAILED(result)) return result;
        LPWSTR destination = nullptr; result = item->GetDisplayName(SIGDN_FILESYSPATH, &destination); item->Release();
        if (FAILED(result)) return result;
        wchar_t executable[32768]; result = ReadExecutable(executable, ARRAYSIZE(executable));
        if (FAILED(result)) { CoTaskMemFree(destination); return result; }
        wchar_t* programArg = QuoteArgument(executable); wchar_t* destinationArg = QuoteArgument(destination); CoTaskMemFree(destination);
        if (!programArg || !destinationArg)
        { if (programArg) HeapFree(GetProcessHeap(), 0, programArg); if (destinationArg) HeapFree(GetProcessHeap(), 0, destinationArg); return E_OUTOFMEMORY; }
        const SIZE_T length = wcslen(programArg) + wcslen(destinationArg) + 14;
        wchar_t* command = static_cast<wchar_t*>(HeapAlloc(GetProcessHeap(), 0, length * sizeof(wchar_t)));
        if (!command) result = E_OUTOFMEMORY;
        else
        {
            wcscpy(command, programArg); wcscat(command, L" --add-rule "); wcscat(command, destinationArg);
            STARTUPINFOW startup = {}; startup.cb = sizeof(startup); PROCESS_INFORMATION process = {};
            if (CreateProcessW(executable, command, nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup, &process))
            { CloseHandle(process.hThread); CloseHandle(process.hProcess); result = S_OK; }
            else result = HRESULT_FROM_WIN32(GetLastError());
            HeapFree(GetProcessHeap(), 0, command);
        }
        HeapFree(GetProcessHeap(), 0, programArg); HeapFree(GetProcessHeap(), 0, destinationArg); return result;
    }
    HRESULT STDMETHODCALLTYPE GetFlags(EXPCMDFLAGS* value) override { if (!value) return E_POINTER; *value = ECF_DEFAULT; return S_OK; }
    HRESULT STDMETHODCALLTYPE EnumSubCommands(IEnumExplorerCommand** value) override { if (value) *value = nullptr; return E_NOTIMPL; }
};

class CommandFactory final : public IClassFactory
{
    LONG references = 1;
public:
    CommandFactory() { InterlockedIncrement(&objects); }
    ~CommandFactory() { InterlockedDecrement(&objects); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** value) override
    {
        if (!value) return E_POINTER; *value = nullptr;
        if (IsEqualIID(id, IID_IUnknown) || IsEqualIID(id, IID_IClassFactory))
        { *value = static_cast<IClassFactory*>(this); AddRef(); return S_OK; }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&references); }
    ULONG STDMETHODCALLTYPE Release() override { const LONG count = InterlockedDecrement(&references); if (!count) { this->~CommandFactory(); HeapFree(GetProcessHeap(), 0, this); } return count; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID id, void** value) override
    {
        if (!value) return E_POINTER; *value = nullptr; if (outer) return CLASS_E_NOAGGREGATION;
        void* storage = HeapAlloc(GetProcessHeap(), 0, sizeof(ShellCommand)); if (!storage) return E_OUTOFMEMORY;
        auto* command = new (storage) ShellCommand();
        const HRESULT result = command->QueryInterface(id, value); command->Release(); return result;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override { if (lock) InterlockedIncrement(&locks); else InterlockedDecrement(&locks); return S_OK; }
};

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID id, REFIID interfaceId, void** value)
{
    if (!value) return E_POINTER; *value = nullptr; if (!IsEqualCLSID(id, CommandId)) return CLASS_E_CLASSNOTAVAILABLE;
    void* storage = HeapAlloc(GetProcessHeap(), 0, sizeof(CommandFactory)); if (!storage) return E_OUTOFMEMORY;
    auto* factory = new (storage) CommandFactory();
    const HRESULT result = factory->QueryInterface(interfaceId, value); factory->Release(); return result;
}
extern "C" HRESULT __stdcall DllCanUnloadNow() { return objects == 0 && locks == 0 ? S_OK : S_FALSE; }
BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
