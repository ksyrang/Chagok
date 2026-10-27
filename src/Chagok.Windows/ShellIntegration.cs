using System.Runtime.InteropServices;
using Chagok.Core;
using Microsoft.Win32;

namespace Chagok.Windows;

internal static class ShellIntegration
{
    private const string KeyPath = @"Software\Classes\Directory\shell\Chagok.AddRule";
    private const string Marker = "15193BE9-9D6D-4D58-9A90-23910F1544AE";

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        using var command = key?.OpenSubKey("command");
        return key?.GetValue("ChagokOwner") as string == Marker && command?.GetValue("") is string;
    }

    private static void AssertOwned()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key is not null && key.GetValue("ChagokOwner") as string != Marker)
            throw new IOException("동일한 이름의 다른 탐색기 메뉴가 있어 변경하지 않았습니다.");
    }

    public static void EnsureRegistered()
    {
        var executable = Environment.ProcessPath ?? throw new IOException("실행 파일 위치를 확인할 수 없습니다.");
        var desired = ShellLaunch.BuildCommand(executable);
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        using var command = key?.OpenSubKey("command");
        if (key?.GetValue("ChagokOwner") as string == Marker
            && string.Equals(command?.GetValue("") as string, desired, StringComparison.OrdinalIgnoreCase)) return;
        Register();
    }

    public static void Register()
    {
        var executable = Environment.ProcessPath ?? throw new IOException("실행 파일 위치를 확인할 수 없습니다.");
        var command = ShellLaunch.BuildCommand(executable);
        AssertOwned();
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue("ChagokOwner", Marker);
        key.SetValue("", "차곡에 추가");
        key.SetValue("Icon", $"\"{executable}\",0");
        key.SetValue("MultiSelectModel", "Single");
        using var action = key.CreateSubKey("command"); action.SetValue("", command);
        NotifyShell();
    }

    public static void Unregister()
    {
        AssertOwned(); Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
        NotifyShell();
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
