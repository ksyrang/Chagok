using Chagok.Core;

static class ReferenceOrganizationChecks
{
    public static void Register(Action<string, Action> add)
    {
        void Check(bool condition, string message = "검증 실패") { if (!condition) throw new Exception(message); }
        void Throws<T>(Action action) where T : Exception
        { try { action(); } catch (T) { return; } throw new Exception(typeof(T).Name + " 예외 없음"); }
        add("ReferenceOrganization 임시 검사 위치는 등록 규칙·제외 경로·설정을 변경하지 않음", () =>
        {
            using var f = new ReferenceFixture(); f.Folder.Rules.Add(new() { Name = "원본 규칙", Pattern = "old", Destination = f.A });
            f.Folder.ExcludedFolders.Add(Path.Combine(f.Source, "excluded"));
            var saved = System.Text.Json.JsonSerializer.Serialize(f.Folder);
            var same = ReferenceOrganization.PrepareSource(f.Folder, f.Source, f.Cache);
            Check(same.Id == f.Folder.Id && same.ExcludedFolders.Count == 1); same.Rules[0].Name = "복사본 변경";
            var temporary = Path.Combine(f.Work, "temporary"); Directory.CreateDirectory(temporary);
            var next = ReferenceOrganization.PrepareSource(f.Folder, temporary, f.Cache);
            Check(next.Path == temporary && next.Id != f.Folder.Id && next.Rules.Count == 0 && next.ExcludedFolders.Count == 0);
            Check(System.Text.Json.JsonSerializer.Serialize(f.Folder) == saved);
            var isolated = ReferenceOrganization.PrepareSource(null, temporary, f.Cache); Check(isolated.Path == temporary);
            Throws<ArgumentException>(() => ReferenceOrganization.PrepareSource(f.Folder, f.Root, f.Root));
        });
        add("OrganizationStart 기존 규칙과 기준 폴더는 별도 진입·좁은 화면·빈 등록 목록", () =>
        {
            using var f = new ReferenceFixture();
            foreach (var width in new[] { 60, 80, 110 })
            foreach (var folder in new FolderSpec?[] { null, f.Folder })
            {
                var rows = Chagok.Terminal.StartScreen.Build(width, folder);
                Check(rows.Count <= 24 && rows.All(r => Chagok.Terminal.TerminalText.CellWidth(r) <= width));
                var text = string.Join("\n", rows);
                Check(text.Contains("1 규칙으로 정리") && text.Contains("2 기준 폴더로 정리") && text.Contains("기존 규칙 목록"));
                Check(text.Contains(folder is null ? "미등록" : f.Folder.Name));
            }
        });
        add("ReferenceOrganization Chagok과 구버전 임시 파일은 제외", () =>
        {
            using var f = new ReferenceFixture();
            f.Write(".chagok-[임시].pdf"); f.Write(".fileorganizer-[임시].pdf"); f.Write("[정상].pdf");
            using var scan = f.Scan();
            Check(scan.Count() == 1 && scan.Groups().Single().Key == "[정상]");
        });
        add("ReferenceOrganization 폴더명 기본 검색·별칭·괄호 일치·조회 무변경", () =>
        {
            using var f = new ReferenceFixture();
            f.Write("거래처A_보고서.PDF"); f.Write("[A] 계약.pdf"); f.Write("[a] 소문자.pdf");
            var targets = ReferenceOrganization.ReadTargets(f.Root);
            Check(targets.Count == 2 && targets.Any(t => t.Keywords == "거래처A"));
            using var scan = f.Scan([new(f.A, "거래처A\n[A]")]);
            Check(scan.Count() == 3 && scan.Count(ReferenceFilter.Decided) == 3);
            Check(scan.Files(ReferenceFilter.Decided, 0).All(r => r.Destination == f.A));
            using var exact = f.Scan([new(f.A, "[A]", ReferenceMatch.Group)]);
            Check(exact.Count(ReferenceFilter.Decided) == 1 && exact.Count(ReferenceFilter.Unmatched) == 2);
            Check(Directory.GetFiles(f.Source).Length == 3 && !Directory.GetFiles(f.A).Any());
            Throws<ArgumentException>(() => f.Scan([new(f.A, "A", ReferenceMatch.Group)]));
            Throws<ArgumentException>(() => f.Scan([new(f.A, " \n")]));
        });
        add("ReferenceOrganization 동일 목적지 별칭 중복 집계 방지·복수 후보 보류", () =>
        {
            using var f = new ReferenceFixture(); f.Write("[A] 거래처A 거래처B.pdf");
            using var scan = f.Scan([new(f.A, "[A]\n거래처A"), new(f.B, "거래처B")]);
            var row = scan.Files(ReferenceFilter.Ambiguous, 0).Single();
            Check(row.Candidates == 2 && row.Destination == "" && scan.Candidates(row.Id).Count == 2);
            using (var empty = scan.CreatePlan(f.Cache)) Check(empty.Count == 0);
            scan.Assign(row.Id, f.B);
            using var plan = scan.CreatePlan(f.Cache);
            Check(plan.Count == 1 && plan.ReadPage(0).Single().Entry.Target == Path.Combine(f.B, row.Name));
            scan.ClearAssignment(row.Id); Check(scan.Count(ReferenceFilter.Ambiguous) == 1);
        });
        add("ReferenceOrganization 새 그룹·그룹 전체 지정·겹치는 그룹 한 번만 처리", () =>
        {
            using var f = new ReferenceFixture(); f.Write("[C][D] one.pdf"); f.Write("[C] two.pdf"); f.Write("plain.pdf");
            using var scan = f.Scan();
            Check(scan.GroupCount == 2 && scan.Groups()[0].Count == 2 && scan.GroupFileCount("[C]") == 2);
            var destination = ReferenceOrganization.NewDestination(f.Root, "새 거래처");
            scan.AssignGroup("[C]", destination);
            Check(scan.Count(ReferenceFilter.Decided) == 2 && scan.GroupCount == 0 && !Directory.Exists(destination));
            using var plan = scan.CreatePlan(f.Cache); Check(plan.Count == 2);
            Check(plan.ReadPage(0).All(r => Path.GetDirectoryName(r.Entry.Target) == destination));
            Throws<ArgumentException>(() => ReferenceOrganization.NewDestination(f.Root, "../escape"));
            Throws<ArgumentException>(() => ReferenceOrganization.NewDestination(f.Root, "CON"));
            Throws<ArgumentException>(() => ReferenceOrganization.NewDestination(f.Root, "bad."));
            Throws<ArgumentException>(() => scan.Assign(1, f.Source));
        });
        add("ReferenceOrganization 폴더 제안은 중복 그룹과 보류 파일을 제외하고 페이지 전체에 적용", () =>
        {
            using var f = new ReferenceFixture();
            for (var i = 0; i < 105; i++) f.Write($"[C] {i:000}.pdf");
            f.Write("[C][D] overlap.pdf"); f.Write("[D] single.pdf"); f.Write("plain.pdf"); f.Write("거래처A.pdf");
            using var scan = f.Scan();
            Check(scan.ProposedFileCount == 106 && scan.ProposalCount == 2);
            Check(scan.Count(ReferenceFilter.GroupOverlap) == 1 && scan.Count(ReferenceFilter.NoGroup) == 1);
            Check(scan.ProposalFileCount("[C]") == 105 && scan.Files(ReferenceFilter.Unmatched, 1, "[C]", true).Count == 5);
            scan.AssignProposal("[C]", ReferenceOrganization.NewDestination(f.Root, "C"));
            Check(scan.ProposedFileCount == 1 && scan.Count(ReferenceFilter.GroupOverlap) == 1);
            Check(scan.DestinationCount == 2 && scan.Destinations(0).Sum(x => x.Count) == 106);
            var proposedPath = ReferenceOrganization.NewDestination(f.Root, "C");
            Check(scan.DestinationFileCount(proposedPath) == 105 && scan.DestinationFiles(proposedPath, 1).Count == 5);
            scan.HoldProposal("[D]");
            Check(scan.ProposedFileCount == 0 && scan.Count(ReferenceFilter.Held) == 1);
            using var plan = scan.CreatePlan(f.Cache); Check(plan.Count == 106);
            Check(!Directory.Exists(Path.Combine(f.Root, "C")));
        });
        add("ReferenceOrganization 페이지·필터·전체 그룹 지정은 100건 초과에도 보존", () =>
        {
            using var f = new ReferenceFixture(); for (var i = 0; i < 205; i++) f.Write($"[C] {i:000}.pdf");
            using var scan = f.Scan(); Check(scan.Files(ReferenceFilter.All, 0).Count == 100 && scan.Files(ReferenceFilter.All, 2).Count == 5);
            var last = scan.Files(ReferenceFilter.All, 2)[4]; scan.Assign(last.Id, f.B);
            Check(scan.Files(ReferenceFilter.Decided, 0).Single().Id == last.Id);
            scan.AssignGroup("[C]", f.A); Check(scan.Count(ReferenceFilter.Decided) == 205);
            using var plan = scan.CreatePlan(f.Cache); Check(plan.Count == 205 && plan.SelectedCount == 205);
            Check(plan.ReadPage(200).Single(r => r.Entry.Source == last.Path).Entry.Target == Path.Combine(f.B, last.Name));
        });
        add("ReferenceOrganization 재귀·제외·기준 폴더 중첩·링크 제외", () =>
        {
            using var f = new ReferenceFixture(); f.Write("거래처A.pdf"); f.Write("sub/거래처A child.pdf"); f.Write("excluded/거래처A no.pdf"); f.Write("._거래처A");
            f.Folder.ExcludedFolders.Add(Path.Combine(f.Source, "excluded"));
            using (var scan = f.Scan()) Check(scan.Count() == 1);
            using (var recursive = f.Scan(options: new(Recursive: true))) Check(recursive.Count() == 2);
            using (var previousDestination = ReferenceOrganization.Scan(f.Folder, f.Root, ReferenceOrganization.ReadTargets(f.Root), [f.Source], f.Cache, new())) Check(previousDestination.Count() == 1);
            var nestedRoot = Path.Combine(f.Source, "sorted"); Directory.CreateDirectory(Path.Combine(nestedRoot, "거래처A"));
            File.WriteAllText(Path.Combine(nestedRoot, "거래처A", "거래처A old.pdf"), "old");
            using (var nested = ReferenceOrganization.Scan(f.Folder, nestedRoot, ReferenceOrganization.ReadTargets(nestedRoot), [], f.Cache, new(Recursive: true))) Check(nested.Count() == 2);
            Throws<ArgumentException>(() => ReferenceOrganization.Scan(f.Folder, f.Source, [new(f.A, "A")], [], f.Cache, new()));
            if (!OperatingSystem.IsWindows())
            {
                var link = Path.Combine(f.Source, "거래처A-link.pdf"); File.CreateSymbolicLink(link, Path.Combine(f.Source, "거래처A.pdf"));
                Directory.CreateSymbolicLink(Path.Combine(f.Root, "link"), f.A);
                Check(ReferenceOrganization.ReadTargets(f.Root).Count == 2);
                using var scan = f.Scan(); Check(scan.Count() == 1 && scan.WarningCount > 0);
            }
        });
        add("ReferenceOrganization 분석 후 변경·삭제 원본 보류", () =>
        {
            using var f = new ReferenceFixture(); f.Write("거래처A changed.pdf"); f.Write("거래처A deleted.pdf");
            using var scan = f.Scan(); f.Write("거래처A changed.pdf", "longer content"); File.Delete(Path.Combine(f.Source, "거래처A deleted.pdf"));
            using var plan = scan.CreatePlan(f.Cache);
            Check(plan.Count == 2 && plan.ExecutableCount == 0 && plan.ReadPage(0).All(r => r.Entry.Action == PlanAction.Blocked));
        });
        add("ReferenceOrganization 명시 목적지 이동·이름 충돌·계획 이후 변경 방어", () =>
        {
            using var f = new ReferenceFixture(); f.Write("거래처A.pdf"); File.WriteAllText(Path.Combine(f.A, "거래처A.pdf"), "old");
            using var scan = f.Scan(); using var plan = scan.CreatePlan(f.Cache);
            var entry = plan.ReadPage(0).Single().Entry;
            Check(entry.Action == PlanAction.Rename && Path.GetFileName(entry.Target) == "거래처A (1).pdf");
            var executor = new Executor(new ReferenceBin()); var outcome = executor.ExecuteAsync(plan.Header, entry).GetAwaiter().GetResult();
            Check(File.Exists(entry.Target) && !File.Exists(entry.Source), outcome.Message);
            Check(File.ReadAllText(Path.Combine(f.A, "거래처A.pdf")) == "old");
            f.Write("거래처A second.pdf"); using var again = f.Scan(); using var next = again.CreatePlan(f.Cache);
            var second = next.ReadPage(0).Single().Entry; f.Write("거래처A second.pdf", "changed after plan");
            executor.ExecuteAsync(next.Header, second).GetAwaiter().GetResult();
            Check(File.Exists(second.Source) && !File.Exists(second.Target));
        });
        add("ReferenceOrganization 새 폴더 생성·예약 경로 충돌·전체 원본 이동", () =>
        {
            using var f = new ReferenceFixture(); f.Write("first/[C] same.pdf", "first"); f.Write("second/[C] same.pdf", "second");
            using var scan = f.Scan(options: new(Recursive: true));
            var destination = ReferenceOrganization.NewDestination(f.Root, "C"); scan.AssignGroup("[C]", destination);
            using var plan = scan.CreatePlan(f.Cache); var rows = plan.ReadPage(0);
            Check(rows.Count == 2 && rows.Select(r => r.Entry.Target).Distinct().Count() == 2 && !Directory.Exists(destination));
            var executor = new Executor(new ReferenceBin());
            foreach (var row in rows) executor.ExecuteAsync(plan.Header, row.Entry).GetAwaiter().GetResult();
            Check(Directory.GetFiles(destination).Length == 2 && rows.All(r => !File.Exists(r.Entry.Source)));
        });
        add("ReferenceOrganization 그룹 페이지·보류 필터·저장소 오류는 중단", () =>
        {
            using var f = new ReferenceFixture(); for (var i = 0; i < 105; i++) f.Write($"[group{i:000}] file.pdf");
            using var scan = f.Scan(); Check(scan.Groups(0).Count == 100 && scan.Groups(1).Count == 5);
            var first = scan.Files(ReferenceFilter.All, 0)[0]; scan.Assign(first.Id, f.A); scan.ClearAssignment(first.Id);
            Check(scan.Count(ReferenceFilter.Unmatched) == 105);
            var dbPath = Path.Combine(Directory.GetDirectories(f.Cache).Single(), "analysis.db");
            using var db = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString());
            db.Open(); using (var command = db.CreateCommand()) { command.CommandText = "DROP TABLE candidates"; command.ExecuteNonQuery(); }
            Throws<ScanStorageException>(() => scan.Candidates(first.Id));
            using (var command = db.CreateCommand()) { command.CommandText = "DROP TABLE files"; command.ExecuteNonQuery(); }
            Throws<ScanStorageException>(() => scan.CreatePlan(f.Cache));
            Check(Directory.GetDirectories(f.Cache).Length == 1 && Directory.GetFiles(f.Source).Length == 105);
        });
        add("ReferenceOrganization 정규식 그룹·취소·캐시 정리", () =>
        {
            using var f = new ReferenceFixture(); f.Write("project_one.pdf"); f.Write("project_two.pdf");
            string? directory;
            using (var scan = f.Scan(options: new(CustomPattern: "^(?<group>[^_]+)_")))
            {
                Check(scan.Groups().Single().Key == "project");
                directory = Directory.GetDirectories(f.Cache).Single();
                using var canceled = new CancellationTokenSource(); canceled.Cancel();
                Throws<OperationCanceledException>(() => scan.CreatePlan(f.Cache, canceled.Token));
                Check(Directory.Exists(directory));
            }
            Check(!Directory.Exists(directory));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Throws<OperationCanceledException>(() => ReferenceOrganization.Scan(f.Folder, f.Root, ReferenceOrganization.ReadTargets(f.Root), [], f.Cache, new(), cancellation.Token));
            Check(!Directory.EnumerateDirectories(f.Cache).Any());
            Throws<ArgumentException>(() => f.Scan(options: new(CustomPattern: "[")));
        });
    }
    private sealed class ReferenceFixture : IDisposable
    {
        public string Work { get; } = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "chagok-reference-" + Guid.NewGuid().ToString("N"));
        public string Root => Path.Combine(Work, "sorted");
        public string Source => Path.Combine(Work, "source");
        public string Cache => Path.Combine(Work, "cache");
        public string A => Path.Combine(Root, "거래처A");
        public string B => Path.Combine(Root, "거래처B");
        public FolderSpec Folder { get; }
        public ReferenceFixture()
        {
            Directory.CreateDirectory(A); Directory.CreateDirectory(B); Directory.CreateDirectory(Source);
            Folder = new() { Name = "reference-test", Path = Source };
        }
        public void Write(string name, string text = "contents") { var path = Path.Combine(Source, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        public ReferenceOrganization Scan(IReadOnlyList<ReferenceTarget>? targets = null, AnalysisOptions? options = null)
            => ReferenceOrganization.Scan(Folder, Root, targets ?? ReferenceOrganization.ReadTargets(Root), [], Cache, options ?? new());
        public void Dispose() => Directory.Delete(Work, true);
    }
    private sealed class ReferenceBin : IRecycleBin
    { public Task RecycleAsync(string path, CancellationToken cancellationToken) => throw new Exception("기본 충돌 처리에서 휴지통을 호출하면 안 됩니다."); }
}
