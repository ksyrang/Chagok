using System.Windows;
using FileOrganizer.Core;

namespace FileOrganizer.Windows;

public partial class App : System.Windows.Application
{
    private SessionLease? instance;
    private CancellationTokenSource? shellCancellation;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? destination;
        try { destination = ShellLaunch.ParseArguments(e.Args); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        { MessageBox.Show(ex.Message, "차곡"); Shutdown(1); return; }
        try { instance = SessionLease.Acquire(SessionLease.DefaultDirectory, "GUI"); }
        catch (SessionBusyException busy)
        {
            if (destination is null)
                MessageBox.Show(busy.Message, "차곡");
            else
            {
                try
                {
                    if (!await ShellRequestBridge.SendAsync(destination))
                        MessageBox.Show("요청을 받을 수 없습니다. 기존 차곡의 작업과 대기 요청을 확인하세요.", "차곡");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
                { MessageBox.Show("기존 차곡에 요청을 전달하지 못했습니다. 이전 버전이 실행 중이면 종료한 뒤 최신 버전을 실행하세요.\n\n" + ex.Message, "차곡"); }
            }
            Shutdown(); return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { MessageBox.Show("설정 사용 잠금을 열지 못했습니다.\n\n" + ex.Message, "차곡"); Shutdown(1); return; }
        try
        {
            var storage = new Storage(SessionLease.DefaultDirectory);
            var settings = storage.Load();
            ThemeManager.Apply(settings.Theme);
            var window = new MainWindow(storage, settings);
            MainWindow = window;
            shellCancellation = new CancellationTokenSource();
            _ = ListenForShellRequestsAsync(window, shellCancellation.Token);
            window.Show();
            if (destination is not null) window.EnqueueDestination(destination);
            if (settings.AutoRegisterExplorerMenu)
            {
                try
                {
                    ShellIntegration.EnsureRegistered();
                    await ModernShellIntegration.EnsureRegisteredAsync(shellCancellation.Token);
                }
                catch (OperationCanceledException) when (shellCancellation.IsCancellationRequested) { }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
                { window.ReportShellRegistrationError(ex.Message); }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("프로그램을 시작하지 못했습니다. 기존 설정은 보존했습니다.\n\n" + ex.Message, "차곡", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private static async Task ListenForShellRequestsAsync(MainWindow window, CancellationToken token)
    {
        try
        {
            await ShellRequestBridge.ListenAsync(path => window.Dispatcher.InvokeAsync(() => window.EnqueueDestination(path)).Task, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { if (!token.IsCancellationRequested) window.ReportShellError(ex.Message); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        shellCancellation?.Cancel(); shellCancellation?.Dispose(); instance?.Dispose();
        base.OnExit(e);
    }
}
