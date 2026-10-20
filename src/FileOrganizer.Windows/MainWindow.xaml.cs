using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FileOrganizer.Core;
using CoreTheme = FileOrganizer.Core.ThemeMode;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace FileOrganizer.Windows;

public partial class MainWindow : Window
{
    private readonly Storage storage;
    private AppSettings settings;
    private readonly ProgramMonitor monitor = new();
    private readonly Executor executor = new(new WindowsRecycleBin());
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private readonly Forms.ToolStripMenuItem pauseMenu;
    private readonly SemaphoreSlim monitorGate = new(1, 1);
    private readonly Queue<TriggerRequest> pending = [];
    private readonly ObservableCollection<PreviewRow> previewRows = [];
    private DiskPlanStore? currentPlan;
    private const int PreviewPageSize = 100;
    private long previewPage;
    private bool planExecutable;
    private CancellationTokenSource? workCancellation;
    private bool busy, modal, pumping, awaitingReview, exiting, exitAfterWork, monitoringPaused;
    private bool trayNoticeShown, closeRequested;
    private Guid? selectedFolderId;
    private FolderSpec? SelectedFolder => settings.Folders.FirstOrDefault(f => f.Id == selectedFolderId);

    public MainWindow(Storage storage, AppSettings settings)
    {
        this.storage = storage; this.settings = settings;
        InitializeComponent();
        WireExecutionFeedback(); InitializeFilenameAnalysis();
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        UpdateThemeButton();
        PreviewGrid.ItemsSource = previewRows;
        using (var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/chagok.ico"))!.Stream)
        using (var icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize))
            trayIcon = (System.Drawing.Icon)icon.Clone();
        tray = new Forms.NotifyIcon
        {
            Text = "차곡", Icon = trayIcon, Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(Reveal));
        tray.BalloonTipClicked += (_, _) => Dispatcher.BeginInvoke(new Action(Reveal));
        tray.ContextMenuStrip.Items.Add("차곡 열기", null, (_, _) => Dispatcher.BeginInvoke(new Action(Reveal)));
        pauseMenu = new Forms.ToolStripMenuItem("프로그램 감지 일시 중지");
        pauseMenu.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            monitoringPaused = !monitoringPaused;
            pauseMenu.Checked = monitoringPaused;
            if (monitoringPaused) pending.Clear();
            UpdatePending();
            _ = RestartMonitoringAsync();
            SetStatus(monitoringPaused ? "프로그램 감지를 일시 중지했습니다. 대기 요청도 해제했습니다." : "프로그램 감지를 다시 시작했습니다.");
        }));
        tray.ContextMenuStrip.Items.Add(pauseMenu);
        tray.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        tray.ContextMenuStrip.Items.Add("종료", null, (_, _) => Dispatcher.BeginInvoke(new Action(ExitApplication)));
        monitor.Signaled += signal => Dispatcher.BeginInvoke(new Action(() => EnqueueSignal(signal)));
        monitor.FallbackStarted += message => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (exiting) return;
            SetStatus(message);
            try { storage.Append(new(DateTime.UtcNow, "", "프로그램 감지", "", "", "감지", "대체 감지", message)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus(message + " / 기록 저장 실패: " + ex.Message); }
        }));
        monitor.Failed += message => Dispatcher.BeginInvoke(new Action(() =>
        {
            SetStatus(message);
            try { storage.Append(new(DateTime.UtcNow, "", "프로그램 감지", "", "", "감지", "실패", message)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus(message + " / 기록 저장 실패: " + ex.Message); }
        }));
        monitor.PathMismatch += message => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (exiting) return;
            SetStatus(message);
            try { storage.Append(new(DateTime.UtcNow, "", "프로그램 감지", "", "", "감지", "경로 확인", message)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus(message + " / 기록 저장 실패: " + ex.Message); }
        }));
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
            ClearFilenameAnalysis(); InvalidatePlan(); monitor.Dispose(); tray.Dispose(); trayIcon.Dispose();
        };
        selectedFolderId = settings.Folders.FirstOrDefault()?.Id;
        UpdateCloseHint();
        RefreshFolders();
        Loaded += async (_, _) => await RestartMonitoringAsync();
    }

    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (settings.Theme == CoreTheme.System) Dispatcher.BeginInvoke(new Action(() => ThemeManager.Apply(settings.Theme)));
    }

    private void OpenThemeOptions(object sender, RoutedEventArgs e)
    {
        if (busy || modal || exitAfterWork) return;
        UpdateThemeButton();
        ThemePopup.IsOpen = !ThemePopup.IsOpen;
    }

    private void ThemePopupOpened(object? sender, EventArgs e)
    {
        var selected = settings.Theme switch
        {
            CoreTheme.Light => ThemeLightOption,
            CoreTheme.Dark => ThemeDarkOption,
            _ => ThemeSystemOption
        };
        selected.Focus();
    }

    private void ThemeChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string value }) return;
        if (!busy && !modal && !exitAfterWork)
            Commit(() => settings.Theme = Enum.Parse<CoreTheme>(value), restartMonitor: false);
        ThemeManager.Apply(settings.Theme);
        UpdateThemeButton();
        ThemePopup.IsOpen = false;
        ThemeButton.Focus();
    }

    private void ThemeOptionsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ThemePopup.IsOpen = false; ThemeButton.Focus(); e.Handled = true; return;
        }
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)) return;
        var options = new[] { ThemeSystemOption, ThemeLightOption, ThemeDarkOption };
        var index = Array.FindIndex(options, option => option.IsKeyboardFocused);
        var next = e.Key switch
        {
            Key.Home => 0, Key.End => 2,
            Key.Left or Key.Up => (index + 2) % 3, _ => (index + 1) % 3
        };
        options[next].Focus(); e.Handled = true;
    }

    private void UpdateThemeButton()
    {
        var label = settings.Theme switch { CoreTheme.Light => "라이트", CoreTheme.Dark => "다크", _ => "시스템" };
        ThemeButton.ToolTip = $"테마 선택 · 현재 {label}";
        System.Windows.Automation.AutomationProperties.SetName(ThemeButton, $"테마 선택 · 현재 {label}");
        ThemeSystemOption.IsChecked = settings.Theme == CoreTheme.System;
        ThemeLightOption.IsChecked = settings.Theme == CoreTheme.Light;
        ThemeDarkOption.IsChecked = settings.Theme == CoreTheme.Dark;
        ThemeSymbol.Data = (System.Windows.Media.Geometry)FindResource($"Theme{settings.Theme}Icon");
    }

    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        if (busy || modal || exitAfterWork) return;
        modal = true;
        try
        {
            var editor = new SettingsEditor(this, settings, storage.DirectoryPath, PersistExplorerAutoRegistration);
            if (editor.ShowDialog() != true || editor.Result is not { } result) return;
            if (!Commit(() =>
            {
                settings.Theme = result.Theme;
                settings.CloseAction = result.CloseAction;
                settings.ConfirmExitOnClose = result.ConfirmExitOnClose;
                settings.AutoRegisterExplorerMenu = result.AutoRegisterExplorerMenu;
            }, restartMonitor: false)) return;
            ThemeManager.Apply(settings.Theme);
            UpdateThemeButton();
            UpdateCloseHint(); SetStatus("앱 설정을 저장했습니다.");
        }
        finally { modal = false; PumpPending(); }
    }

    private void UpdateCloseHint() => CloseHint.Text = settings.CloseAction == WindowCloseAction.HideToTray
        ? "창을 닫아도 트레이에서 실행합니다. 완전히 종료하려면 트레이 메뉴를 사용하세요."
        : "창을 닫으면 프로그램과 감지가 종료됩니다. 설정에서 백그라운드 실행으로 바꿀 수 있습니다.";

    private bool Commit(Action mutation, bool restartMonitor = true)
    {
        var previous = Storage.Clone(settings);
        try
        {
            mutation(); storage.Save(settings);
            if (restartMonitor)
            {
                ClearFilenameAnalysis(); InvalidatePlan(); pending.Clear(); UpdatePending();
                _ = RestartMonitoringAsync();
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            settings = previous;
            ThemeManager.Apply(settings.Theme);
            UpdateThemeButton();
            if (restartMonitor) RefreshFolders(); else RefreshRules();
            ShowError("설정을 저장하지 못했습니다. 이전 설정을 유지합니다.", ex);
            return false;
        }
    }

    private async Task RestartMonitoringAsync()
    {
        await monitorGate.WaitAsync();
        try
        {
            if (exiting) return;
            var paths = monitoringPaused ? [] : TriggerPlanner.ProgramPaths(Storage.Clone(settings));
            await Task.Run(() => monitor.Configure(paths));
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or Win32Exception
            or System.Runtime.InteropServices.COMException or ArgumentException)
        { SetStatus("프로그램 감지를 시작하지 못했습니다. 수동 실행은 사용할 수 있습니다. " + ex.Message); }
        finally { monitorGate.Release(); }
    }

    private void RefreshFolders()
    {
        FolderList.ItemsSource = null; FolderList.ItemsSource = settings.Folders;
        FolderList.SelectedItem = SelectedFolder;
        RefreshRules();
    }

    private void FolderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderList.SelectedItem is not FolderSpec folder) return;
        selectedFolderId = folder.Id;
        var showAnalysis = AnalysisView.Visibility == Visibility.Visible;
        InvalidatePlan(); RefreshRules();
        if (showAnalysis) { AnalysisView.Bind(filenameAnalysis, folder.Id); ShowView(AnalysisView); } else ShowView(RulesView);
        PumpPending();
    }

    private void RefreshRules()
    {
        var folder = SelectedFolder;
        FolderTitle.Text = folder?.Name ?? "시작하기";
        FolderPathText.Text = folder?.Path ?? "검사할 폴더를 추가하세요.";
        FolderTriggerText.Text = folder is null ? "" : folder.Trigger == TriggerMode.Manual ? "폴더 실행 조건: 수동 실행" :
            "폴더 실행 조건: " + Path.GetFileName(folder.ProgramPath)
            + (folder.Trigger == TriggerMode.ProgramStarted ? " 실행 시" : " 종료 시")
            + (folder.RunMode == RunMode.Confirm ? " · 목록 확인 후 실행" : " · 자동 실행");
        RuleList.ItemsSource = folder?.Rules.Select(r => new RuleRow(r)).ToArray();
        RuleCount.Text = folder is null ? "" : $"정리 규칙 {folder.Rules.Count}개 · 사용 {folder.Rules.Count(r => r.Enabled)}개";
        EmptyRulesText.Visibility = folder is null || folder.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyRulesText.Text = folder is null ? "왼쪽의 ＋ 버튼으로 검사할 폴더를 추가하세요." : "‘규칙 추가’로 파일 조건과 이동 위치를 설정하세요.";
        AddRuleButton.IsEnabled = folder is not null && !busy;
        PreviewButton.IsEnabled = folder?.Rules.Any(r => r.Enabled) == true && !busy;
        FolderOptionsButton.IsEnabled = folder is not null && !busy;
    }

    private void AddFolder(object sender, RoutedEventArgs e) => OpenFolderEditor(null);
    private void EditFolder(object sender, RoutedEventArgs e) { if (SelectedFolder is { } folder) OpenFolderEditor(folder); }
    private void DuplicateFolder(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { Tag: FolderSpec folder }) OpenFolderEditor(folder, duplicate: true); }
    private void OpenFolderEditor(FolderSpec? original, bool duplicate = false)
    {
        if (busy || modal) return;
        modal = true;
        try
        {
            var editor = new FolderEditor(this, settings, original, duplicate);
            if (editor.ShowDialog() != true) return;
            if (Commit(() =>
            {
                if (original is not null && !duplicate) settings.Folders.RemoveAll(f => f.Id == original.Id);
                if (!editor.DeleteRequested && editor.Result is { } result)
                { settings.Folders.Add(result); selectedFolderId = result.Id; }
                else selectedFolderId = settings.Folders.FirstOrDefault()?.Id;
            })) { RefreshFolders(); SetStatus("폴더 설정을 저장했습니다."); }
        }
        finally { modal = false; PumpPending(); }
    }

    private void AddRule(object sender, RoutedEventArgs e) => OpenRuleEditor(null);
    private void EditRule(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { Tag: RuleRow row }) OpenRuleEditor(row.Spec); }
    private void OpenRuleEditor(RuleSpec? original)
    {
        if (busy || modal || SelectedFolder is not { } folder) return;
        modal = true;
        try
        {
            var editor = new RuleEditor(this, folder, original);
            if (editor.ShowDialog() != true) return;
            if (Commit(() =>
            {
                var index = original is null ? -1 : folder.Rules.FindIndex(r => r.Id == original.Id);
                if (index >= 0) folder.Rules.RemoveAt(index);
                if (!editor.DeleteRequested && editor.Result is { } result)
                {
                    folder.Rules.Insert(index < 0 ? folder.Rules.Count : index, result);
                    DestinationPaths.Remember(folder, result);
                }
            })) { RefreshRules(); SetStatus("규칙을 저장했습니다."); }
        }
        finally { modal = false; PumpPending(); }
    }

    private void ToggleRule(object sender, RoutedEventArgs e)
    {
        if (busy || modal || sender is not ToggleButton { Tag: RuleRow row } toggle) return;
        var restoreKeyboardFocus = toggle.IsKeyboardFocused;
        Commit(() => row.Spec.Enabled = toggle.IsChecked == true);
        RefreshRules();
        if (restoreKeyboardFocus)
        {
            // 저장 후 카드가 다시 만들어져도 Space로 같은 규칙을 계속 전환할 수 있게 한다.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (busy || modal || !RulesView.IsVisible) return;
                var index = SelectedFolder?.Rules.FindIndex(r => r.Id == row.Spec.Id) ?? -1;
                if (index >= 0 && RuleList.ItemContainerGenerator.ContainerFromIndex(index) is ContentPresenter presenter
                    && presenter.ContentTemplate?.FindName("RuleEnabledSwitch", presenter) is ToggleButton replacement)
                    replacement.Focus();
            }));
        }
    }

    private async void PreviewFolder(object sender, RoutedEventArgs e)
    { if (SelectedFolder is { } folder) await ScanAsync(folder.Id); }

    private async void PreviewRule(object sender, RoutedEventArgs e)
    {
        if (busy || SelectedFolder is not { } folder || sender is not FrameworkElement { Tag: RuleRow row } || !row.Enabled) return;
        await ScanAsync(folder.Id, new HashSet<Guid> { row.Spec.Id });
    }

    private async Task<DiskPlanStore?> ScanAsync(Guid folderId, IReadOnlySet<Guid>? rules = null, bool automatic = false)
    {
        if (busy || settings.Folders.FirstOrDefault(f => f.Id == folderId) is not { } folder) return null;
        ClearFilenameAnalysis(); InvalidatePlan();
        SetBusy(true, "폴더를 검사하고 실행 계획을 디스크에 저장하고 있습니다…");
        ShowScanFeedback();
        ScanWarnings.Visibility = Visibility.Collapsed;
        DiskPlanStore? scanned = null;
        try
        {
            var copy = Storage.Clone(folder);
            if (PathSafety.IsWithin(copy.Path, storage.DirectoryPath))
                throw new ArgumentException("차곡 데이터 저장소는 검사 폴더로 사용할 수 없습니다.");
            var destinations = settings.Folders.SelectMany(f => f.Rules).Select(DestinationPaths.Resolve).Where(p => !string.IsNullOrWhiteSpace(p))
                .Append(storage.DirectoryPath).ToArray();
            var token = workCancellation!.Token;
            scanned = await Task.Run(() => new Planner().ScanToDisk(copy, destinations,
                Path.Combine(storage.DirectoryPath, "scan-cache"), rules, token));
            var warnings = await Task.Run(() => scanned.ReadWarnings());
            if (scanned.WarningCount > 0)
                storage.Append(new(DateTime.UtcNow, scanned.Header.FolderName, "폴더 검사", copy.Path, "", "검사", "검사 경고",
                    string.Join(Environment.NewLine, warnings) + (scanned.WarningCount > 20 ? $"\n추가 경고 {scanned.WarningCount - 20}개" : "")));
            selectedFolderId = folderId;
            RefreshFolders();
            currentPlan = scanned; planExecutable = true;
            await LoadPageAsync(0);
            var scope = rules is null ? "켜진 규칙 전체" : string.Join(", ", copy.Rules.Where(r => rules.Contains(r.Id)).Select(r => r.Name));
            PreviewSubtitle.Text = $"{scanned.Header.FolderName} · {scope} · 대상 {scanned.Count}개 · {scanned.Header.CreatedUtc.ToLocalTime():HH:mm:ss} 검사";
            ScanWarnings.Text = string.Join(Environment.NewLine, warnings.Take(5))
                + (scanned.WarningCount > 5 ? $"\n추가 검사 경고 {scanned.WarningCount - 5}개" : "");
            ScanWarnings.Visibility = scanned.WarningCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowScanReady(scanned);
            awaitingReview = !automatic;
            ShowView(PreviewView); UpdateSelection();
            SetStatus($"검사 완료 · 실행 가능 {scanned.ExecutableCount}개 · 검사 경고 {scanned.WarningCount}개");
            return scanned;
        }
        catch (OperationCanceledException)
        {
            InvalidatePlan(); SetStatus("검사를 취소했습니다. 파일은 변경하지 않았습니다.");
            ShowScanStopped("검사를 취소했습니다", "필요할 때 다시 검사해 주세요.", error: false); return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            InvalidatePlan();
            try { storage.Append(new(DateTime.UtcNow, folder.Name, "폴더 검사", folder.Path, "", "검사", "실패", ex.Message)); }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
            { SetStatus("검사 오류와 기록 저장 오류: " + ex.Message + " / " + logError.Message); }
            ShowScanStopped("폴더를 검사하지 못했습니다", ex.Message, error: true);
            ShowError("폴더를 검사하지 못했습니다.", ex); return null;
        }
        finally
        {
            if (scanned is not null && !ReferenceEquals(currentPlan, scanned)) scanned.Dispose();
            SetBusy(false);
        }
    }

    private async Task LoadPageAsync(long page)
    {
        if (currentPlan is not { } plan) return;
        var rows = await Task.Run(() => plan.ReadPage(page * PreviewPageSize, PreviewPageSize));
        previewPage = page; previewRows.Clear();
        foreach (var record in rows)
        {
            var row = new PreviewRow(record.Entry) { Selected = record.Selected };
            row.SetResult(record.Result);
            if (!planExecutable) row.Finish(executionResultsIncomplete);
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(PreviewRow.Selected) || busy || !planExecutable || !ReferenceEquals(currentPlan, plan)) return;
                try { plan.SetSelected(record.Position, row.Selected); UpdateSelection(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { InvalidatePlan(); ShowError("선택 상태를 저장하지 못했습니다. 다시 검사하세요.", ex); }
            };
            previewRows.Add(row);
        }
        var pages = Math.Max(1, (plan.Count + PreviewPageSize - 1) / PreviewPageSize);
        PageText.Text = $"{page + 1} / {pages} 페이지 · 전체 {plan.Count}개 · 페이지당 {PreviewPageSize}개";
        UpdateSelection();
    }

    private async void PreviousPage(object sender, RoutedEventArgs e) => await ChangePageAsync(previewPage - 1);
    private async void NextPage(object sender, RoutedEventArgs e) => await ChangePageAsync(previewPage + 1);
    private async Task ChangePageAsync(long page)
    {
        if (busy || currentPlan is not { } plan || page < 0 || page >= Math.Max(1, (plan.Count + PreviewPageSize - 1) / PreviewPageSize)) return;
        SetBusy(true, "검사 결과를 불러오고 있습니다…");
        try { await LoadPageAsync(page); SetStatus("검사 결과를 불러왔습니다."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { InvalidatePlan(); ShowError("검사 결과를 읽지 못했습니다. 다시 검사하세요.", ex); }
        finally { SetBusy(false); PumpPending(); }
    }

    private async void ExecuteSelected(object sender, RoutedEventArgs e)
    {
        if (busy || !planExecutable || currentPlan is not { } plan || plan.SelectedCount == 0) return;
        if (plan.SelectedReplaceCount > 0 && MessageBox.Show(this,
            $"선택한 {plan.SelectedCount}개를 실행합니다. 같은 이름의 기존 대상 파일 {plan.SelectedReplaceCount}개는 휴지통으로 옮긴 뒤 교체합니다.",
            "파일 이동·교체", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        await ExecuteAsync(plan);
        PumpPending();
    }

    private async Task ExecuteAsync(DiskPlanStore plan)
    {
        if (busy || !planExecutable || plan.SelectedCount == 0) return;
        awaitingReview = false;
        SetBusy(true, "파일을 이동하고 있습니다…");
        var report = new ExecutionReport(plan.SelectedCount);
        BeginExecutionFeedback(report);
        var token = workCancellation!.Token;
        string? error = null;
        long cursor = 0;
        try
        {
            await Task.Run(plan.Flush);
            while (!token.IsCancellationRequested)
            {
                var entries = await Task.Run(() => plan.ReadSelectedPage(cursor, PreviewPageSize));
                if (entries.Count == 0) break;
                foreach (var record in entries)
                {
                    if (token.IsCancellationRequested) break;
                    var entry = record.Entry;
                    activeTransfer = entry.Id;
                    RunCurrentText.Text = Path.GetFileName(entry.Source) + " · 준비 중";
                    CurrentFileProgress.IsIndeterminate = false; CurrentFileProgress.Value = 0;
                    var progress = new Progress<TransferProgress>(value => UpdateTransferFeedback(entry.Id, entry.Source, value));
                    SetStatus($"처리 중 {report.Processed + 1}/{report.Total} · {Path.GetFileName(entry.Source)}");
                    storage.Append(new(DateTime.UtcNow, plan.Header.FolderName, entry.RuleName, entry.Source, entry.Target, entry.Action.ToString(), "시작", "실행 전 기록"));
                    var result = await Task.Run(() => executor.ExecuteAsync(plan.Header, entry, token, progress));
                    activeTransfer = null;
                    report.Record(result); UpdateExecutionFeedback(report);
                    previewRows.FirstOrDefault(row => row.Entry.Id == entry.Id)?.SetResult(result);
                    storage.Append(result);
                    await Task.Run(() => plan.StoreResult(record.Position, result));
                    cursor = record.Position;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            activeTransfer = null; executionResultsIncomplete = true;
            error = "실행 목록 또는 기록 오류로 남은 처리를 중단했습니다. 실행 기록을 확인하세요. " + ex.Message;
            ShowError("실행 목록 또는 기록을 읽고 쓰지 못해 남은 작업을 중단했습니다. 이미 처리한 파일의 위치를 확인하세요.", ex);
        }
        finally
        {
            activeTransfer = null; planExecutable = false;
            executionClock.Stop(); executionTimer.Stop();
            try { await Task.Run(() => plan.SetAllSelected(false)); await LoadPageAsync(previewPage); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                InvalidatePlan(keepFeedback: true);
                error ??= "결과 목록을 갱신하지 못했습니다. 실행 기록을 확인하세요. " + ex.Message;
            }
            report.Finish(token.IsCancellationRequested, error);
            ShowExecutionFinished(report);
            SetStatus(report.Title + " · " + report.CountsText);
            if (!IsVisible) Notify(report.Title, report.CountsText);
            SetBusy(false); UpdateSelection();
        }
    }

    private void UpdateSelection()
    {
        var count = planExecutable ? currentPlan?.SelectedCount ?? 0 : 0;
        SelectionText.Text = planExecutable ? $"전체 결과에서 선택 {count}개 · 다른 페이지의 선택도 함께 실행합니다."
            : currentPlan is not null ? "실행 결과는 위 요약과 ‘처리 결과’ 열에서 확인하세요. 다시 실행하려면 새로 검사하세요."
            : "폴더를 다시 검사한 뒤 실행할 수 있습니다.";
        ExecuteButton.Content = planExecutable ? $"선택 {count}개 실행" : currentPlan is not null ? "실행 종료" : "선택 항목 실행";
        ExecuteButton.IsEnabled = !busy && planExecutable && currentPlan is not null && count > 0;
        PreviousPageButton.IsEnabled = !busy && currentPlan is not null && previewPage > 0;
        NextPageButton.IsEnabled = !busy && currentPlan is { } plan && (previewPage + 1) * PreviewPageSize < plan.Count;
        SelectAllButton.IsEnabled = SelectNoneButton.IsEnabled = !busy && planExecutable && currentPlan is not null;
    }
    private async void SelectAll(object sender, RoutedEventArgs e) => await SelectAllAsync(true);
    private async void SelectNone(object sender, RoutedEventArgs e) => await SelectAllAsync(false);
    private async Task SelectAllAsync(bool selected)
    {
        if (busy || !planExecutable || currentPlan is not { } plan) return;
        SetBusy(true, "전체 결과의 선택 상태를 저장하고 있습니다…");
        try { await Task.Run(() => plan.SetAllSelected(selected)); await LoadPageAsync(previewPage); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { InvalidatePlan(); ShowError("선택 상태를 저장하지 못했습니다. 다시 검사하세요.", ex); }
        finally { SetBusy(false); PumpPending(); }
    }
    private void BackToRules(object sender, RoutedEventArgs e)
    { if (busy) return; InvalidatePlan(); ShowView(RulesView); PumpPending(); }
    private void ShowView(UIElement view)
    {
        RulesView.Visibility = ReferenceEquals(view, RulesView) ? Visibility.Visible : Visibility.Collapsed;
        PreviewView.Visibility = ReferenceEquals(view, PreviewView) ? Visibility.Visible : Visibility.Collapsed;
        HistoryView.Visibility = ReferenceEquals(view, HistoryView) ? Visibility.Visible : Visibility.Collapsed;
        AnalysisView.Visibility = ReferenceEquals(view, AnalysisView) ? Visibility.Visible : Visibility.Collapsed;
    }
    private void InvalidatePlan(bool keepFeedback = false)
    {
        var previous = currentPlan;
        currentPlan = null; planExecutable = false; awaitingReview = false; previewPage = 0;
        previewRows.Clear(); previous?.Dispose();
        if (!keepFeedback) ResetExecutionFeedback();
        if (ExecuteButton is not null) UpdateSelection();
    }

    private void ShowHistory(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try
        {
            InvalidatePlan();
            HistoryGrid.ItemsSource = storage.ReadHistory().Select(r => new HistoryRow(r)).ToArray();
            ShowView(HistoryView);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError("실행 기록을 읽지 못했습니다.", ex); }
    }
    private void HistoryDetails(object sender, MouseButtonEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryRow row) return;
        var r = row.Result;
        var dialog = new Window
        {
            Owner = this, Title = "실행 기록 상세", Width = 650, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBox { Text = $"시간: {r.TimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n폴더: {r.Folder}\n규칙: {r.Rule}\n작업: {r.Action}\n결과: {r.Status}\n\n원본: {r.Source}\n대상: {r.Target}\n\n{r.Message}",
                IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new(20), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        modal = true; try { dialog.ShowDialog(); } finally { modal = false; PumpPending(); }
    }
    private void EnqueueSignal(ProgramSignal signal)
    {
        if (exiting || exitAfterWork || monitoringPaused) return;
        var added = 0; var merged = 0;
        foreach (var batch in TriggerPlanner.Build(settings, signal.Path, signal.Trigger))
        {
            var ids = batch.RuleIds;
            if (pending.Any(p => p.FolderId == batch.FolderId && p.Automatic == batch.Automatic && p.RuleIds.SetEquals(ids))) { merged++; continue; }
            if (pending.Count >= 100) { SetStatus("프로그램 감지 요청이 많아 추가 요청을 생략했습니다. 대기 목록을 확인하세요."); break; }
            pending.Enqueue(new(batch.FolderId, ids, batch.Automatic,
                Path.GetFileName(signal.Path) + (signal.Trigger == TriggerMode.ProgramStarted ? " 실행 감지" : " 종료 감지")));
            added++;
        }
        var waiting = modal ? "편집 창 닫기 대기" : busy ? "현재 작업 완료 대기" : awaitingReview ? "현재 검사 목록 확인 대기"
            : pending.TryPeek(out var first) && !first.Automatic ? "감지 목록 확인 대기" : "자동 요청 처리 가능";
        try { storage.Append(new(DateTime.UtcNow, "", "프로그램 감지", signal.Path, "", "감지", "감지됨",
            $"{(signal.Trigger == TriggerMode.ProgramStarted ? "시작" : "종료")} · 추가 {added}개 · 기존 대기와 통합 {merged}개 · {waiting}")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("감지 기록 저장 실패: " + ex.Message); }
        UpdatePending(); PumpPending();
    }

    private async void PumpPending()
    {
        if (!busy && !modal && !exiting && !exitAfterWork && shellDestinations.Count > 0)
        { _ = Dispatcher.BeginInvoke(new Action(ProcessShellRequests)); return; }
        if (pumping || busy || modal || awaitingReview || exiting || exitAfterWork) return;
        pumping = true;
        try
        {
            while (pending.TryPeek(out var request) && request.Automatic && !busy && !modal && !awaitingReview && !exiting && !exitAfterWork && shellDestinations.Count == 0)
            {
                pending.Dequeue(); UpdatePending();
                var ids = CurrentRequestRules(request);
                if (ids.Count == 0) continue;
                var plan = await ScanAsync(request.FolderId, ids, automatic: true);
                if (plan is not null && !exiting && !exitAfterWork) await ExecuteAsync(plan);
            }
        }
        finally { pumping = false; UpdatePending(); }
    }

    private HashSet<Guid> CurrentRequestRules(TriggerRequest request) => settings.Folders.FirstOrDefault(f => f.Id == request.FolderId)?.Rules
        .Where(r => r.Enabled && request.RuleIds.Contains(r.Id)).Select(r => r.Id).ToHashSet() ?? [];
    private async void OpenPending(object sender, RoutedEventArgs e)
    {
        if (busy || modal || !pending.TryDequeue(out var request)) return;
        UpdatePending(); Reveal();
        var ids = CurrentRequestRules(request);
        if (ids.Count == 0) { SetStatus("요청에 해당하는 규칙이 변경되어 실행하지 않았습니다."); PumpPending(); return; }
        await ScanAsync(request.FolderId, ids); PumpPending();
    }
    private void DismissPending(object sender, RoutedEventArgs e)
    { if (busy) return; pending.TryDequeue(out _); UpdatePending(); PumpPending(); }
    private void UpdatePending()
    {
        PendingBanner.Visibility = pending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (pending.TryPeek(out var request))
        {
            PendingText.Text = $"{request.Reason} · 대기 {pending.Count}개";
            if (!IsVisible && !busy) Notify("차곡 요청", PendingText.Text + " — 트레이에서 열어 확인하세요.");
        }
    }

    private void SetBusy(bool value, string? message = null)
    {
        busy = value;
        if (value) workCancellation = new CancellationTokenSource();
        else { workCancellation?.Dispose(); workCancellation = null; }
        AddFolderButton.IsEnabled = !value; FolderList.IsEnabled = !value;
        AnalyzeAllButton.IsEnabled = !value; AnalysisView.WorkState(value);
        RuleList.IsEnabled = !value; PendingBanner.IsEnabled = !value; ThemeButton.IsEnabled = !value && !exitAfterWork;
        if (value) ThemePopup.IsOpen = false;
        SettingsButton.IsEnabled = !value && !exitAfterWork;
        PreviewGrid.IsEnabled = !value;
        PreviewBackButton.IsEnabled = HistoryNavButton.IsEnabled = ResultHistoryButton.IsEnabled = !value;
        CancelButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        RefreshRules(); UpdateSelection();
        if (message is not null) SetStatus(message);
        if (!value && exitAfterWork) ExitApplication();
        else if (!value && shellDestinations.Count > 0) Dispatcher.BeginInvoke(new Action(ProcessShellRequests));
    }
    private void CancelWork(object sender, RoutedEventArgs e)
    {
        workCancellation?.Cancel(); SetStatus("현재 작업을 안전하게 마친 뒤 취소합니다…");
        if (executing) RunTitle.Text = "취소 요청 · 안전하게 중단하고 있습니다";
    }
    private void SetStatus(string text) { StatusText.Text = text; StatusText.ToolTip = text; }
    private void ShowError(string heading, Exception ex)
    {
        SetStatus(heading + " " + ex.Message);
        if (IsVisible) MessageBox.Show(this, heading + "\n\n" + ex.Message, "차곡", MessageBoxButton.OK, MessageBoxImage.Warning);
        else Notify("차곡 오류", heading + " " + ex.Message);
    }
    private void Notify(string title, string text)
    { tray.BalloonTipTitle = title; tray.BalloonTipText = text.Length > 220 ? text[..220] : text; tray.ShowBalloonTip(5000); }
    private void Reveal() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true;
        if (modal || closeRequested || exitAfterWork) return;
        if (settings.CloseAction == WindowCloseAction.HideToTray)
        {
            Hide();
            if (!trayNoticeShown)
            { Notify("차곡은 트레이에서 실행 중입니다", "켜진 프로그램 감지 규칙이 있으면 감지를 계속합니다. 완전히 종료하려면 트레이 메뉴의 ‘종료’를 선택하세요."); trayNoticeShown = true; }
            return;
        }
        // Closing 안에서 다시 Close를 호출하지 않도록 현재 닫기 이벤트가 끝난 뒤 처리한다.
        closeRequested = true;
        Dispatcher.BeginInvoke(new Action(ConfirmWindowExit));
    }

    private void ConfirmWindowExit()
    {
        if (exiting || exitAfterWork || modal) { closeRequested = false; return; }
        try
        {
            modal = true;
            if (settings.ConfirmExitOnClose)
            {
                var confirmation = new ExitConfirmation(this, busy);
                if (confirmation.ShowDialog() != true) return;
                if (confirmation.DoNotAskAgain
                    && !Commit(() => settings.ConfirmExitOnClose = false, restartMonitor: false)) return;
            }
            ExitApplication();
        }
        finally
        {
            closeRequested = false; modal = false;
            if (!exiting && !exitAfterWork) PumpPending();
        }
    }
    private void ExitApplication()
    {
        // 다른 편집 창이 열려 있을 때 트레이 종료가 저장 작업과 겹치지 않게 한다.
        if (modal && !closeRequested) return;
        if (busy)
        {
            exitAfterWork = true; pending.Clear(); UpdatePending();
            SettingsButton.IsEnabled = false;
            workCancellation?.Cancel(); SetStatus("현재 작업을 안전하게 중단한 뒤 종료합니다…");
            if (executing) RunTitle.Text = "종료 요청 · 안전하게 중단하고 있습니다";
            return;
        }
        exiting = true; pending.Clear(); shellDestinations.Clear(); monitor.Dispose();
        tray.Visible = false; Close(); System.Windows.Application.Current.Shutdown();
    }
}
