using System.ComponentModel;
using FileOrganizer.Core;

namespace FileOrganizer.Windows;

internal sealed record RuleRow(RuleSpec Spec)
{
    public string Name => Spec.Name;
    public bool Enabled => Spec.Enabled;
    public string ConditionText => "조건  " + (Spec.Match == NameMatch.Any ? "파일명 제한 없음" :
        Spec.Match switch { NameMatch.Contains => $"이름에 ‘{Spec.Pattern}’ 포함", NameMatch.StartsWith => $"‘{Spec.Pattern}’로 시작", _ => $"패턴 ‘{Spec.Pattern}’" })
        + " · " + (Spec.Extensions.Length == 0 ? "모든 확장자" : Spec.Extensions);
    public string DestinationText => "이동  " + Spec.Destination;
    public string TriggerText => Spec.Trigger == TriggerMode.Manual ? "수동 실행" :
        Path.GetFileName(Spec.ProgramPath) + (Spec.Trigger == TriggerMode.ProgramStarted ? " 실행 시" : " 종료 시")
        + (Spec.RunMode == RunMode.Confirm ? " · 목록 확인 후 실행" : " · 자동 실행");
}

internal sealed class PreviewRow(PlanEntry entry) : INotifyPropertyChanged
{
    private bool selected = entry.CanExecute;
    private bool finished;
    private bool uncertain;
    private OperationResult? result;
    public PlanEntry Entry => entry;
    public string ResultText => result?.Status ?? (!entry.CanExecute ? "제외" : finished ? uncertain ? "미확인" : "미실행" : "대기");
    public string ResultMessage => result?.Message ?? (finished && entry.CanExecute
        ? uncertain ? "결과 저장 중 오류가 발생했습니다. 실행 기록과 실제 파일 위치를 확인하세요."
            : "선택하지 않았거나 실행이 중단되어 처리하지 않았습니다." : entry.Reason);
    public bool CanExecute => entry.CanExecute && !finished;
    public bool Selected
    {
        get => selected;
        set { selected = value && CanExecute; PropertyChanged?.Invoke(this, new(nameof(Selected))); }
    }
    public string FileDisplay => Path.GetFileName(entry.Source) + Environment.NewLine + entry.RuleName;
    public string TargetDisplay => entry.Target + Environment.NewLine + entry.Reason;
    public string ActionText => entry.Action switch
    { PlanAction.Move => "이동", PlanAction.Rename => "이름 변경 후 이동", PlanAction.Replace => "기존 파일 교체", PlanAction.Skip => "건너뛰기", _ => "보류" };
    public void SetResult(OperationResult? value)
    {
        result = value;
        PropertyChanged?.Invoke(this, new(nameof(ResultText)));
        PropertyChanged?.Invoke(this, new(nameof(ResultMessage)));
    }
    public void Finish(bool resultsIncomplete = false)
    {
        uncertain = resultsIncomplete; finished = true; Selected = false;
        PropertyChanged?.Invoke(this, new(nameof(CanExecute)));
        PropertyChanged?.Invoke(this, new(nameof(ResultText)));
        PropertyChanged?.Invoke(this, new(nameof(ResultMessage)));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed record HistoryRow(OperationResult Result)
{
    public string Time => Result.TimeUtc.ToLocalTime().ToString("MM-dd HH:mm:ss");
    public string File => Path.GetFileName(Result.Source) + Environment.NewLine + Result.Rule;
    public string Status => Result.Status;
}

internal sealed record TriggerRequest(Guid FolderId, HashSet<Guid> RuleIds, bool Automatic, string Reason);
