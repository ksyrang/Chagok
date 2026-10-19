using System.Diagnostics;
using System.Security.Cryptography;

namespace FileOrganizer.Core;

public sealed class Executor(IRecycleBin recycleBin)
{
    public async Task<OperationResult> ExecuteAsync(ScanPlan plan, PlanEntry entry,
        CancellationToken cancellationToken = default, IProgress<TransferProgress>? progress = null)
    {
        if (!entry.CanExecute) return Result("건너뜀", entry.Reason);
        string? temporary = null;
        var recycled = false;
        var published = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            PathSafety.AssertNoLinks(entry.Source);
            PathSafety.AssertNoLinks(entry.Target);
            if (FileStamp.Read(entry.Source) != entry.SourceStamp)
                return Result("보류", "미리보기 이후 원본 파일이 변경되었습니다. 다시 검사하세요.");
            AssertTarget();
            var parent = Path.GetDirectoryName(entry.Target)!;
            var root = Path.GetPathRoot(parent)!;
            if (!Directory.Exists(root)) throw new IOException("대상 드라이브가 연결되지 않았습니다.");
            if (entry.CreateDestination) Directory.CreateDirectory(parent);
            else if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("이동 폴더가 없습니다.");
            PathSafety.AssertNoLinks(parent);

            // 다른 드라이브에서도 원본을 먼저 지우지 않는다. 복사 검증을 마친 뒤 이름을 확정한다.
            temporary = Path.Combine(parent, $".fileorganizer-{Guid.NewGuid():N}.tmp");
            byte[] digest;
            await using (var source = new FileStream(entry.Source, FileMode.Open, FileAccess.Read,
                FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 131072, FileOptions.Asynchronous))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[131072];
                var clock = Stopwatch.StartNew();
                long copied = 0;
                progress?.Report(new(TransferStage.Copy, 0, source.Length));
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer.AsSpan(0, count));
                    await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    copied += count;
                    // 진행 알림이 파일 크기에 비례해 쌓이지 않도록 약 100ms마다 제한한다.
                    if (progress is not null && clock.ElapsedMilliseconds >= 100)
                    { progress.Report(new(TransferStage.Copy, copied, source.Length)); clock.Restart(); }
                }
                await target.FlushAsync(cancellationToken);
                target.Flush(flushToDisk: true);
                progress?.Report(new(TransferStage.Copy, copied, source.Length));
                digest = hash.GetHashAndReset();
                target.Position = 0;
                progress?.Report(new(TransferStage.VerifyCopy, copied, source.Length));
                var copiedDigest = await SHA256.HashDataAsync(target, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(digest, copiedDigest))
                    throw new IOException("복사한 파일의 무결성 검증에 실패했습니다. 원본은 보존했습니다.");
                if (FileStamp.Read(entry.Source) != entry.SourceStamp)
                    throw new IOException("복사 중 원본이 변경되었습니다. 원본은 보존했습니다.");
            }
            File.SetLastWriteTimeUtc(temporary, entry.SourceStamp!.LastWriteUtc);
            AssertTarget();
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(TransferStage.Publish, entry.SourceStamp!.Length, entry.SourceStamp.Length));
            if (entry.Action == PlanAction.Replace)
            {
                await recycleBin.RecycleAsync(entry.Target, cancellationToken);
                recycled = true;
                if (File.Exists(entry.Target) || Directory.Exists(entry.Target))
                    throw new IOException("기존 대상 파일을 휴지통으로 옮기지 못했습니다.");
            }
            File.Move(temporary, entry.Target, overwrite: false);
            temporary = null;
            published = true;
            // 게시 이후 취소되거나 삭제가 실패해도 검증된 사본과 원본을 모두 보존한다.
            cancellationToken.ThrowIfCancellationRequested();
            PathSafety.AssertNoLinks(entry.Source);
            PathSafety.AssertNoLinks(entry.Target);
            if (FileStamp.Read(entry.Source) != entry.SourceStamp)
                return Result("원본 보존", "대상 복사는 완료했지만 원본이 변경되어 삭제하지 않았습니다.");
            progress?.Report(new(TransferStage.VerifyBeforeDelete, entry.SourceStamp.Length, entry.SourceStamp.Length));
            await using (var target = new FileStream(entry.Target, FileMode.Open, FileAccess.Read,
                FileShare.Read, 131072, FileOptions.Asynchronous))
            await using (var source = new FileStream(entry.Source, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 131072, FileOptions.Asynchronous))
            {
                var publishedDigest = await SHA256.HashDataAsync(target, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(digest, publishedDigest))
                    return Result("원본 보존", "게시된 대상 파일이 변경되어 원본을 삭제하지 않았습니다.");
                var currentSourceDigest = await SHA256.HashDataAsync(source, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(digest, currentSourceDigest) || FileStamp.Read(entry.Source) != entry.SourceStamp)
                    return Result("원본 보존", "원본 내용이 변경되어 삭제하지 않았습니다. 다시 검사하세요.");
                File.Delete(entry.Source);
            }
            return Result("완료", recycled ? "기존 파일은 휴지통에 보관하고 이동했습니다." : "복사 검증 후 원본을 삭제했습니다.");
        }
        catch (OperationCanceledException)
        { return Result(published ? "원본 보존" : "취소", published ? "대상 복사 후 취소되어 원본도 보존했습니다." : "작업을 취소했습니다. 원본은 보존했습니다."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or System.Runtime.InteropServices.COMException or InvalidOperationException)
        { return Result(published ? "원본 보존" : "실패", ex.Message + (recycled ? " 기존 대상 파일은 휴지통에서 확인하세요." : "")); }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }

        void AssertTarget()
        {
            PathSafety.AssertNoLinks(entry.Target);
            if (Directory.Exists(entry.Target)) throw new IOException("이동 위치에 같은 이름의 폴더가 생겼습니다. 다시 검사하세요.");
            if (entry.Action == PlanAction.Replace)
            {
                if (!File.Exists(entry.Target) || FileStamp.Read(entry.Target) != entry.TargetStamp)
                    throw new IOException("미리보기 이후 대상 파일이 변경되었습니다. 다시 검사하세요.");
            }
            else if (File.Exists(entry.Target)) throw new IOException("이동 위치에 파일이 생겼습니다. 다시 검사하세요.");
        }
        OperationResult Result(string status, string message) => new(DateTime.UtcNow, plan.FolderName,
            entry.RuleName, entry.Source, entry.Target, entry.Action.ToString(), status, message);
    }
}
