using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Chagok.Core;

namespace Chagok.Windows;

public partial class MainWindow
{
    private readonly Stopwatch executionClock = new();
    private readonly DispatcherTimer executionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool executing;
    private bool executionResultsIncomplete;
    private Guid? activeTransfer;
    private const string ArrowMark = "M 2,9 L 17,9 M 11,3 L 17,9 L 11,15";
    private const string CheckMark = "M 2,9 L 7,14 L 18,3";
    private const string AttentionMark = "M 10,1 L 19,18 L 1,18 Z M 10,6 L 10,11 M 10,14 L 10,15";
    private const string StopMark = "M 3,3 L 17,3 L 17,17 L 3,17 Z";

    private void WireExecutionFeedback()
    { executionTimer.Tick += (_, _) => UpdateElapsed(); }

    private void SetFeedback(string title, string details, string brush, string mark)
    {
        RunCard.BeginAnimation(OpacityProperty, null); RunCard.Opacity = 1;
        RunCard.Visibility = Visibility.Visible;
        RunCard.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, brush);
        RunTitle.Text = title; RunTitle.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, brush);
        RunSymbol.Data = Geometry.Parse(mark);
        RunSymbol.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brush);
        RunCountsText.Text = details;
        OverallProgress.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, brush);
        ResultHistoryButton.Visibility = Visibility.Collapsed;
    }

    private void ResetExecutionFeedback()
    {
        executing = false; activeTransfer = null;
        executionClock.Stop(); executionTimer.Stop();
        RunCard.BeginAnimation(OpacityProperty, null); RunCard.Opacity = 1;
        RunCard.Visibility = Visibility.Collapsed;
        OverallProgress.IsIndeterminate = CurrentFileProgress.IsIndeterminate = false;
        PreviewHint.Text = "검사 결과입니다. 실행할 항목을 선택한 뒤 아래 실행 버튼을 누르세요.";
    }

    private void ShowScanFeedback()
    {
        SetFeedback("폴더를 검사하고 있습니다", "검사 중에는 파일을 이동하지 않습니다.", "AccentBrush", ArrowMark);
        RunTimeText.Text = "";
        OverallProgress.Visibility = Visibility.Visible; OverallProgress.Value = 0;
        OverallProgress.IsIndeterminate = SystemParameters.ClientAreaAnimation;
        CurrentFileProgress.Visibility = Visibility.Collapsed;
        RunCurrentText.Text = "규칙에 맞는 파일을 찾고 검사 결과를 저장하고 있습니다.";
        RunCurrentText.ToolTip = RunCurrentText.Text;
        PreviewSubtitle.Text = "검사 결과를 준비하고 있습니다…";
        PreviewHint.Text = "검사가 끝나면 대상 목록을 확인할 수 있습니다.";
        ShowView(PreviewView);
    }

    private void ShowScanReady(DiskPlanStore plan)
    {
        SetFeedback("검사 완료 · 실행 대상을 확인하세요",
            $"대상 {plan.Count}개 · 실행 가능 {plan.ExecutableCount}개 · 검사 경고 {plan.WarningCount}개", "AccentBrush", ArrowMark);
        OverallProgress.IsIndeterminate = false; OverallProgress.Visibility = Visibility.Collapsed;
        CurrentFileProgress.Visibility = Visibility.Collapsed;
        PreviewHint.Text = "검사 결과입니다. 실행할 항목을 선택한 뒤 아래 실행 버튼을 누르세요.";
        RunCurrentText.Text = "아직 파일을 이동하지 않았습니다. 선택 항목 실행을 누르면 이동합니다.";
        RunCurrentText.ToolTip = RunCurrentText.Text;
    }

    private void ShowScanStopped(string title, string message, bool error)
    {
        SetFeedback(title, "파일은 이동하지 않았습니다.", error ? "ErrorBrush" : "CautionBrush", error ? AttentionMark : StopMark);
        OverallProgress.IsIndeterminate = false; OverallProgress.Visibility = Visibility.Collapsed;
        CurrentFileProgress.Visibility = Visibility.Collapsed;
        RunCurrentText.Text = message; RunCurrentText.ToolTip = message;
        PreviewSubtitle.Text = "다시 검사한 뒤 실행할 수 있습니다.";
        PreviewHint.Text = "검사가 중단됐으며 파일은 이동하지 않았습니다.";
    }

    private void BeginExecutionFeedback(ExecutionReport report)
    {
        executing = true; executionResultsIncomplete = false; executionClock.Restart(); executionTimer.Start();
        SetFeedback("파일을 정리하고 있습니다", "", "AccentBrush", ArrowMark);
        OverallProgress.Visibility = Visibility.Visible; OverallProgress.IsIndeterminate = false;
        CurrentFileProgress.Visibility = Visibility.Visible;
        CurrentFileProgress.IsIndeterminate = false; CurrentFileProgress.Value = 0;
        RunCurrentText.Text = "실행 목록을 준비하고 있습니다.";
        RunCurrentText.ToolTip = RunCurrentText.Text;
        PreviewHint.Text = "전체 선택 항목을 처리하고 있습니다. 취소하면 안전하게 중단합니다.";
        ShowView(PreviewView); UpdateExecutionFeedback(report); UpdateElapsed();
    }

    private void UpdateExecutionFeedback(ExecutionReport report)
    {
        OverallProgress.Value = report.Percent;
        RunCountsText.Text = $"처리 {report.Processed} / {report.Total}개 ({report.Percent:0}%) · 완료 {report.Completed} · 확인 필요 {report.Deferred + report.Failed + report.Preserved + report.Skipped}";
    }

    private void UpdateTransferFeedback(Guid entryId, string path, TransferProgress progress)
    {
        if (!executing || activeTransfer != entryId) return;
        CurrentFileProgress.IsIndeterminate = progress.Stage != TransferStage.Copy && SystemParameters.ClientAreaAnimation;
        var percent = progress.TotalBytes == 0 ? 100 : Math.Clamp(100d * progress.CopiedBytes / progress.TotalBytes, 0, 100);
        CurrentFileProgress.Value = percent;
        var phase = progress.Stage switch
        {
            TransferStage.Copy => $"복사 {percent:0}% · {FormatBytes(progress.CopiedBytes)} / {FormatBytes(progress.TotalBytes)}",
            TransferStage.VerifyCopy => "복사한 파일을 검증하고 있습니다",
            TransferStage.Publish => "이동 위치를 확정하고 있습니다",
            _ => "원본 삭제 전 파일을 재검증하고 있습니다"
        };
        RunCurrentText.Text = $"{Path.GetFileName(path)} · {phase}";
        RunCurrentText.ToolTip = path + Environment.NewLine + phase;
    }

    private void ShowExecutionFinished(ExecutionReport report)
    {
        executing = false; activeTransfer = null; executionClock.Stop(); executionTimer.Stop();
        var brush = report.Outcome switch
        {
            ExecutionOutcome.Success => "SuccessBrush",
            ExecutionOutcome.Error => "ErrorBrush",
            _ => "CautionBrush"
        };
        var mark = report.Outcome == ExecutionOutcome.Success ? CheckMark
            : report.Outcome == ExecutionOutcome.Canceled ? StopMark : AttentionMark;
        SetFeedback(report.Title, report.CountsText, brush, mark);
        OverallProgress.Visibility = Visibility.Visible; OverallProgress.IsIndeterminate = false;
        OverallProgress.Value = report.Percent;
        CurrentFileProgress.IsIndeterminate = false; CurrentFileProgress.Visibility = Visibility.Collapsed;
        RunCurrentText.Text = report.Error ?? (report.Outcome switch
        {
            ExecutionOutcome.Success => "선택한 파일을 모두 처리했습니다. 아래 목록에서 파일별 결과를 확인하세요.",
            ExecutionOutcome.Canceled => "취소 전에 완료된 이동은 유지됩니다. 미실행 항목은 새로 검사해 주세요.",
            _ => "보류·실패·원본 보존 항목의 이유를 아래 결과 또는 실행 기록에서 확인하세요."
        });
        RunCurrentText.ToolTip = RunCurrentText.Text;
        ResultHistoryButton.Visibility = Visibility.Visible;
        PreviewHint.Text = "요약은 이번 선택 항목 기준입니다. 목록에는 선택하지 않은 행도 표시됩니다.";
        UpdateElapsed();
        if (SystemParameters.ClientAreaAnimation)
            RunCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(240)) { FillBehavior = FillBehavior.Stop });
        UIElementAutomationPeer.FromElement(RunTitle)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void UpdateElapsed()
    {
        var elapsed = executionClock.Elapsed;
        var time = elapsed.TotalHours >= 1 ? $"{(long)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        RunTimeText.Text = (executing ? "경과 " : "소요 ") + time;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024 * 1024):0.0} GiB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024d * 1024):0.0} MiB";
        return $"{bytes / 1024d:0.0} KiB";
    }
}
