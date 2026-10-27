using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Chagok.Core;
using Microsoft.Win32;

namespace Chagok.Terminal;

public static class PathCommand
{
    public static int Run(Arguments args)
    {
        args.Allow();
        if (args.Words.Count != 2 || args.Words[1] is not ("add" or "remove" or "status"))
            throw new ArgumentException("사용법: Chagok.exe path add|remove|status");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("PATH 등록·해제·조회는 Windows 배포본 Chagok.exe에서 지원합니다.");
        return RunWindows(args);
    }

    [SupportedOSPlatform("windows")]
    private static int RunWindows(Arguments args)
    {
        var executable = Environment.ProcessPath ?? throw new IOException("실행 파일 위치를 확인할 수 없습니다.");
        if (!string.Equals(Path.GetFileName(executable), "Chagok.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("고정 이름의 Windows 배포본 Chagok.exe에서 실행하세요.");
        var directory = Path.GetDirectoryName(executable)!;
        var action = args.Words[1];
        using var key = action == "add" ? Registry.CurrentUser.CreateSubKey("Environment")
            : Registry.CurrentUser.OpenSubKey("Environment", writable: action != "status");
        if (key is null) throw new IOException("사용자 환경 변수 저장 위치를 열 수 없습니다.");
        var raw = key.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is not null and not string) throw new IOException("사용자 PATH의 형식을 확인할 수 없어 변경하지 않았습니다.");
        var before = raw as string ?? "";
        var kind = raw is null ? RegistryValueKind.ExpandString : key.GetValueKind("Path");
        if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
            throw new IOException("지원하지 않는 사용자 PATH 형식입니다. 기존 값은 보존했습니다.");
        var after = action == "status" ? before : UserPath.Update(before, directory, action == "add");
        var changed = before != after;
        if (changed)
        {
            if (!Equals(raw, key.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames)))
                throw new IOException("다른 프로그램이 사용자 PATH를 변경했습니다. 다시 실행하세요.");
            key.SetValue("Path", after, kind);
            // 기존 터미널의 환경은 바뀌지 않는다. 새 프로세스가 변경을 읽도록 알린다.
            _ = SendMessageTimeout(new IntPtr(0xffff), 0x001a, IntPtr.Zero, "Environment", 0x0002, 1000, out _);
        }
        var registered = UserPath.Contains(after, directory);
        var message = action switch
        {
            "add" => changed ? "사용자 PATH에 등록했습니다. 터미널 앱을 완전히 종료한 뒤 다시 열어 chagok tui를 실행하세요."
                : "이미 사용자 PATH에 등록되어 있습니다.",
            "remove" => changed ? "사용자 PATH에서 해제했습니다. 변경은 새로 시작한 터미널 앱에 적용됩니다."
                : "사용자 PATH에 등록되어 있지 않습니다.",
            _ => registered ? "현재 실행 파일 폴더가 사용자 PATH에 등록되어 있습니다." : "현재 실행 파일 폴더가 사용자 PATH에 등록되어 있지 않습니다."
        };
        Console.WriteLine(args.Json ? JsonSerializer.Serialize(new { command = "chagok", directory, registered, changed, message }) : message + "\n" + directory);
        return 0;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam,
        string lParam, uint flags, uint timeout, out IntPtr result);
}
