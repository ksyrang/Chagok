using System.Runtime.InteropServices;

namespace Chagok.Windows;

internal static class EntryPoint
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!Chagok.Terminal.LaunchRoute.IsGui(args))
            return Chagok.Terminal.Program.Main(args).GetAwaiter().GetResult();

        // 콘솔 서브시스템으로 CLI의 파이프·종료 대기를 보장한다. GUI만 콘솔에서 분리한다.
        FreeConsole();
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();
}
