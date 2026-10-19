using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Diagnostics;
using FileOrganizer.Core;

namespace FileOrganizer.Windows;

internal sealed class SettingsEditor : Window
{
    public AppSettings? Result { get; private set; }

    public SettingsEditor(Window owner, AppSettings settings, string dataDirectory, Action<bool> persistAutoRegistration)
    {
        Owner = owner; Title = "설정";
        SetResourceReference(StyleProperty, typeof(Window));
        Width = 540; Height = Math.Min(520, SystemParameters.WorkArea.Height);
        MinWidth = 420; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var shell = new DockPanel { Margin = new(24) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); shell.Children.Add(actions);
        var form = new StackPanel();
        shell.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(0, 0, 12, 0) });
        Content = shell;
        form.Children.Add(new TextBlock { Text = "설정", FontSize = 25, FontWeight = FontWeights.SemiBold });
        form.Children.Add(EditorUi.Heading("화면"));
        var theme = EditorUi.Select(["시스템", "라이트", "다크"], (int)settings.Theme);
        EditorUi.Field(form, "테마", theme);
        form.Children.Add(EditorUi.Heading("창 닫기"));
        var closeAction = EditorUi.Select(["창만 닫고 백그라운드에서 실행", "프로그램 종료"], (int)settings.CloseAction);
        EditorUi.Field(form, "닫기 버튼(X)을 눌렀을 때", closeAction);
        var confirm = EditorUi.Check("종료하기 전에 확인창 표시", settings.ConfirmExitOnClose);
        form.Children.Add(confirm);
        EditorUi.Hint(form, "‘다시 묻지 않기’를 선택했다면 여기에서 종료 확인을 다시 켤 수 있습니다.");
        EditorUi.Hint(form, "백그라운드 실행을 선택하면 창을 닫아도 프로그램 감지를 계속합니다. 프로그램을 종료하면 감지도 중단됩니다. 작업 중 종료는 안전하게 중단한 뒤 처리합니다.");
        void UpdateCloseAction() => confirm.IsEnabled = closeAction.SelectedIndex == (int)WindowCloseAction.Exit;
        closeAction.SelectionChanged += (_, _) => UpdateCloseAction(); UpdateCloseAction();
        form.Children.Add(EditorUi.Heading("탐색기 연동"));
        var autoShell = EditorUi.Check("차곡 시작 시 탐색기 메뉴 확인·등록", settings.AutoRegisterExplorerMenu);
        form.Children.Add(autoShell);
        var shellStatus = new TextBlock { Margin = new(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
        shellStatus.SetResourceReference(TextBlock.ForegroundProperty, "SubBrush"); form.Children.Add(shellStatus);
        var shellActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 10) };
        var shellBusy = false;
        Closing += (_, e) => { if (shellBusy) e.Cancel = true; };
        void RefreshShellStatus()
        {
            try { shellStatus.Text = (ShellIntegration.IsRegistered() ? "기존 ‘차곡에 추가’ 메뉴가 등록되어 있습니다." : "기존 탐색기 메뉴가 등록되어 있지 않습니다.") + "\n" + ModernShellIntegration.Status; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { shellStatus.Text = "메뉴 상태를 확인하지 못했습니다: " + ex.Message; }
        }
        async Task ChangeShellAsync(bool register)
        {
            if (shellBusy) return;
            shellBusy = true; form.IsEnabled = false; actions.IsEnabled = false;
            shellStatus.Text = "탐색기 연동을 처리하고 있습니다. 최대 30초 정도 기다려 주세요…";
            try
            {
                if (register) { ShellIntegration.Register(); await ModernShellIntegration.EnsureRegisteredAsync(); }
                else
                {
                    persistAutoRegistration(false); autoShell.IsChecked = false;
                    await ModernShellIntegration.UnregisterAsync();
                    ShellIntegration.Unregister();
                }
                RefreshShellStatus();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException or Win32Exception)
            { EditorUi.Error(this, ex); }
            finally { shellBusy = false; form.IsEnabled = true; actions.IsEnabled = true; RefreshShellStatus(); }
        }
        var registerShell = EditorUi.Button("메뉴 등록 / 경로 갱신", async (_, _) => await ChangeShellAsync(true)); registerShell.Margin = new(0, 0, 8, 0);
        shellActions.Children.Add(registerShell);
        shellActions.Children.Add(EditorUi.Button("메뉴 해제", async (_, _) => await ChangeShellAsync(false)));
        form.Children.Add(shellActions); RefreshShellStatus();
        EditorUi.Hint(form, "등록·해제는 즉시 적용됩니다. 메뉴 해제 시 자동 등록도 끄고 저장합니다. 첫 메뉴 연동은 x64 Windows 11의 개발자 모드에서 테스트할 수 있습니다. 차곡은 개발자 모드나 인증서 신뢰를 자동으로 바꾸지 않습니다.");
        var developerSettings = EditorUi.Button("Windows 개발자 설정 열기", (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:developers") { UseShellExecute = true }); }
            catch (Exception ex) when (ex is Win32Exception or IOException) { EditorUi.Error(this, ex); }
        });
        developerSettings.Margin = new(0, 0, 0, 12); developerSettings.HorizontalAlignment = HorizontalAlignment.Left; form.Children.Add(developerSettings);
        form.Children.Add(EditorUi.Heading("앱 정보"));
        form.Children.Add(new TextBlock { Text = "차곡 · by ksyrang", FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 6) });
        var version = typeof(SettingsEditor).Assembly.GetName().Version?.ToString(3) ?? "확인 불가";
        EditorUi.Hint(form, $"ver. {version}");
        var openData = EditorUi.Button("데이터 폴더 열기", (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(dataDirectory);
                Process.Start(new ProcessStartInfo(dataDirectory) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            { EditorUi.Error(this, ex); }
        });
        openData.Margin = new(0, 0, 0, 8);
        openData.HorizontalAlignment = HorizontalAlignment.Left;
        form.Children.Add(openData);
        EditorUi.Hint(form, "폴더·규칙 설정과 실행 기록을 저장한 위치를 엽니다.");
        var cancel = EditorUi.Button("취소", (_, _) => Close()); cancel.IsCancel = true;
        actions.Children.Add(cancel);
        actions.Children.Add(EditorUi.Button("저장", (_, _) =>
        {
            Result = Storage.Clone(settings);
            Result.Theme = (FileOrganizer.Core.ThemeMode)theme.SelectedIndex;
            Result.CloseAction = (WindowCloseAction)closeAction.SelectedIndex;
            Result.ConfirmExitOnClose = confirm.IsChecked == true;
            Result.AutoRegisterExplorerMenu = autoShell.IsChecked == true;
            DialogResult = true;
        }, primary: true));
    }
}

internal sealed class ExitConfirmation : Window
{
    public bool DoNotAskAgain { get; private set; }

    public ExitConfirmation(Window owner, bool working)
    {
        Owner = owner; Title = "차곡 종료";
        SetResourceReference(StyleProperty, typeof(Window));
        Width = 490; Height = Math.Min(300, SystemParameters.WorkArea.Height);
        MinWidth = 380; MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var shell = new DockPanel { Margin = new(24) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); shell.Children.Add(actions);
        var form = new StackPanel();
        shell.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = shell;
        form.Children.Add(new TextBlock { Text = "차곡을 종료할까요?", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 12) });
        EditorUi.Hint(form, working
            ? "진행 중인 작업을 안전하게 중단한 뒤 종료합니다. 이미 이동한 파일은 되돌리지 않습니다. 프로그램 감지도 중단됩니다."
            : "프로그램 감지가 중단됩니다. 다시 감지하려면 차곡을 실행해야 합니다.");
        var doNotAsk = EditorUi.Check("다시 묻지 않기", false);
        form.Children.Add(doNotAsk);
        var cancel = EditorUi.Button("취소", (_, _) => Close());
        cancel.IsCancel = true; cancel.IsDefault = true;
        actions.Children.Add(cancel);
        actions.Children.Add(EditorUi.Button("종료", (_, _) =>
        {
            DoNotAskAgain = doNotAsk.IsChecked == true;
            DialogResult = true;
        }, primary: true));
    }
}
