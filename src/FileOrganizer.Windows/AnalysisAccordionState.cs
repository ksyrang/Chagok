using System;
using FileOrganizer.Core;

namespace FileOrganizer.Windows;

// 화면 전환만 관리한다. 분석 저장소나 사용자가 입력한 조건은 소유하지 않는다.
internal sealed class AnalysisAccordionState
{
    public bool Editing { get; private set; } = true;
    public bool Running { get; private set; }
    public bool HasResults { get; private set; }
    public bool Changed { get; private set; }
    public void Begin() { Editing = false; Running = true; HasResults = false; Changed = false; }
    public void Loaded() { Editing = false; Running = false; HasResults = true; Changed = false; }
    public void Clear() { Editing = true; Running = false; HasResults = false; Changed = false; }
    public void Finish() { if (Running) { Running = false; Editing = true; } }
    public void Edit() { if (!Running) Editing = true; }
    public void ShowResults() { if (!Running && HasResults) Editing = false; }
    public void MarkChanged(bool value) => Changed = HasResults && value;

    public static bool SameConditions(AnalysisOptions a, AnalysisOptions b) => a.Recursive == b.Recursive && string.Equals(a.CustomPattern, b.CustomPattern, StringComparison.Ordinal)
        && (a.CustomPattern.Length > 0 || a.Brackets == b.Brackets
            && (a.Conditions?.IncludeBrackets ?? true) == (b.Conditions?.IncludeBrackets ?? true));
}
