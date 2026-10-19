using System.Windows.Media;
using FileOrganizer.Core;
using Microsoft.Win32;

namespace FileOrganizer.Windows;

internal static class ThemeManager
{
    public static void Apply(ThemeMode theme)
    {
        var light = theme == ThemeMode.Light || (theme == ThemeMode.System && SystemIsLight());
        var values = new Dictionary<string, string>
        {
            ["Page"] = light ? "#F5F6F8" : "#191C21",
            ["Side"] = light ? "#EEF1F5" : "#20242B",
            ["Surface"] = light ? "#FFFFFF" : "#272C34",
            ["Text"] = light ? "#202936" : "#EDF1F7",
            ["Sub"] = light ? "#576576" : "#ACB7C8",
            ["Line"] = light ? "#D8DFE8" : "#444E5C",
            ["Accent"] = light ? "#075EAE" : "#95C9FF",
            ["OnAccent"] = light ? "#FFFFFF" : "#102A44",
            ["Soft"] = light ? "#DFEEFD" : "#293F57",
            ["Success"] = light ? "#1C6940" : "#85D8AB",
            ["Caution"] = light ? "#82520C" : "#F2C36B",
            ["Error"] = light ? "#AA2D34" : "#FF9D9D"
        };
        foreach (var (name, value) in values)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            System.Windows.Application.Current.Resources[name + "Brush"] = brush;
        }
        // .NET 10의 Fluent 테마로 기본 컨트롤·스크롤바도 같은 모드로 적용한다.
        // WPF0001은 이 API 사용 위치에서만 허용한다. SDK와 실행 환경은 패키지에 고정되어 있다.
#pragma warning disable WPF0001
        System.Windows.Application.Current.ThemeMode = light ? System.Windows.ThemeMode.Light : System.Windows.ThemeMode.Dark;
#pragma warning restore WPF0001
    }

    private static bool SystemIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (System.Security.SecurityException) { return true; }
    }
}
