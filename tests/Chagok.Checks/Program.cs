using Chagok.Core;
using Chagok.Windows;

var tests = new List<(string Name, Func<Task> Run)>();
void Add(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
void Async(string name, Func<Task> action) => tests.Add((name, action));
void Assert(bool value, string message = "검증 실패") { if (!value) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception(typeof(T).Name + " 예외가 발생하지 않았습니다.");
}

Add("파일명과 확장자는 대소문자를 구분하지 않음", () =>
{
    var rule = new RuleSpec { Pattern = "REPORT", Extensions = ".pdf, .docx" };
    Assert(RuleMatcher.Matches(rule, "/files/report_10.PDF", 0));
    Assert(!RuleMatcher.Matches(rule, "/files/report_10.jpg", 0));
    Assert(!RuleMatcher.Matches(rule, "/files/memo.PDF", 0));
});
Add("시작 단어·와일드카드·이름 제한 없음", () =>
{
    var r = new RuleSpec { Match = NameMatch.StartsWith, Pattern = "IMG_" };
    Assert(RuleMatcher.Matches(r, "/files/img_01.jpg", 0));
    r.Match = NameMatch.Glob; r.Pattern = "report_??.pdf";
    Assert(RuleMatcher.Matches(r, "/files/REPORT_10.PDF", 0));
    Assert(!RuleMatcher.Matches(r, "/files/report_100.pdf", 0));
    r.Match = NameMatch.Any;
    Assert(RuleMatcher.Matches(r, "/files/all.txt", 0));
});
Add("잘못된 확장자 옵션 거부", () => Throws<ArgumentException>(() => RuleMatcher.ParseExtensions(".p?f")));
Add("미리보기는 파일을 변경하지 않음", () =>
{
    using var f = new Fixture(); f.Write("보고서.pdf", "data");
    var p = f.Scan();
    Assert(p.Entries.Count == 1 && p.Entries[0].Action == PlanAction.Move);
    Assert(File.Exists(f.SourceFile("보고서.pdf")) && !File.Exists(f.TargetFile("보고서.pdf")));
});
Add("하위 폴더 옵션과 제외 폴더", () =>
{
    using var f = new Fixture(); f.Write("top.pdf", "a"); f.Write("sub/child.pdf", "b"); f.Write("excluded/no.pdf", "c");
    f.Folder.ExcludedFolders.Add(f.SourceFile("excluded"));
    Assert(f.Scan().Entries.Count == 1);
    f.Rule.Recursive = true;
    Assert(f.Scan().Entries.Count == 2);
});
Add("이동 폴더는 재검사에서 자동 제외", () =>
{
    using var f = new Fixture(); f.Rule.Recursive = true;
    f.Rule.Destination = f.SourceFile("sorted");
    f.Write("top.pdf", "a"); f.Write("sorted/previous.pdf", "b");
    Assert(f.Scan().Entries.Count == 1);
});
Add("다른 작업의 이동 위치도 검사 폴더로 등록 가능", () =>
{
    using var f = new Fixture(); f.Write("a.pdf", "a");
    Assert(new Planner().Scan(f.Folder, [f.Folder.Path, f.Rule.Destination]).Entries.Count == 1);
});
Add("규칙 순서에서 첫 일치 규칙만 적용", () =>
{
    using var f = new Fixture(); f.Write("a.pdf", "a");
    f.Folder.Rules.Add(new RuleSpec { Name = "second", Match = NameMatch.Any, Destination = f.Target });
    Assert(f.Scan().Entries.Single().RuleName == f.Rule.Name);
    f.Rule.Enabled = false;
    Assert(f.Scan().Entries.Single().RuleName == "second");
});
Add("프로그램 감지 요청은 지정된 규칙만 검사", () =>
{
    using var f = new Fixture(); f.Write("a.pdf", "a");
    var p = new Planner().Scan(f.Folder, [f.Target], new HashSet<Guid>());
    Assert(p.Entries.Count == 0);
});
Add("규칙별 검사는 다른 선행 규칙을 적용하지 않음", () =>
{
    using var f = new Fixture(); f.Write("보고서.pdf", "a"); f.Write("메모.pdf", "b");
    var selected = new RuleSpec { Name = "보고서만", Pattern = "보고서", Destination = Path.Combine(f.Root, "second") };
    f.Folder.Rules.Add(selected);
    var p = new Planner().Scan(f.Folder, [f.Target, selected.Destination], new HashSet<Guid> { selected.Id });
    Assert(p.Entries.Count == 1 && p.Entries[0].RuleName == selected.Name && Path.GetFileName(p.Entries[0].Source) == "보고서.pdf");
});
Add("제외 폴더 표의 경로 정규화·중복 제거·범위 검사", () =>
{
    using var f = new Fixture(); var child = f.SourceFile("excluded");
    var list = PathSafety.NormalizeExclusions(f.Source, [child, child + Path.DirectorySeparatorChar, " "]);
    Assert(list.Count == 1 && list[0] == child);
    Throws<ArgumentException>(() => PathSafety.NormalizeExclusions(f.Source, [f.Source]));
    Throws<ArgumentException>(() => PathSafety.NormalizeExclusions(f.Source, [f.Target]));
    Throws<ArgumentException>(() => PathSafety.NormalizeExclusions(f.Target, list));
});
Add("실제 파일과 계획 내부 이름 충돌 번호 계산", () =>
{
    using var f = new Fixture(); f.Rule.Recursive = true;
    f.Write("a/same.pdf", "a"); f.Write("b/same.pdf", "b");
    Directory.CreateDirectory(f.Target); File.WriteAllText(f.TargetFile("same.pdf"), "old");
    var p = f.Scan();
    Assert(p.Entries.Count == 2);
    Assert(p.Entries[0].Target.EndsWith("same (1).pdf") && p.Entries[1].Target.EndsWith("same (2).pdf"));
});
Add("건너뛰기 옵션과 폴더 충돌 보류", () =>
{
    using var f = new Fixture(); f.Write("same.pdf", "a");
    Directory.CreateDirectory(f.Target); File.WriteAllText(f.TargetFile("same.pdf"), "old");
    f.Rule.Conflict = ConflictMode.Skip;
    Assert(f.Scan().Entries.Single().Action == PlanAction.Skip);
    File.Delete(f.TargetFile("same.pdf")); Directory.CreateDirectory(f.TargetFile("same.pdf"));
    Assert(f.Scan().Entries.Single().Action == PlanAction.Blocked);
});
Add("없는 이동 폴더 자동 생성 끄면 보류", () =>
{
    using var f = new Fixture(); f.Write("a.pdf", "a"); f.Rule.CreateDestination = false;
    Assert(f.Scan().Entries.Single().Action == PlanAction.Blocked);
});
Add("같은 위치 이동 거부", () =>
{
    using var f = new Fixture(); f.Rule.Destination = f.Folder.Path;
    Throws<ArgumentException>(() => f.Scan());
});
Add("취소된 검사는 파일을 변경하지 않음", () =>
{
    using var f = new Fixture(); f.Write("a.pdf", "a");
    using var c = new CancellationTokenSource(); c.Cancel();
    Throws<OperationCanceledException>(() => new Planner().Scan(f.Folder, [f.Target], cancellationToken: c.Token));
    Assert(File.Exists(f.SourceFile("a.pdf")));
});
Async("정상 이동은 복사 검증 후 원본 제거", async () =>
{
    using var f = new Fixture(); f.Write("a.pdf", new string('가', 200000)); var p = f.Scan();
    var result = await f.Execute(p);
    Assert(result.Status == "완료", result.Message);
    Assert(!File.Exists(f.SourceFile("a.pdf")) && File.ReadAllText(f.TargetFile("a.pdf")) == new string('가', 200000));
});
Async("미리보기 후 원본 변경 시 보류", async () =>
{
    using var f = new Fixture(); f.Write("a.pdf", "original"); var p = f.Scan(); f.Write("a.pdf", "changed data");
    var r = await f.Execute(p);
    Assert(r.Status == "보류" && File.Exists(f.SourceFile("a.pdf")) && !File.Exists(f.TargetFile("a.pdf")));
});
Async("미리보기 후 대상 생성 시 덮어쓰지 않음", async () =>
{
    using var f = new Fixture(); f.Write("a.pdf", "new"); var p = f.Scan();
    Directory.CreateDirectory(f.Target); File.WriteAllText(f.TargetFile("a.pdf"), "unexpected");
    var r = await f.Execute(p);
    Assert(r.Status == "실패" && File.ReadAllText(f.TargetFile("a.pdf")) == "unexpected" && File.Exists(f.SourceFile("a.pdf")));
});
Async("기존 파일 교체는 휴지통 인터페이스를 거침", async () =>
{
    using var f = new Fixture(); f.Write("a.pdf", "new");
    Directory.CreateDirectory(f.Target); File.WriteAllText(f.TargetFile("a.pdf"), "old");
    f.Rule.Conflict = ConflictMode.Replace; var p = f.Scan();
    var bin = new TestBin(f.Root); var r = await f.Execute(p, bin);
    Assert(r.Status == "완료", r.Message);
    Assert(File.ReadAllText(bin.LastRecycled!) == "old" && File.ReadAllText(f.TargetFile("a.pdf")) == "new");
});
Async("휴지통 실패 시 두 파일 모두 보존", async () =>
{
    using var f = new Fixture(); f.Write("a.pdf", "new");
    Directory.CreateDirectory(f.Target); File.WriteAllText(f.TargetFile("a.pdf"), "old");
    f.Rule.Conflict = ConflictMode.Replace; var p = f.Scan();
    var r = await f.Execute(p, new TestBin(f.Root) { Fail = true });
    Assert(r.Status == "실패" && File.ReadAllText(f.SourceFile("a.pdf")) == "new" && File.ReadAllText(f.TargetFile("a.pdf")) == "old");
    Assert(!Directory.EnumerateFiles(f.Target, ".chagok-*").Any());
});
Async("미리보기 후 교체 대상 변경 시 보존", async () =>
{
    using var f = new Fixture(); f.Write("a.pdf", "new");
    Directory.CreateDirectory(f.Target); File.WriteAllText(f.TargetFile("a.pdf"), "old");
    f.Rule.Conflict = ConflictMode.Replace; var p = f.Scan(); File.WriteAllText(f.TargetFile("a.pdf"), "modified old");
    var r = await f.Execute(p);
    Assert(r.Status == "실패" && File.ReadAllText(f.TargetFile("a.pdf")) == "modified old");
});
Async("복사 이후 원본 내용 변경 시 삭제하지 않음", async () =>
{
    using var f = new Fixture(); f.Write("a.pdf", "1111");
    Directory.CreateDirectory(f.Target); File.WriteAllText(f.TargetFile("a.pdf"), "old");
    f.Rule.Conflict = ConflictMode.Replace; var p = f.Scan();
    var stamp = File.GetLastWriteTimeUtc(f.SourceFile("a.pdf"));
    var bin = new TestBin(f.Root) { BeforeRecycle = () => { File.WriteAllText(f.SourceFile("a.pdf"), "2222"); File.SetLastWriteTimeUtc(f.SourceFile("a.pdf"), stamp); } };
    var r = await f.Execute(p, bin);
    Assert(r.Status == "원본 보존" && File.ReadAllText(f.SourceFile("a.pdf")) == "2222", r.Message);
});
Async("취소된 실행은 원본 보존", async () =>
{
    using var f = new Fixture(); f.Write("a.pdf", "original"); var p = f.Scan();
    using var c = new CancellationTokenSource(); c.Cancel();
    var r = await new Executor(new TestBin(f.Root)).ExecuteAsync(p, p.Entries.Single(), c.Token);
    Assert(r.Status == "취소" && File.Exists(f.SourceFile("a.pdf")));
});
Add("설정 저장·재로드와 이전 설정 백업", () =>
{
    using var f = new Fixture(); var store = new Storage(Path.Combine(f.Root, "state"));
    var settings = new AppSettings { Theme = ThemeMode.Dark, Folders = [f.Folder] }; store.Save(settings);
    settings.Theme = ThemeMode.Light; store.Save(settings);
    Assert(store.Load().Theme == ThemeMode.Light && File.Exists(store.SettingsPath + ".backup"));
    var loaded = store.Load(); Assert(loaded.Folders[0].Rules[0].Id == f.Rule.Id);
});
Add("새 설정과 이전 버전 설정은 트레이 숨기기·종료 확인을 기본으로 사용", () =>
{
    using var f = new Fixture(); var store = new Storage(Path.Combine(f.Root, "state"));
    var fresh = store.Load();
    Assert(fresh.CloseAction == WindowCloseAction.HideToTray && fresh.ConfirmExitOnClose);
    Directory.CreateDirectory(store.DirectoryPath);
    File.WriteAllText(store.SettingsPath, "{\"Version\":1,\"Theme\":\"Dark\",\"Folders\":[]}");
    var legacy = store.Load();
    Assert(legacy.Theme == ThemeMode.Dark && legacy.CloseAction == WindowCloseAction.HideToTray && legacy.ConfirmExitOnClose);
});
Add("종료 동작·확인 생략 저장과 복원은 기존 규칙을 보존", () =>
{
    using var f = new Fixture(); var store = new Storage(Path.Combine(f.Root, "state"));
    var settings = new AppSettings { Folders = [f.Folder], CloseAction = WindowCloseAction.Exit, ConfirmExitOnClose = false };
    store.Save(settings);
    var loaded = store.Load();
    Assert(loaded.CloseAction == WindowCloseAction.Exit && !loaded.ConfirmExitOnClose);
    Assert(loaded.Folders.Single().Rules.Single().Id == f.Rule.Id);
    loaded.CloseAction = WindowCloseAction.HideToTray; store.Save(loaded);
    var hidden = store.Load(); Assert(!hidden.ConfirmExitOnClose);
    hidden.CloseAction = WindowCloseAction.Exit; hidden.ConfirmExitOnClose = true; store.Save(hidden);
    var restored = store.Load(); Assert(restored.CloseAction == WindowCloseAction.Exit && restored.ConfirmExitOnClose);
    Assert(restored.Folders.Single().Rules.Single().Id == f.Rule.Id);
});
Add("알 수 없는 창 닫기 동작은 설정을 보존하고 거부", () =>
{
    using var f = new Fixture(); var store = new Storage(Path.Combine(f.Root, "state"));
    Directory.CreateDirectory(store.DirectoryPath);
    foreach (var value in new[] { "99", "\"Unknown\"" })
    {
        var json = "{\"Version\":1,\"CloseAction\":" + value + "}";
        File.WriteAllText(store.SettingsPath, json);
        if (value == "99") Throws<InvalidDataException>(() => store.Load());
        else Throws<System.Text.Json.JsonException>(() => store.Load());
        Assert(File.ReadAllText(store.SettingsPath) == json);
    }
});
Add("손상된 설정을 기본값으로 덮어쓰지 않음", () =>
{
    using var f = new Fixture(); var store = new Storage(Path.Combine(f.Root, "state"));
    Directory.CreateDirectory(store.DirectoryPath); File.WriteAllText(store.SettingsPath, "{broken");
    Throws<System.Text.Json.JsonException>(() => store.Load());
    Assert(File.ReadAllText(store.SettingsPath) == "{broken");
});
Add("기록 마지막 줄이 깨져도 이전 기록 표시", () =>
{
    using var f = new Fixture(); var store = new Storage(Path.Combine(f.Root, "state"));
    store.Append(new(DateTime.UtcNow, "folder", "rule", "a", "b", "Move", "완료", "ok"));
    File.AppendAllText(store.HistoryPath, "{truncated");
    Assert(store.ReadHistory().Count == 1);
});
Add("설정의 null 필드와 중복 ID 거부", () =>
{
    using var f = new Fixture(); var store = new Storage(Path.Combine(f.Root, "state"));
    Directory.CreateDirectory(store.DirectoryPath);
    File.WriteAllText(store.SettingsPath, "{\"Version\":1,\"Folders\":[{\"Name\":null}]}");
    Throws<InvalidDataException>(() => store.Load());
    var settings = new AppSettings { Folders = [f.Folder, Storage.Clone(f.Folder)] };
    store.Save(settings);
    Throws<InvalidDataException>(() => store.Load());
});
Add("macOS 메타데이터 파일은 정리 대상에서 제외", () =>
{
    using var f = new Fixture(); f.Write("._memo.pdf", "metadata"); f.Write("memo.pdf", "data");
    var p = f.Scan(); Assert(p.Entries.Count == 1 && Path.GetFileName(p.Entries[0].Source) == "memo.pdf");
});
Add("다른 실행이 잠근 파일은 미리보기에서 보류", () =>
{
    using var f = new Fixture(); f.Write("a.pdf", "a");
    using var locked = new FileStream(f.SourceFile("a.pdf"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    Assert(f.Scan().Entries.Single().Action == PlanAction.Blocked);
});
if (!OperatingSystem.IsWindows())
{
    Add("링크 하위 폴더를 따라가지 않음", () =>
    {
        using var f = new Fixture(); f.Rule.Recursive = true;
        Directory.CreateDirectory(f.Target); File.WriteAllText(f.TargetFile("secret.pdf"), "secret");
        Directory.CreateSymbolicLink(f.SourceFile("link"), f.Target);
        Assert(f.Scan().Entries.Count == 0);
    });
    Add("링크 대상 폴더 거부", () =>
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.Target);
        var link = Path.Combine(f.Root, "linked-target"); Directory.CreateSymbolicLink(link, f.Target);
        f.Rule.Destination = link; f.Write("a.pdf", "a");
        Throws<IOException>(() => f.Scan());
    });
    Async("검사 후 이동 폴더가 링크로 바뀌면 보존", async () =>
    {
        using var f = new Fixture(); f.Write("a.pdf", "a"); var p = f.Scan();
        var other = Path.Combine(f.Root, "other"); Directory.CreateDirectory(other);
        Directory.CreateSymbolicLink(f.Target, other);
        var r = await f.Execute(p);
        Assert(r.Status == "실패" && File.Exists(f.SourceFile("a.pdf")) && !File.Exists(Path.Combine(other, "a.pdf")));
    });
    Async("작업 드라이브에서 내부 임시 드라이브로 이동", async () =>
    {
        using var f = new Fixture(external: true); f.Write("a.pdf", "cross-volume");
        var target = Path.Combine("/private/tmp", "chagok-cross-volume-" + Guid.NewGuid().ToString("N"));
        try
        {
            f.Rule.Destination = target;
            var p = f.Scan(); var r = await f.Execute(p);
            Assert(r.Status == "완료" && File.ReadAllText(Path.Combine(target, "a.pdf")) == "cross-volume", r.Message);
        }
        finally { if (Directory.Exists(target)) Directory.Delete(target, true); }
    });
}

Add("디스크 검사도 정렬·충돌·규칙 범위가 기존 검사와 같음", () =>
{
    using var f = new Fixture(); f.Rule.Recursive = true;
    for (var i = 0; i < 605; i++) f.Write($"flat-{i:0000}.pdf", "data");
    f.Write("a/same.pdf", "one"); f.Write("b/same.pdf", "two"); f.Write("a.pdf", "three");
    var extra = new RuleSpec { Name = "선택 규칙", Match = NameMatch.Any, Extensions = ".pdf", Recursive = true, Destination = f.Target };
    f.Folder.Rules.Add(extra);
    var ids = new HashSet<Guid> { extra.Id };
    var memory = new Planner().Scan(f.Folder, [f.Target], ids);
    using var disk = new Planner().ScanToDisk(f.Folder, [f.Target], Path.Combine(f.Root, "cache"), ids);
    Assert(disk.Count == memory.Entries.Count && disk.ExecutableCount == memory.Entries.Count(e => e.CanExecute));
    var actual = new List<PlanEntry>();
    for (long offset = 0; offset < disk.Count; offset += 100) actual.AddRange(disk.ReadPage(offset).Select(r => r.Entry));
    Assert(actual.Select(e => (e.Source, e.Target, e.RuleName, e.Action, e.SourceStamp)).SequenceEqual(
        memory.Entries.Select(e => (e.Source, e.Target, e.RuleName, e.Action, e.SourceStamp))));
    Assert(File.Exists(f.SourceFile("a/same.pdf")) && !Directory.Exists(f.Target));
});
Add("페이지 간 선택 유지·전체 선택은 보이지 않는 항목에도 적용", () =>
{
    using var f = new Fixture();
    using var disk = new DiskPlanStore(Path.Combine(f.Root, "cache"), f.Folder);
    for (var i = 0; i < 1203; i++) disk.AddEntry(new(Guid.NewGuid(), $"source-{i}", $"target-{i}", "rule",
        i == 1202 ? PlanAction.Blocked : i == 1001 ? PlanAction.Replace : PlanAction.Move, "", null, null, true));
    disk.CompleteScan();
    Assert(disk.Count == 1203 && disk.SelectedCount == 1202 && disk.SelectedReplaceCount == 1);
    var page = disk.ReadPage(1000); Assert(page.Count == 100 && page[0].Position == 1001);
    disk.SetSelected(1002, false); disk.SetSelected(1, false); disk.SetSelected(1203, true);
    Assert(disk.SelectedCount == 1200 && disk.SelectedReplaceCount == 0 && !disk.ReadPage(1000)[1].Selected);
    disk.SetAllSelected(false); Assert(disk.SelectedCount == 0 && disk.ReadSelectedPage(0).Count == 0);
    disk.SetAllSelected(true); Assert(disk.SelectedCount == 1202 && disk.SelectedReplaceCount == 1);
    long cursor = 0, total = 0;
    while (true)
    {
        var batch = disk.ReadSelectedPage(cursor); if (batch.Count == 0) break;
        Assert(batch.Count <= 100 && batch.All(r => r.Entry.CanExecute));
        total += batch.Count; cursor = batch[^1].Position;
    }
    Assert(total == 1202 && disk.ReadPage(1200).Count == 3 && disk.ReadPage(1300).Count == 0);
    Throws<ArgumentOutOfRangeException>(() => disk.ReadPage(0, 10000));
});
Async("디스크 선택 목록은 페이지 밖 항목을 실행하고 해제 항목은 보존", async () =>
{
    using var f = new Fixture();
    for (var i = 0; i < 203; i++) f.Write($"{i:000}.pdf", "data");
    using var disk = new Planner().ScanToDisk(f.Folder, [f.Target], Path.Combine(f.Root, "cache"));
    disk.SetAllSelected(false); disk.SetSelected(1, true); disk.SetSelected(203, true);
    var executor = new Executor(new TestBin(f.Root)); long cursor = 0; var done = 0;
    while (true)
    {
        var rows = disk.ReadSelectedPage(cursor, 1); if (rows.Count == 0) break;
        var result = await executor.ExecuteAsync(disk.Header, rows[0].Entry);
        Assert(result.Status == "완료", result.Message); done++; cursor = rows[0].Position;
    }
    Assert(done == 2 && File.Exists(f.SourceFile("001.pdf")) && !File.Exists(f.SourceFile("000.pdf")) && !File.Exists(f.SourceFile("202.pdf")));
});
Add("디스크 검사 취소 후 임시 저장소 정리", () =>
{
    using var f = new Fixture(); f.Write("one.pdf", "data");
    var root = Path.Combine(f.Root, "cache");
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    Throws<OperationCanceledException>(() => new Planner().ScanToDisk(f.Folder, [f.Target], root, cancellationToken: cancel.Token));
    Assert(!Directory.EnumerateDirectories(root).Any() && File.Exists(f.SourceFile("one.pdf")));
});
Add("사용 중인 디스크 검사 보존 및 폐기 시 캐시 삭제", () =>
{
    using var f = new Fixture(); var root = Path.Combine(f.Root, "cache");
    var first = new DiskPlanStore(root, f.Folder); var firstPath = first.DirectoryPath;
    using (var second = new DiskPlanStore(root, f.Folder)) Assert(Directory.Exists(firstPath));
    first.Dispose(); Assert(!Directory.Exists(firstPath));
    var abandoned = Path.Combine(root, "scan-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(abandoned); File.WriteAllText(Path.Combine(abandoned, "lease"), ""); File.WriteAllText(Path.Combine(abandoned, "plan.db"), "");
    using var third = new DiskPlanStore(root, f.Folder); Assert(!Directory.Exists(abandoned));
});
Add("검사 폴더 내부의 임시 저장소는 검사에서 제외", () =>
{
    using var f = new Fixture(); f.Rule.Extensions = ""; f.Rule.Recursive = true; f.Write("one.pdf", "data");
    using var disk = new Planner().ScanToDisk(f.Folder, [f.Target], f.SourceFile("cache"));
    Assert(disk.Count == 1 && Path.GetFileName(disk.ReadPage(0)[0].Entry.Source) == "one.pdf");
});
Add("디스크 경고·예약 경로는 배치를 넘어 유지", () =>
{
    using var f = new Fixture(); using var disk = new DiskPlanStore(Path.Combine(f.Root, "cache"), f.Folder);
    for (var i = 0; i < 1100; i++) { disk.Reserve($"/path/{i}"); disk.AddWarning($"warning-{i}"); }
    disk.CompleteScan();
    Assert(disk.IsReserved("/path/0") && disk.IsReserved("/path/1099") && !disk.IsReserved("/path/none"));
    Assert(disk.WarningCount == 1100 && disk.ReadWarnings(5).SequenceEqual(Enumerable.Range(0, 5).Select(i => $"warning-{i}")));
});
Add("1만 긴 경로 결과를 저장해도 관리 메모리에 전체 결과를 보관하지 않음", () =>
{
    using var f = new Fixture(); using var disk = new DiskPlanStore(Path.Combine(f.Root, "cache"), f.Folder);
    disk.AddEntry(new(Guid.NewGuid(), "warmup", "warmup", "rule", PlanAction.Move, "", null, null, true)); disk.Flush();
    var before = GC.GetTotalMemory(true); var prefix = new string('가', 1000);
    for (var i = 0; i < 10000; i++) disk.AddEntry(new(Guid.NewGuid(), prefix + i, prefix + "target" + i, "rule", PlanAction.Move, "", null, null, true));
    disk.CompleteScan(); var growth = GC.GetTotalMemory(true) - before;
    Console.WriteLine($"  디스크 계획 10,001건 관리 메모리 증가: {growth:N0} bytes (WPF 전체 메모리 아님)");
    Assert(disk.Count == 10001 && growth < 8 * 1024 * 1024, $"전체 결과 보관 의심: {growth:N0} bytes");
});

Add("임시 저장소 자체를 검사하지 않고 잘못된 저장 위치는 원본을 보존", () =>
{
    using var f = new Fixture(); f.Write("one.pdf", "data");
    Throws<ArgumentException>(() => new Planner().ScanToDisk(f.Folder, [f.Target], f.Source));
    var fileRoot = Path.Combine(f.Root, "not-a-folder"); File.WriteAllText(fileRoot, "keep");
    Throws<IOException>(() => new Planner().ScanToDisk(f.Folder, [f.Target], fileRoot));
    Assert(File.ReadAllText(fileRoot) == "keep" && File.Exists(f.SourceFile("one.pdf")));
});
Async("디스크에 저장했던 교체 계획도 원본 변경 시 두 파일 보존", async () =>
{
    using var f = new Fixture(); f.Write("one.pdf", "new"); Directory.CreateDirectory(f.Target);
    File.WriteAllText(f.TargetFile("one.pdf"), "old"); f.Rule.Conflict = ConflictMode.Replace;
    using var disk = new Planner().ScanToDisk(f.Folder, [f.Target], Path.Combine(f.Root, "cache"));
    var entry = disk.ReadSelectedPage(0).Single().Entry;
    Assert(entry.TargetStamp is not null && entry.Action == PlanAction.Replace);
    File.WriteAllText(f.SourceFile("one.pdf"), "changed source length");
    var result = await new Executor(new TestBin(f.Root)).ExecuteAsync(disk.Header, entry);
    Assert(result.Status == "보류" && File.ReadAllText(f.TargetFile("one.pdf")) == "old" && File.Exists(f.SourceFile("one.pdf")));
});

Add("실행 요약은 성공·보류·실패·원본 보존과 미실행을 구분", () =>
{
    var report = new ExecutionReport(5);
    foreach (var status in new[] { "완료", "보류", "실패", "원본 보존" })
        report.Record(new(DateTime.UtcNow, "folder", "rule", "source", "target", "Move", status, "message"));
    report.Finish(false);
    Assert(report.Processed == 4 && report.Completed == 1 && report.Deferred == 1 && report.Failed == 1 && report.Preserved == 1);
    Assert(report.Remaining == 1 && report.Percent == 80 && report.Outcome == ExecutionOutcome.Attention);
    Assert(report.CountsText.Contains("선택 5개 기준") && report.CountsText.Contains("미실행 1"));
});
Add("취소·기록 오류·늦은 취소가 성공으로 잘못 표시되지 않음", () =>
{
    var canceled = new ExecutionReport(3); canceled.Finish(true);
    Assert(canceled.Outcome == ExecutionOutcome.Canceled && canceled.Remaining == 3 && canceled.Percent == 0);
    var failed = new ExecutionReport(1); failed.Record(new(DateTime.UtcNow, "", "", "", "", "", "완료", ""));
    failed.Finish(false, "기록 오류"); Assert(failed.Outcome == ExecutionOutcome.Error && failed.Remaining == 0);
    var done = new ExecutionReport(1); done.Record(new(DateTime.UtcNow, "", "", "", "", "", "완료", ""));
    done.Finish(true); Assert(done.Outcome == ExecutionOutcome.Success && done.Percent == 100);
    Throws<InvalidOperationException>(() => done.Record(new(DateTime.UtcNow, "", "", "", "", "", "완료", "")));
    Assert(new ExecutionReport(0).Percent == 0);
});
Add("원본 보존·건너뜀·알 수 없는 결과는 정상 완료가 아님", () =>
{
    foreach (var status in new[] { "원본 보존", "건너뜀", "취소", "알 수 없는 결과" })
    {
        var report = new ExecutionReport(1); report.Record(new(DateTime.UtcNow, "", "", "", "", "", status, "")); report.Finish(false);
        Assert(report.Outcome != ExecutionOutcome.Success && report.Percent == 100);
    }
});
Async("실제 복사 진행과 검증 단계가 전달되고 최종 이동은 유지", async () =>
{
    using var f = new Fixture(); f.Write("progress.pdf", new string('a', 2 * 1024 * 1024));
    var plan = f.Scan(); var updates = new List<TransferProgress>();
    var result = await new Executor(new TestBin(f.Root)).ExecuteAsync(plan, plan.Entries[0], progress: new CallbackProgress(updates.Add));
    Assert(result.Status == "완료");
    var copy = updates.Where(u => u.Stage == TransferStage.Copy).ToArray();
    Assert(copy.Length >= 2 && copy[0].CopiedBytes == 0 && copy[^1].CopiedBytes == 2 * 1024 * 1024);
    Assert(copy.All(u => u.CopiedBytes >= 0 && u.CopiedBytes <= u.TotalBytes));
    Assert(copy.Select(u => u.CopiedBytes).SequenceEqual(copy.Select(u => u.CopiedBytes).Order()));
    Assert(updates.Any(u => u.Stage == TransferStage.VerifyCopy) && updates.Any(u => u.Stage == TransferStage.Publish)
        && updates[^1].Stage == TransferStage.VerifyBeforeDelete);
    Assert(!File.Exists(f.SourceFile("progress.pdf")) && File.Exists(f.TargetFile("progress.pdf")));
});
Async("복사 진행 알림에서 취소해도 원본을 보존하고 검증 완료로 표시하지 않음", async () =>
{
    using var f = new Fixture(); f.Write("cancel-progress.pdf", "data"); var plan = f.Scan();
    using var cancel = new CancellationTokenSource(); var updates = new List<TransferProgress>();
    var observer = new CallbackProgress(value => { updates.Add(value); cancel.Cancel(); });
    var result = await new Executor(new TestBin(f.Root)).ExecuteAsync(plan, plan.Entries[0], cancel.Token, observer);
    Assert(result.Status == "취소" && File.Exists(f.SourceFile("cancel-progress.pdf")) && !File.Exists(f.TargetFile("cancel-progress.pdf")));
    Assert(updates.Count > 0 && updates.All(u => u.Stage == TransferStage.Copy));
});
Add("파일별 결과를 디스크에 보관하고 페이지 이동 뒤에도 유지", () =>
{
    using var f = new Fixture(); using var disk = new DiskPlanStore(Path.Combine(f.Root, "cache"), f.Folder);
    for (var i = 0; i < 203; i++) disk.AddEntry(new(Guid.NewGuid(), $"s{i}", $"t{i}", "rule", PlanAction.Move, "", null, null, true));
    disk.CompleteScan();
    var result = new OperationResult(DateTime.UtcNow, "test", "rule", "s200", "t200", "Move", "원본 보존", "대상 복사 후 원본 보존");
    disk.StoreResult(201, result);
    Assert(disk.ReadPage(0).All(r => r.Result is null));
    Assert(disk.ReadPage(200)[0].Result == result && disk.ReadPage(200)[1].Result is null);
    Assert(disk.SelectedCount == 203);
    Throws<InvalidDataException>(() => disk.StoreResult(201, result));
    Throws<InvalidDataException>(() => disk.StoreResult(999, result));
});

Add("결과 저장 오류 시 실제 미실행으로 단정하지 않고 확인 필요로 표시", () =>
{
    var entry = new PlanEntry(Guid.NewGuid(), "source", "target", "rule", PlanAction.Move, "일치", null, null, true);
    var unprocessed = new PreviewRow(entry); unprocessed.Finish();
    Assert(unprocessed.ResultText == "미실행" && !unprocessed.CanExecute);
    var unknown = new PreviewRow(entry); unknown.Finish(resultsIncomplete: true);
    Assert(unknown.ResultText == "미확인" && unknown.ResultMessage.Contains("실제 파일 위치"));
    var known = new PreviewRow(entry); known.SetResult(new(DateTime.UtcNow, "", "", "source", "target", "Move", "완료", "이동 완료"));
    known.Finish(resultsIncomplete: true); Assert(known.ResultText == "완료");
    var excluded = new PreviewRow(entry with { Action = PlanAction.Blocked }); excluded.Finish(resultsIncomplete: true);
    Assert(excluded.ResultText == "제외");
});


Add("폴더 복제는 규칙 전체를 독립된 새 ID로 만들고 원본을 보존", () =>
{
    using var f = new Fixture();
    f.Folder.Trigger = TriggerMode.ProgramStopped; f.Folder.ProgramPath = Path.Combine(f.Root, "tool.exe");
    f.Folder.RunMode = RunMode.Automatic;
    f.Folder.ExcludedFolders.Add(Path.Combine(f.Source, "temp"));
    f.Rule.Enabled = false; f.Rule.Recursive = true; f.Rule.Conflict = ConflictMode.Skip;
    var copy = FolderDuplicator.Create(f.Folder);
    Assert(copy.Id != f.Folder.Id && copy.Name == f.Folder.Name + " 복사본");
    Assert(copy.Trigger == f.Folder.Trigger && copy.ProgramPath == f.Folder.ProgramPath && copy.RunMode == f.Folder.RunMode);
    Assert(copy.Rules.Single().Id != f.Rule.Id && !copy.Rules[0].Enabled && copy.Rules[0].Recursive);
    Assert(copy.Rules[0].Destination == f.Rule.Destination && copy.Rules[0].Conflict == ConflictMode.Skip);
    copy.Rules[0].Name = "changed"; copy.ExcludedFolders.Clear();
    Assert(f.Rule.Name == "test-rule" && f.Folder.ExcludedFolders.Count == 1);
    var storage = new Storage(Path.Combine(f.Root, "state"));
    storage.Save(new AppSettings { Folders = [f.Folder, copy] }); Assert(storage.Load().Folders.Count == 2);
});
Add("복제한 제외 폴더는 새 검사 폴더의 같은 하위 경로로 변경", () =>
{
    using var f = new Fixture();
    var paths = FolderDuplicator.RebaseExclusions(f.Source, f.Target, [Path.Combine(f.Source, "temp", "nested"), Path.Combine(f.Target, "new")]);
    Assert(paths.SequenceEqual(new[] { Path.Combine(f.Target, "temp", "nested"), Path.Combine(f.Target, "new") }));
    Throws<ArgumentException>(() => FolderDuplicator.RebaseExclusions(f.Source, f.Target, [f.Root]));
    Throws<ArgumentException>(() => FolderDuplicator.RebaseExclusions(f.Source, f.Target, [f.Source]));
});
Add("폴더 실행 조건 저장과 이전 설정 기본값 및 손상 값 거부", () =>
{
    using var f = new Fixture(); var storage = new Storage(Path.Combine(f.Root, "state"));
    f.Folder.Trigger = TriggerMode.ProgramStarted; f.Folder.ProgramPath = Path.Combine(f.Root, "tool.exe");
    f.Folder.RunMode = RunMode.Automatic; storage.Save(new AppSettings { Folders = [f.Folder] });
    var loaded = storage.Load().Folders.Single();
    Assert(loaded.Trigger == TriggerMode.ProgramStarted && loaded.RunMode == RunMode.Automatic && loaded.ProgramPath == f.Folder.ProgramPath);
    File.WriteAllText(storage.SettingsPath, "{\"Folders\":[{\"Name\":\"legacy\",\"Path\":\"/legacy\"}]}");
    loaded = storage.Load().Folders.Single(); Assert(loaded.Trigger == TriggerMode.Manual && loaded.RunMode == RunMode.Confirm);
    foreach (var property in new[] { "\"Trigger\":99", "\"RunMode\":99", "\"ProgramPath\":null" })
    {
        var json = "{\"Folders\":[{" + property + "}]}"; File.WriteAllText(storage.SettingsPath, json);
        Throws<InvalidDataException>(() => storage.Load()); Assert(File.ReadAllText(storage.SettingsPath) == json);
    }
});
Add("폴더 감지는 수동·다른 프로그램 규칙도 포함하고 꺼진 규칙은 제외", () =>
{
    using var f = new Fixture(); var program = Path.Combine(f.Root, "tool.exe");
    f.Folder.Trigger = TriggerMode.ProgramStarted; f.Folder.ProgramPath = program;
    var other = new RuleSpec { Match = NameMatch.Any, Destination = f.Target, Trigger = TriggerMode.ProgramStopped, ProgramPath = Path.Combine(f.Root, "other.exe") };
    var disabled = new RuleSpec { Enabled = false }; f.Folder.Rules.AddRange([other, disabled]);
    var settings = new AppSettings { Folders = [f.Folder] };
    var batch = TriggerPlanner.Build(settings, program, TriggerMode.ProgramStarted).Single();
    Assert(!batch.Automatic && batch.RuleIds.SetEquals(new[] { f.Rule.Id, other.Id }));
    f.Write("a.pdf", "data");
    var plan = new Planner().Scan(f.Folder, [f.Target], batch.RuleIds);
    Assert(plan.Entries.Count == 1 && plan.Entries[0].RuleName == f.Rule.Name);
});
Add("규칙 실행 조건은 폴더 조건과 독립적으로 동작", () =>
{
    using var f = new Fixture(); var ruleProgram = Path.Combine(f.Root, "rule.exe");
    f.Folder.Trigger = TriggerMode.ProgramStopped; f.Folder.ProgramPath = Path.Combine(f.Root, "folder.exe");
    f.Rule.Trigger = TriggerMode.ProgramStarted; f.Rule.ProgramPath = ruleProgram; f.Rule.RunMode = RunMode.Automatic;
    var settings = new AppSettings { Folders = [f.Folder] };
    var batch = TriggerPlanner.Build(settings, ruleProgram, TriggerMode.ProgramStarted).Single();
    Assert(batch.Automatic && batch.RuleIds.SetEquals(new[] { f.Rule.Id }));
    Assert(TriggerPlanner.Build(settings, ruleProgram, TriggerMode.ProgramStopped).Count == 0);
});
Add("같은 이벤트의 폴더·규칙 중복은 한 번만 포함하고 목록 확인을 우선", () =>
{
    using var f = new Fixture(); var program = Path.Combine(f.Root, "tool.exe");
    f.Folder.Trigger = f.Rule.Trigger = TriggerMode.ProgramStarted; f.Folder.ProgramPath = f.Rule.ProgramPath = program;
    var settings = new AppSettings { Folders = [f.Folder] };
    foreach (var folderMode in Enum.GetValues<RunMode>())
    foreach (var ruleMode in Enum.GetValues<RunMode>())
    {
        f.Folder.RunMode = folderMode; f.Rule.RunMode = ruleMode;
        var batch = TriggerPlanner.Build(settings, program.ToUpperInvariant(), TriggerMode.ProgramStarted).Single();
        Assert(batch.RuleIds.Count == 1 && batch.RuleIds.Contains(f.Rule.Id));
        Assert(batch.Automatic == (folderMode == RunMode.Automatic && ruleMode == RunMode.Automatic));
    }
});
Add("폴더 전체는 한 목록으로 순서를 유지하고 감지 대상은 중복 제거", () =>
{
    using var f = new Fixture(); var program = Path.Combine(f.Root, "tool.exe");
    f.Folder.Trigger = TriggerMode.ProgramStarted; f.Folder.ProgramPath = program; f.Folder.RunMode = RunMode.Automatic;
    f.Rule.Trigger = TriggerMode.ProgramStarted; f.Rule.ProgramPath = program; f.Rule.RunMode = RunMode.Confirm;
    var manualRule = new RuleSpec { Match = NameMatch.Any, Destination = f.Target }; f.Folder.Rules.Add(manualRule);
    var settings = new AppSettings { Folders = [f.Folder] };
    var batches = TriggerPlanner.Build(settings, program, TriggerMode.ProgramStarted);
    Assert(batches.Count == 1 && !batches[0].Automatic);
    Assert(batches[0].RuleIds.SetEquals(new[] { f.Rule.Id, manualRule.Id }));
    f.Rule.Conflict = ConflictMode.Skip; f.Write("a.pdf", "source");
    Directory.CreateDirectory(f.Target); File.WriteAllText(f.TargetFile("a.pdf"), "existing");
    var plan = new Planner().Scan(f.Folder, [f.Target], batches[0].RuleIds);
    Assert(plan.Entries.Count == 1 && plan.Entries[0].Action == PlanAction.Skip && plan.Entries[0].RuleName == f.Rule.Name);
    Assert(TriggerPlanner.ProgramPaths(settings).Count == 1);
    foreach (var rule in f.Folder.Rules) rule.Enabled = false;
    Assert(TriggerPlanner.ProgramPaths(settings).Count == 0 && TriggerPlanner.Build(settings, program, TriggerMode.ProgramStarted).Count == 0);
});
Add("폴더 이벤트가 아니면 규칙별 확인·자동 목록이 서로 겹치지 않음", () =>
{
    using var f = new Fixture(); var program = Path.Combine(f.Root, "tool.exe");
    f.Rule.Trigger = TriggerMode.ProgramStarted; f.Rule.ProgramPath = program;
    var automatic = Storage.Clone(f.Rule); automatic.Id = Guid.NewGuid(); automatic.RunMode = RunMode.Automatic;
    f.Folder.Rules.Add(automatic);
    var batches = TriggerPlanner.Build(new AppSettings { Folders = [f.Folder] }, program, TriggerMode.ProgramStarted);
    Assert(batches.Count == 2 && batches.Sum(b => b.RuleIds.Count) == 2);
    Assert(!batches[0].Automatic && batches[0].RuleIds.SetEquals(new[] { f.Rule.Id }));
    Assert(batches[1].Automatic && batches[1].RuleIds.SetEquals(new[] { automatic.Id }));
});
Add("탐색기 인수는 목적지 하나만 허용하고 명령 경로를 따옴표로 감쌈", () =>
{
    using var f = new Fixture();
    Assert(ShellLaunch.ParseArguments([]) is null);
    Assert(ShellLaunch.ParseArguments(["--add-rule", f.Target]) == PathSafety.Normalize(f.Target));
    Throws<ArgumentException>(() => ShellLaunch.ParseArguments(["--unknown", f.Target]));
    Throws<ArgumentException>(() => ShellLaunch.ParseArguments(["--add-rule", f.Target, "extra"]));
    Throws<ArgumentException>(() => ShellLaunch.ParseArguments(["--add-rule", "relative"]));
    var executable = @"C:\차곡 & Tools\Chagok.exe";
    Assert(ShellLaunch.BuildCommand(executable) == "\"" + executable + "\" --add-rule \"%1\"");
    Throws<ArgumentException>(() => ShellLaunch.BuildCommand("bad\".exe"));
    Throws<ArgumentException>(() => ShellLaunch.BuildCommand("bad\n.exe"));
});
Async("탐색기 전달 형식은 한글·공백 경로를 보존하고 과대·잘린 요청을 거부", async () =>
{
    using var f = new Fixture(); var path = Path.Combine(f.Root, "한글 공백 & 폴더");
    using var stream = new MemoryStream();
    await ShellLaunch.WriteRequestAsync(stream, path, CancellationToken.None); stream.Position = 0;
    Assert(await ShellLaunch.ReadRequestAsync(stream, CancellationToken.None) == path);
    foreach (var length in new[] { -1, 0, 1024 * 1024 })
    {
        using var invalid = new MemoryStream(BitConverter.GetBytes(length));
        try { await ShellLaunch.ReadRequestAsync(invalid, CancellationToken.None); throw new Exception("크기 오류를 수락했습니다."); }
        catch (InvalidDataException) { }
    }
    using var truncated = new MemoryStream([5, 0, 0, 0, 1]);
    try { await ShellLaunch.ReadRequestAsync(truncated, CancellationToken.None); throw new Exception("잘린 요청을 수락했습니다."); }
    catch (EndOfStreamException) { }
});

Add("대체 감지는 처음부터 실행 중인 프로그램을 시작으로 다시 알리지 않음", () =>
{
    using var f = new Fixture(); var path = Path.Combine(f.Root, "tool.exe");
    var tracker = new ProcessPresenceTracker([path]);
    Assert(tracker.Observe([new(1, path)]).Count == 0);
    var ended = tracker.Observe([]).Single(); Assert(ended.Path == path && ended.Trigger == TriggerMode.ProgramStopped);
});
Add("대체 감지는 첫 실행본 시작과 마지막 실행본 종료만 알림", () =>
{
    using var f = new Fixture(); var path = Path.Combine(f.Root, "tool.exe");
    var tracker = new ProcessPresenceTracker([path]); tracker.Observe([]);
    Assert(tracker.Observe([new(1, path)]).Single().Trigger == TriggerMode.ProgramStarted);
    Assert(tracker.Observe([new(1, path), new(2, path)]).Count == 0);
    Assert(tracker.Observe([new(2, path)]).Count == 0);
    Assert(tracker.Observe([]).Single().Trigger == TriggerMode.ProgramStopped);
});
Add("프로그램 경로 접근 실패를 종료로 판단하지 않음", () =>
{
    using var f = new Fixture(); var path = Path.Combine(f.Root, "tool.exe");
    var tracker = new ProcessPresenceTracker([path]); tracker.Observe([new(1, path)]);
    Assert(tracker.Observe([new(1, null)]).Count == 0);
    Assert(tracker.Observe([new(1, null)]).Count == 0);
    Assert(tracker.Observe([new(1, path)]).Count == 0);
    Assert(tracker.Observe([]).Single().Trigger == TriggerMode.ProgramStopped);
});
Add("불완전한 프로세스 목록은 이전 상태를 유지", () =>
{
    using var f = new Fixture(); var path = Path.Combine(f.Root, "tool.exe");
    var tracker = new ProcessPresenceTracker([path]); tracker.Observe([new(1, path)]);
    Assert(tracker.Observe([], complete: false).Count == 0);
    Assert(tracker.Observe([new(1, path)]).Count == 0);
    Assert(tracker.Observe([]).Single().Trigger == TriggerMode.ProgramStopped);
});
Add("같은 파일 이름의 다른 프로그램은 감지하지 않음", () =>
{
    using var f = new Fixture(); var path = Path.Combine(f.Root, "a", "tool.exe");
    var other = Path.Combine(f.Root, "b", "tool.exe");
    var tracker = new ProcessPresenceTracker([path]); tracker.Observe([]);
    Assert(tracker.Observe([new(1, other), new(2, null)]).Count == 0);
    Assert(tracker.Observe([new(1, other), new(3, path.ToUpperInvariant())]).Single().Trigger == TriggerMode.ProgramStarted);
});
Add("초기 경로 미확인이 나중에 해소돼도 시작으로 오인하지 않음", () =>
{
    using var f = new Fixture(); var path = Path.Combine(f.Root, "tool.exe");
    var tracker = new ProcessPresenceTracker([path]); tracker.Observe([new(1, null)]);
    Assert(tracker.Observe([new(1, path)]).Count == 0);
    Assert(tracker.Observe([]).Single().Trigger == TriggerMode.ProgramStopped);
    Assert(tracker.Observe([new(2, path)]).Single().Trigger == TriggerMode.ProgramStarted);
});
Add("사라진 미확인 PID가 재사용되어도 새 실행을 감지", () =>
{
    using var f = new Fixture(); var path = Path.Combine(f.Root, "tool.exe");
    var tracker = new ProcessPresenceTracker([path]); tracker.Observe([new(1, null)]);
    Assert(tracker.Observe([]).Count == 0);
    Assert(tracker.Observe([new(1, path)]).Single().Trigger == TriggerMode.ProgramStarted);
});
Add("표본 사이 실행본이 모두 바뀌면 종료와 재시작을 순서대로 감지", () =>
{
    using var f = new Fixture(); var path = Path.Combine(f.Root, "tool.exe");
    var tracker = new ProcessPresenceTracker([path]); tracker.Observe([new(1, path)]);
    var changes = tracker.Observe([new(2, path)]);
    Assert(changes.Count == 2);
    Assert(changes[0].Trigger == TriggerMode.ProgramStopped && changes[1].Trigger == TriggerMode.ProgramStarted);
    Assert(changes.All(c => c.Path == path));
});
Add("빈 표본 없이 연속 재실행해도 매번 새 실행을 감지", () =>
{
    using var f = new Fixture(); var path = Path.Combine(f.Root, "tool.exe");
    var tracker = new ProcessPresenceTracker([path]); tracker.Observe([]);
    for (var pid = 1; pid <= 5; pid++)
    {
        var changes = tracker.Observe([new(pid, path)]);
        Assert(changes.Count(c => c.Trigger == TriggerMode.ProgramStarted) == 1);
        Assert(changes.Count(c => c.Trigger == TriggerMode.ProgramStopped) == (pid == 1 ? 0 : 1));
    }
});
Add("기존 실행본의 경로 미확인 중 추가 실행본이 생겨도 재시작으로 오인하지 않음", () =>
{
    using var f = new Fixture(); var path = Path.Combine(f.Root, "tool.exe");
    var tracker = new ProcessPresenceTracker([path]); tracker.Observe([new(1, path)]);
    Assert(tracker.Observe([new(1, null), new(2, path)]).Count == 0);
    Assert(tracker.Observe([new(2, path)]).Count == 0);
});
Add("재실행과 다른 프로그램의 연속 실행을 따로 판단", () =>
{
    using var f = new Fixture(); var a = Path.Combine(f.Root, "a.exe"); var b = Path.Combine(f.Root, "b.exe");
    var tracker = new ProcessPresenceTracker([a, b]); tracker.Observe([new(1, a), new(2, b)]);
    var changes = tracker.Observe([new(3, a), new(2, b)]);
    Assert(changes.Count == 2 && changes.All(c => c.Path == a));
});

Add("탐색기 자동 등록은 기본 켜짐이며 해제 상태를 저장", () =>
{
    using var f = new Fixture(); var storage = new Storage(Path.Combine(f.Root, "state"));
    Assert(storage.Load().AutoRegisterExplorerMenu);
    Directory.CreateDirectory(storage.DirectoryPath); File.WriteAllText(storage.SettingsPath, "{\"Version\":1}");
    Assert(storage.Load().AutoRegisterExplorerMenu);
    var settings = storage.Load(); settings.AutoRegisterExplorerMenu = false; storage.Save(settings);
    Assert(!storage.Load().AutoRegisterExplorerMenu);
});

Add("공유 세션은 중복 실행을 거부하고 종료 뒤 다시 획득", () =>
{
    using var f = new Fixture(); var directory = Path.Combine(f.Root, "state");
    using (var lease = SessionLease.Acquire(directory, "GUI"))
        Throws<SessionBusyException>(() => SessionLease.Acquire(directory, "TUI"));
    using var next = SessionLease.Acquire(directory, "CLI");
    Assert(File.Exists(Path.Combine(directory, "session.lock")));
});
Add("터미널 폴더 검증은 겹치는 검사 범위를 거부", () =>
{
    using var f = new Fixture(); var nested = Path.Combine(f.Source, "nested"); Directory.CreateDirectory(nested);
    Throws<ArgumentException>(() => SettingsValidation.Folder(new AppSettings { Folders = [f.Folder] }, new FolderSpec { Name = "겹침", Path = nested }));
});
Add("터미널 편집은 GUI 설정과 기존 규칙 ID를 보존", () =>
{
    using var f = new Fixture(); var storage = new Storage(Path.Combine(f.Root, "state"));
    storage.Save(new AppSettings { Theme = ThemeMode.Dark, CloseAction = WindowCloseAction.Exit, AutoRegisterExplorerMenu = false, Folders = [f.Folder] });
    var session = new Chagok.Terminal.TerminalSession(storage, storage.Load());
    var rule = Storage.Clone(f.Rule); rule.Name = "터미널에서 편집"; session.PutRule(f.Folder.Id, rule);
    var loaded = storage.Load();
    Assert(loaded.Theme == ThemeMode.Dark && loaded.CloseAction == WindowCloseAction.Exit && !loaded.AutoRegisterExplorerMenu);
    Assert(loaded.Folders[0].Rules[0].Name == rule.Name && loaded.Folders[0].Rules[0].Id == rule.Id);
});
Add("터미널의 잘못된 규칙은 저장 파일과 현재 설정을 보존", () =>
{
    using var f = new Fixture(); var storage = new Storage(Path.Combine(f.Root, "state"));
    storage.Save(new AppSettings { Folders = [f.Folder] });
    var session = new Chagok.Terminal.TerminalSession(storage, storage.Load()); var before = File.ReadAllText(storage.SettingsPath);
    var bad = Storage.Clone(f.Rule); bad.Destination = f.Source;
    Throws<ArgumentException>(() => session.PutRule(f.Folder.Id, bad));
    Assert(File.ReadAllText(storage.SettingsPath) == before && session.Settings.Folders[0].Rules[0].Destination == f.Target);
});
Add("터미널 인수는 누락·중복·오타·잘못된 불리언을 거부", () =>
{
    Throws<ArgumentException>(() => Chagok.Terminal.Arguments.Parse(["scan", "--folder"]));
    Throws<ArgumentException>(() => Chagok.Terminal.Arguments.Parse(["run", "--yes", "--yes"]));
    var parsedArgs = Chagok.Terminal.Arguments.Parse(["scan", "--forlder", "test"]);
    Throws<ArgumentException>(() => parsedArgs.Allow("folder"));
    var boolean = Chagok.Terminal.Arguments.Parse(["rules", "update", "--enabled", "maybe"]);
    Throws<ArgumentException>(() => boolean.Boolean("enabled", true));
});
Add("터미널 이름 중복은 전체 ID 선택을 요구", () =>
{
    using var f = new Fixture(); var folder = Storage.Clone(f.Folder); folder.Id = Guid.NewGuid();
    var session = new Chagok.Terminal.TerminalSession(new Storage(Path.Combine(f.Root, "state")), new AppSettings { Folders = [f.Folder, folder] });
    Throws<ArgumentException>(() => session.Folder(folder.Name)); Assert(session.Folder(folder.Id.ToString()).Id == folder.Id);
});
Async("터미널은 선택 항목만 이동하고 결과·기록을 저장", async () =>
{
    using var f = new Fixture(); f.Write("a.pdf", "a"); f.Write("b.pdf", "b"); f.Rule.Match = NameMatch.Any; f.Rule.Extensions = ".pdf";
    var session = new Chagok.Terminal.TerminalSession(new Storage(Path.Combine(f.Root, "state")), new AppSettings { Folders = [f.Folder] });
    using var plan = session.Scan(f.Folder, null, CancellationToken.None);
    plan.SetAllSelected(false); plan.SetSelected(plan.ReadPage(0)[1].Position, true);
    var report = await session.ExecuteAsync(plan, new TestBin(f.Root), null, CancellationToken.None);
    Assert(report.Completed == 1 && report.Total == 1);
    Assert(File.Exists(Path.Combine(f.Source, "a.pdf")) && !File.Exists(Path.Combine(f.Source, "b.pdf")));
    Assert(plan.ReadPage(0)[1].Result?.Status == "완료" && session.Storage.ReadHistory().Any(r => r.Status == "완료"));
});
Async("터미널 실행 전 기록 오류는 이동을 시작하지 않음", async () =>
{
    using var f = new Fixture(); f.Write("a.pdf", "a"); f.Rule.Match = NameMatch.Any;
    var storage = new Storage(Path.Combine(f.Root, "state")); Directory.CreateDirectory(storage.HistoryPath);
    var session = new Chagok.Terminal.TerminalSession(storage, new AppSettings { Folders = [f.Folder] });
    using var plan = session.Scan(f.Folder, null, CancellationToken.None);
    var report = await session.ExecuteAsync(plan, new TestBin(f.Root), null, CancellationToken.None);
    Assert(report.Outcome == ExecutionOutcome.Error && report.Processed == 0 && File.Exists(Path.Combine(f.Source, "a.pdf")));
});
Add("터미널 출력은 제어 문자를 제거하고 한글 표시 폭 제한", () =>
{
    Assert(!Chagok.Terminal.TerminalText.Clean("a\u001b[31mb\n").Contains('\u001b'));
    Assert(Chagok.Terminal.TerminalText.Fit("가나다", 4) == "가나");
});

Add("규칙명 폴더는 기본 위치와 분리하고 이름 변경에 따라 갱신", () =>
{
    using var f = new Fixture();
    Assert(DestinationPaths.Resolve(f.Rule) == f.Target);
    f.Rule.AppendRuleName = true; f.Rule.Name = "PDF 정리";
    Assert(DestinationPaths.Resolve(f.Rule) == Path.Combine(f.Target, "PDF 정리"));
    f.Rule.Name = "사진";
    Assert(DestinationPaths.Resolve(f.Rule) == Path.Combine(f.Target, "사진") && f.Rule.Destination == f.Target);
    var copy = Storage.Clone(f.Rule);
    Assert(copy.AppendRuleName && DestinationPaths.Resolve(copy) == DestinationPaths.Resolve(f.Rule));
    copy.AppendRuleName = false; Assert(DestinationPaths.Resolve(copy) == f.Target);
});
Add("규칙명 폴더의 경로 탈출·Windows 금지 이름 거부", () =>
{
    using var f = new Fixture(); f.Rule.AppendRuleName = true;
    foreach (var name in new[] { "../outside", @"..\outside", "..", ".", "bad/name", "bad:name", "bad*", "end.", "CON", "nul.txt", "COM1.pdf", "LPT9", "LPT¹.txt", "CONOUT$", "bad\nname", new string('a', 256) })
    { f.Rule.Name = name; Throws<ArgumentException>(() => PathSafety.ValidateRule(f.Folder, f.Rule)); }
    f.Rule.Name = "COM10"; PathSafety.ValidateRule(f.Folder, f.Rule);
    f.Rule.AppendRuleName = false; f.Rule.Name = "일반 규칙 / 설명"; PathSafety.ValidateRule(f.Folder, f.Rule);
});
Async("규칙명 폴더는 검사 때 생성하지 않고 실행 때 자동 생성", async () =>
{
    using var f = new Fixture(); f.Rule.AppendRuleName = true; f.Rule.CreateDestination = false;
    f.Rule.Name = "문서"; f.Write("a.pdf", "contents");
    var p = f.Scan(); var target = Path.Combine(f.Target, "문서", "a.pdf");
    Assert(p.Entries.Single().Target == target && p.Entries[0].CanExecute && p.Entries[0].CreateDestination);
    Assert(!Directory.Exists(f.Target));
    var result = await f.Execute(p);
    Assert(result.Status == "완료" && File.ReadAllText(target) == "contents" && !File.Exists(f.SourceFile("a.pdf")));
});
Add("규칙명 최종 폴더만 자동 제외하고 기존 파일 충돌도 판단", () =>
{
    using var f = new Fixture(); f.Rule.AppendRuleName = true; f.Rule.Recursive = true;
    f.Rule.Destination = f.Source; f.Rule.Name = "sorted";
    f.Write("top.pdf", "new"); f.Write("sorted/top.pdf", "old"); f.Write("other/next.pdf", "next");
    using var disk = new Planner().ScanToDisk(f.Folder, [DestinationPaths.Resolve(f.Rule)], Path.Combine(f.Root, "cache"));
    Assert(disk.Count == 2);
    var rows = disk.ReadPage(0).Select(x => x.Entry).ToArray();
    Assert(rows.Any(e => e.Target.EndsWith("top (1).pdf") && e.Action == PlanAction.Rename));
    Assert(rows.All(e => !PathSafety.IsWithin(e.Source, DestinationPaths.Resolve(f.Rule))));
    f.Rule.Name = "source"; f.Rule.Destination = f.Root;
    Throws<ArgumentException>(() => PathSafety.ValidateRule(f.Folder, f.Rule));
});
Add("최근 이동 위치는 폴더별 10개·중복 제거·꺼짐 기록 중단", () =>
{
    using var f = new Fixture(); var folder = f.Folder; var other = new FolderSpec();
    DestinationPaths.Remember(folder, f.Rule); Assert(folder.RecentDestinations.Count == 0);
    folder.UseRecentDestinations = true;
    for (var i = 0; i < 12; i++) { f.Rule.Destination = Path.Combine(f.Root, i.ToString()); DestinationPaths.Remember(folder, f.Rule); }
    Assert(folder.RecentDestinations.Count == 10 && folder.RecentDestinations[0] == Path.Combine(f.Root, "11"));
    f.Rule.Destination = Path.Combine(f.Root, "5"); f.Rule.AppendRuleName = true;
    DestinationPaths.Remember(folder, f.Rule);
    Assert(folder.RecentDestinations.Count == 10 && folder.RecentDestinations[0] == f.Rule.Destination);
    Assert(other.RecentDestinations.Count == 0);
    var before = folder.RecentDestinations.ToArray(); folder.UseRecentDestinations = false;
    f.Rule.Destination = f.Target; DestinationPaths.Remember(folder, f.Rule);
    Assert(DestinationPaths.Recent(folder).Count == 0 && before.SequenceEqual(folder.RecentDestinations));
    folder.UseRecentDestinations = true; Assert(DestinationPaths.Recent(folder).Count == 10);
});
Add("최근 위치·규칙 옵션 저장 실패 시 공유 설정 보존", () =>
{
    using var f = new Fixture(); f.Folder.UseRecentDestinations = true;
    var storage = new Storage(Path.Combine(f.Root, "data"));
    var settings = new AppSettings { Folders = [f.Folder] }; storage.Save(settings);
    var session = new Chagok.Terminal.TerminalSession(storage, settings);
    var before = File.ReadAllText(storage.SettingsPath);
    var rule = Storage.Clone(f.Rule); rule.AppendRuleName = true;
    Directory.CreateDirectory(storage.SettingsPath + ".tmp");
    Throws<UnauthorizedAccessException>(() => session.PutRule(f.Folder.Id, rule));
    Assert(File.ReadAllText(storage.SettingsPath) == before && session.Settings.Folders[0].RecentDestinations.Count == 0);
    Assert(!session.Settings.Folders[0].Rules[0].AppendRuleName);
    Directory.Delete(storage.SettingsPath + ".tmp"); session.PutRule(f.Folder.Id, rule);
    var loaded = storage.Load().Folders[0];
    Assert(loaded.Rules[0].AppendRuleName && loaded.RecentDestinations.SequenceEqual(new[] { f.Target }));
});
Add("이전 설정은 새 이동 옵션 기본 꺼짐으로 로드", () =>
{
    using var f = new Fixture(); var storage = new Storage(Path.Combine(f.Root, "data"));
    Directory.CreateDirectory(storage.DirectoryPath);
    File.WriteAllText(storage.SettingsPath, "{\"Version\":1,\"Folders\":[{\"Name\":\"old\",\"Path\":\"/old\",\"Rules\":[{\"Name\":\"old-rule\",\"Destination\":\"/target\"}]}]}");
    var folder = storage.Load().Folders.Single();
    Assert(!folder.UseRecentDestinations && folder.RecentDestinations.Count == 0 && !folder.Rules[0].AppendRuleName);
});

Add("새 규칙의 최근 위치 기본값은 옵션·명시 목적지 우선순위를 따름", () =>
{
    using var f = new Fixture(); f.Folder.RecentDestinations = [f.Target, f.Source];
    Assert(DestinationPaths.Initial(f.Folder) == "");
    f.Folder.UseRecentDestinations = true;
    Assert(DestinationPaths.Initial(f.Folder) == f.Target);
    Assert(DestinationPaths.Initial(f.Folder, f.Source) == f.Source);
    Assert(DestinationPaths.Initial(f.Folder, "") == "");
    var before = System.Text.Json.JsonSerializer.Serialize(f.Folder);
    _ = DestinationPaths.Initial(f.Folder);
    Assert(System.Text.Json.JsonSerializer.Serialize(f.Folder) == before);
});
Add("TUI 항목 설명은 규칙명·자동 생성·최근 위치와 연동 이유를 제공", () =>
{
    var folder = new FolderSpec();
    var off = Chagok.Terminal.TuiFieldHelp.RuleDisabled(13, folder);
    folder.UseRecentDestinations = true;
    var empty = Chagok.Terminal.TuiFieldHelp.RuleDisabled(13, folder);
    Assert(off.Contains("검사 폴더 설정") && empty.Contains("아직 최근 위치가 없습니다"));
    Assert(Chagok.Terminal.TuiFieldHelp.RuleDisabled(7, folder).Contains("자동 생성이 적용"));
    Assert(Chagok.Terminal.TuiFieldHelp.Rule(0).Contains("최종 이동 폴더"));
    Assert(Chagok.Terminal.TuiFieldHelp.Folder(6).Contains("새 규칙") && Chagok.Terminal.TuiFieldHelp.Folder(6).Contains("기존 기록"));
});

AnalysisChecks.Register(Add);
ConditionChecks.Register(Add);
RegexLessonChecks.Register(Add);
AnalysisAccordionChecks.Register(Add);
ReferenceOrganizationChecks.Register(Add);
DataMigrationChecks.Register(Add);
var failures = 0;
Add("통합 EXE는 GUI·탐색기와 터미널 명령을 분리", () =>
{
    Assert(Chagok.Terminal.LaunchRoute.IsGui([]));
    Assert(Chagok.Terminal.LaunchRoute.IsGui(["--add-rule", "C:\\target"]));
    Assert(!Chagok.Terminal.LaunchRoute.IsGui(["tui"]));
    Assert(!Chagok.Terminal.LaunchRoute.IsGui(["folders", "list", "--json"]));
    Assert(!Chagok.Terminal.LaunchRoute.IsGui(["--help"]));
});
Add("대시보드는 선택 폴더·규칙·목적지와 실행 키를 함께 표시", () =>
{
    var folder = new FolderSpec { Name = "다운로드", Path = "/files/downloads", Rules = [new RuleSpec { Name = "PDF 정리", Destination = "/files/pdf", Match = NameMatch.Any }] };
    foreach (var width in new[] { 60, 80, 110 })
    {
        var rows = Chagok.Terminal.Dashboard.Build(width, 32, [folder], 0, 0, 1);
        var screen = string.Join('\n', rows);
        Assert(screen.Contains("PDF 정리") && screen.Contains("/files/pdf") && screen.Contains("A 폴더 전체 검사"));
        Assert(rows.Count <= 31);
    }
    Assert(string.Join('\n', Chagok.Terminal.Dashboard.Build(80, 24, [], 0, 0, 0)).Contains("폴더를 추가하세요"));
});

Add("선택 규칙 검사는 앞선 광범위 규칙과 다른 파일을 이동 대상으로 선택", () =>
{
    using var f = new Fixture(); f.Write("keep.pdf", "pdf"); f.Write("only.txt", "txt");
    f.Rule.Match = NameMatch.Any; f.Rule.Extensions = ".pdf";
    var second = new RuleSpec { Name = "TXT", Match = NameMatch.Any, Extensions = ".txt", Destination = f.Target };
    f.Folder.Rules.Add(second);
    var session = new Chagok.Terminal.TerminalSession(new Storage(Path.Combine(f.Root, "state")), new AppSettings { Folders = [f.Folder] });
    using var single = session.Scan(f.Folder, new HashSet<Guid> { second.Id }, CancellationToken.None);
    Assert(single.Count == 1 && single.ReadPage(0)[0].Entry.Source.EndsWith("only.txt"));
    using var all = session.Scan(f.Folder, null, CancellationToken.None);
    Assert(all.Count == 2);
});

Add("버전이 붙은 EXE 이름을 탐색기 manifest에 XML 이스케이프해 반영", () =>
{
    const string xml = "<Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\"><Applications><Application Id=\"Chagok\" Executable=\"Chagok.exe\"/></Applications></Package>";
    var path = Path.Combine(Path.GetTempPath(), "Chagok-v0.4.3 & test.exe");
    var result = ShellLaunch.BuildPackageManifest(System.Text.Encoding.UTF8.GetBytes(xml), path);
    using var stream = new MemoryStream(result);
    var doc = System.Xml.Linq.XDocument.Load(stream);
    Assert((string?)doc.Descendants().Single(e => e.Name.LocalName == "Application").Attribute("Executable") == Path.GetFileName(path));
    Throws<ArgumentException>(() => ShellLaunch.BuildPackageManifest(result, "bad.exe\""));
});

Add("명령별 --help는 대상 명령과 JSON 옵션을 보존", () =>
{
    var args = Chagok.Terminal.Arguments.Parse(["rules", "add", "--help", "--json"]);
    Assert(args.Words.SequenceEqual(["help", "rules", "add"]) && args.Json);
    Assert(Chagok.Terminal.Arguments.Parse(["--version", "--json"]).Json);
});
Add("AI 도움말은 실행 승인·변경 효과·필수 인수와 전체 기능을 설명", () =>
{
    using var json = System.Text.Json.JsonDocument.Parse(Chagok.Terminal.HelpCatalog.Serialize("run"));
    var root = json.RootElement; var command = root.GetProperty("commands")[0];
    Assert(root.GetProperty("schemaVersion").GetInt32() == 1);
    Assert(command.GetProperty("name").GetString() == "run");
    Assert(command.GetProperty("effects").EnumerateArray().Any(e => e.GetString()!.Contains("원본 삭제")));
    Assert(command.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("name").GetString() == "folder").GetProperty("required").GetBoolean());
    Assert(command.GetProperty("confirmation").GetString()!.Contains("--yes"));
    Assert(Chagok.Terminal.HelpCatalog.Find("").Length == 22);
    Assert(Chagok.Terminal.HelpCatalog.Find("analyze").Single().Confirmation == "없음");
    Assert(Chagok.Terminal.HelpCatalog.Find("folders").Length == 5);
    Throws<ArgumentException>(() => Chagok.Terminal.HelpCatalog.Find("unknown"));
});
Add("테두리 TUI는 작은 창에서도 실행·도움말 안내와 선택 규칙을 유지", () =>
{
    var folder = new FolderSpec { Name = "선택 폴더", Path = "/source", Rules = [new RuleSpec { Name = "선택 규칙", Match = NameMatch.Any, Destination = "/target" }] };
    foreach (var width in new[] { 60, 80, 110 })
    foreach (var height in new[] { 24, 32 })
    {
        var rows = Chagok.Terminal.Dashboard.Build(width, height, [folder], 0, 0, 1);
        Assert(rows.Count <= height - 1);
        Assert(rows.All(row => Chagok.Terminal.TerminalText.CellWidth(row) <= width));
        var screen = string.Join('\n', rows);
        Assert(screen.Contains("╭") && screen.Contains("선택 규칙") && screen.Contains("F1/? 도움말"));
    }
});

Add("TUI 선택 배경은 활성 패널 안에만 있고 테두리와 간격을 보존", () =>
{
    var folders = Enumerable.Range(0, 16).Select(i => new FolderSpec { Name = "폴더 > " + i,
        Rules = [new RuleSpec { Name = "규칙 > 이름" }] }).ToArray();
    foreach (var width in new[] { 60, 80, 110 })
    foreach (var pane in new[] { 0, 1 })
    {
        var lines = Chagok.Terminal.Dashboard.Build(width, 24, folders, 15, 0, pane);
        var selections = Chagok.Terminal.Dashboard.Selections(width, 24, folders, 15, 0, pane);
        Assert(selections.Count(s => s.Active) == 1);
        foreach (var selection in selections)
        {
            var parts = Chagok.Terminal.Dashboard.SplitSelection(lines[selection.Line], selection);
            Assert(parts.Before + parts.Selected + parts.After == lines[selection.Line]);
            Assert(!parts.Selected.Contains('│'));
            Assert(Chagok.Terminal.TerminalText.CellWidth(parts.Selected) == selection.Width);
            Assert(parts.Selected.StartsWith(selection.Active ? "> " : "· "));
        }
    }
    Assert(Chagok.Terminal.Dashboard.Selections(110, 24, [], 0, 0, 0).Count == 0);
});

Add("폴더·규칙 상세는 이름·경로·조건을 구분하고 긴 경로를 보존", () =>
{
    var path = "C:\\" + string.Concat(Enumerable.Repeat("긴검사경로\\", 40));
    var folder = new FolderSpec { Name = "검사 폴더", Path = path, ExcludedFolders = ["C:\\제외"],
        Trigger = TriggerMode.ProgramStarted, ProgramPath = "C:\\app.exe", RunMode = RunMode.Automatic };
    var rule = new RuleSpec { Name = "문서 이동", Pattern = "보고서", Extensions = ".pdf", Recursive = true,
        Destination = "D:\\문서", Conflict = ConflictMode.Replace };
    folder.Rules.Add(rule);
    foreach (var width in new[] { 30, 60, 100 })
    {
        var rows = Chagok.Terminal.TuiDetails.FolderLines(folder, width);
        Assert(rows.All(s => Chagok.Terminal.TerminalText.CellWidth(s) <= width));
        var content = string.Join("\n", rows);
        Assert(content.Contains("폴더 이름") && content.Contains("전체 경로") && content.Contains("프로그램 시작 시") && content.Contains("자동 실행"));
        Assert(string.Concat(Chagok.Terminal.TuiDetails.Wrap(path, width - 6)) == path);
        var details = Chagok.Terminal.TuiDetails.RuleLines(folder, rule, width);
        Assert(details.All(s => Chagok.Terminal.TerminalText.CellWidth(s) <= width));
        var full = string.Join("\n", details);
        Assert(full.Contains("특정 단어 포함") && full.Contains("보고서") && full.Contains(".pdf") && full.Contains("번호") == false);
        Assert(full.Contains("규칙 ID"));
    }
});

Add("설정 목록은 선택한 값만 바꾸고 저장 전 원본을 유지", () =>
{
    var saved = new AppSettings { Folders = [new FolderSpec { Name = "보존" }] };
    var draft = Storage.Clone(saved);
    Chagok.Terminal.TuiSettings.Change(draft, 3, 1);
    Assert(saved.AutoRegisterExplorerMenu && !draft.AutoRegisterExplorerMenu);
    Assert(draft.Theme == saved.Theme && draft.CloseAction == saved.CloseAction && draft.ConfirmExitOnClose == saved.ConfirmExitOnClose);
    Assert(draft.Folders[0].Id == saved.Folders[0].Id && Chagok.Terminal.TuiSettings.Changed(saved, draft));
    Chagok.Terminal.TuiSettings.Change(draft, 3, -1);
    Chagok.Terminal.TuiSettings.Change(draft, 0, -1);
    Assert(draft.Theme == ThemeMode.Dark);
    Chagok.Terminal.TuiSettings.Change(draft, 0, 1);
    Assert(!Chagok.Terminal.TuiSettings.Changed(saved, draft));
});
Add("설정 목록은 60열 화면에 모든 항목과 선택 설명·저장·취소를 표시", () =>
{
    foreach (var width in new[] { 60, 80, 100 })
    foreach (var selected in Enumerable.Range(0, 4))
    {
        var rows = Chagok.Terminal.TuiSettings.Build(width, new AppSettings(), selected, true);
        Assert(rows.Count <= 23 && rows.All(s => Chagok.Terminal.TerminalText.CellWidth(s) <= width));
        var full = string.Join("\n", rows);
        Assert(full.Contains("GUI 테마") && full.Contains("GUI 창 닫기") && full.Contains("GUI 종료 확인") && full.Contains("GUI 시작 시 메뉴 확인"));
        Assert(full.Contains("S 저장") && full.Contains("Esc 취소") && full.Contains("저장하지 않은 변경"));
    }
});

Add("Chagok과 구버전 임시 파일은 규칙 검사·파일명 분석에서 제외", () =>
{
    using var f = new Fixture();
    f.Write(".chagok-[제외].pdf", "new temporary");
    f.Write(".fileorganizer-[제외].pdf", "legacy temporary");
    f.Write("[정상].pdf", "document");
    Assert(f.Scan().Entries.Single().Source == f.SourceFile("[정상].pdf"));
    using var analysis = FilenameAnalysis.Scan([f.Folder], [], Path.Combine(f.Root, "analysis"), new());
    Assert(analysis.Groups(null).Single().Key == "[정상]");
});

Add("PATH 등록은 기존 항목·환경 변수·빈 항목을 보존하고 중복을 추가하지 않음", () =>
{
    var before = @"%SystemRoot%\System32;;D:\Tools;";
    var directory = @"C:\Apps\차곡";
    var after = UserPath.Update(before, directory, true);
    Assert(after == before + directory);
    Assert(UserPath.Update(after, @"c:/apps/차곡/", true) == after);
    Assert(UserPath.Update(after, directory, false) == before.TrimEnd(';'));
    Assert(UserPath.Update(before, directory, false) == before);
    Assert(UserPath.Update("", directory, true) == directory);
});
Add("PATH 해제는 일치하는 항목만 제거하고 환경 변수 원문을 유지", () =>
{
    var directory = @"C:\Apps\Chagok";
    var value = @"%SystemRoot%\System32;C:\Apps\Chagok;D:\Other;""c:/apps/chagok/""";
    Assert(UserPath.Update(value, directory, false) == @"%SystemRoot%\System32;D:\Other");
    Assert(!UserPath.Contains(@"C:\Apps\Chagok-old", directory));
    Assert(!UserPath.Contains("C:", @"C:\") && UserPath.Contains("c:/", @"C:\"));
    Throws<ArgumentException>(() => UserPath.Update("", @"C:\Bad;Path", true));
    Throws<ArgumentException>(() => UserPath.Update("", "", true));
});

if (args.Length != 0 && (args.Length != 2 || args[0] != "--filter"))
{
    Console.Error.WriteLine("사용법: Chagok.Checks [--filter 테스트명]"); return 2;
}
var selectedTests = args.Length == 0 ? tests : tests.Where(t => t.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase)).ToList();
if (selectedTests.Count == 0) { Console.Error.WriteLine("일치하는 테스트가 없습니다."); return 2; }
foreach (var (name, run) in selectedTests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + name + "\n" + ex); }
}
Console.WriteLine($"{selectedTests.Count - failures}/{selectedTests.Count} passed");
return failures == 0 ? 0 : 1;

sealed class Fixture : IDisposable
{
    public string Root { get; }
    public string Source => Path.Combine(Root, "source");
    public string Target => Path.Combine(Root, "target");
    public FolderSpec Folder { get; }
    public RuleSpec Rule => Folder.Rules[0];
    public Fixture(bool external = false)
    {
        Root = Path.Combine(OperatingSystem.IsMacOS() && !external ? "/private/tmp/chagok-tests"
            : Path.Combine(Environment.CurrentDirectory, ".tools", "test-runs"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Source);
        Folder = new FolderSpec { Name = "test", Path = Source,
            Rules = [new RuleSpec { Name = "test-rule", Match = NameMatch.Any, Extensions = ".pdf", Destination = Target }] };
    }
    public string SourceFile(string name) => Path.Combine(Source, name.Replace('/', Path.DirectorySeparatorChar));
    public string TargetFile(string name) => Path.Combine(Target, name);
    public void Write(string name, string contents)
    {
        var path = SourceFile(name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, contents);
    }
    public ScanPlan Scan() => new Planner().Scan(Folder, [DestinationPaths.Resolve(Rule)]);
    public Task<OperationResult> Execute(ScanPlan plan, IRecycleBin? bin = null)
        => new Executor(bin ?? new TestBin(Root)).ExecuteAsync(plan, plan.Entries.Single());
    public void Dispose()
    {
        if (!Directory.Exists(Root)) return;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { Directory.Delete(Root, true); return; }
            catch (IOException) when (attempt < 2) { }
        }
    }
}

sealed class TestBin(string root) : IRecycleBin
{
    public bool Fail { get; init; }
    public Action? BeforeRecycle { get; init; }
    public string? LastRecycled { get; private set; }
    public Task RecycleAsync(string path, CancellationToken cancellationToken)
    {
        if (Fail) throw new IOException("휴지통 테스트 실패");
        cancellationToken.ThrowIfCancellationRequested(); BeforeRecycle?.Invoke();
        LastRecycled = Path.Combine(root, "recycled-" + Guid.NewGuid().ToString("N"));
        File.Move(path, LastRecycled);
        return Task.CompletedTask;
    }
}

sealed class CallbackProgress(Action<TransferProgress> report) : IProgress<TransferProgress>
{
    public void Report(TransferProgress value) => report(value);
}
