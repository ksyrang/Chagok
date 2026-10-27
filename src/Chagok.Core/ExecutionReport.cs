namespace Chagok.Core;

public enum TransferStage { Copy, VerifyCopy, Publish, VerifyBeforeDelete }
public sealed record TransferProgress(TransferStage Stage, long CopiedBytes, long TotalBytes);
public enum ExecutionOutcome { Success, Attention, Canceled, Error }

// 전체 결과 목록 대신 카운터만 보관한다. 미실행 항목과 취소된 시도를 구분한다.
public sealed class ExecutionReport
{
    public long Total { get; }
    public long Processed { get; private set; }
    public long Completed { get; private set; }
    public long Deferred { get; private set; }
    public long Failed { get; private set; }
    public long Preserved { get; private set; }
    public long Canceled { get; private set; }
    public long Skipped { get; private set; }
    public long Remaining => Total - Processed;
    public double Percent => Total == 0 ? 0 : 100d * Processed / Total;
    public bool CancellationRequested { get; private set; }
    public string? Error { get; private set; }
    public ExecutionOutcome Outcome => Error is not null ? ExecutionOutcome.Error
        : Canceled > 0 || (CancellationRequested && (Remaining > 0 || Preserved > 0)) ? ExecutionOutcome.Canceled
        : Remaining > 0 || Deferred + Failed + Preserved + Skipped > 0 ? ExecutionOutcome.Attention
        : ExecutionOutcome.Success;
    public string Title => Outcome switch
    {
        ExecutionOutcome.Success => "파일 정리를 완료했습니다",
        ExecutionOutcome.Canceled => "실행을 취소했습니다",
        ExecutionOutcome.Error => "오류로 실행을 중단했습니다",
        _ => "처리 완료 · 일부 항목 확인 필요"
    };
    public string CountsText
    {
        get
        {
            var parts = new List<string> { $"선택 {Total}개 기준", $"완료 {Completed}" };
            if (Deferred > 0) parts.Add($"보류 {Deferred}");
            if (Failed > 0) parts.Add($"실패 {Failed}");
            if (Preserved > 0) parts.Add($"원본 보존 {Preserved}");
            if (Canceled > 0) parts.Add($"취소 {Canceled}");
            if (Skipped > 0) parts.Add($"건너뜀 {Skipped}");
            if (Remaining > 0) parts.Add($"미실행 {Remaining}");
            return string.Join(" · ", parts);
        }
    }

    public ExecutionReport(long total)
    { ArgumentOutOfRangeException.ThrowIfNegative(total); Total = total; }

    public void Record(OperationResult result)
    {
        if (Processed >= Total) throw new InvalidOperationException("실행 대상보다 결과가 많습니다.");
        Processed++;
        switch (result.Status)
        {
            case "완료": Completed++; break;
            case "보류": Deferred++; break;
            case "원본 보존": Preserved++; break;
            case "취소": Canceled++; break;
            case "건너뜀": Skipped++; break;
            default: Failed++; break;
        }
    }

    public void Finish(bool cancellationRequested, string? error = null)
    { CancellationRequested = cancellationRequested; Error = error; }
}
