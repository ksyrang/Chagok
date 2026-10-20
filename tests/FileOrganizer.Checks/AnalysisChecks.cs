using FileOrganizer.Core;

static class AnalysisChecks
{
    public static void Register(Action<string, Action> add)
    {
        void Check(bool condition) { if (!condition) throw new Exception("파일명 분석 검사 실패"); }
        void Throws<T>(Action action) where T : Exception
        { try { action(); } catch (T) { return; } throw new Exception(typeof(T).Name + " 필요"); }
        FilenameAnalysis Scan(Fixture f, AnalysisOptions? options = null)
            => FilenameAnalysis.Scan([f.Folder], [], Path.Combine(f.Root, "analysis"), options ?? new());
        add("연습은 정규식 전체 일치와 추출 그룹을 구분", () =>
        {
            var capture = FilenameGroups.Preview("[공고문][공고문][보고서] 계획.pdf", @"\[(?<group>[^\[\]]+)\]");
            Check(capture.Matches.SequenceEqual(new[] { "[공고문]", "[공고문]", "[보고서]" }));
            Check(capture.Groups.SetEquals(new[] { "공고문", "보고서" }));
            var full = FilenameGroups.Preview("[공고문] 계획.pdf", @"\[[^\[\]]+\]");
            Check(full.Matches.Single() == "[공고문]" && full.Groups.Single() == "[공고문]");
            var noMatch = FilenameGroups.Preview("보고서.pdf", @"\[(?<group>[^\[\]]+)\]");
            Check(noMatch.Matches.Count == 0 && noMatch.Groups.Count == 0);
            Check(FilenameGroups.Preview("plain.pdf", "^").Groups.Count == 0);
        });
        add("연습의 그룹 합계는 한 줄에서 중복된 구분값을 한 번만 집계", () =>
        {
            var values = new[] { "[공고문][공고문] 첫번째.pdf", "[보고서] 결과.pdf", "[공고문] 추가.pdf", "일반.pdf" };
            var rows = values.Select(value => FilenameGroups.Preview(value, @"\[[^\[\]]+\]")).ToArray();
            var groups = rows.SelectMany(row => row.Groups).GroupBy(k => k, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());
            Check(groups["[공고문]"] == 2 && groups["[보고서]"] == 1 && groups.Count == 2);
            foreach (var value in values)
                Check(FilenameGroups.Preview(value, FilenameGroups.PatternExample).Groups.SetEquals(
                    FilenameGroups.Extract(value, new AnalysisOptions(CustomPattern: FilenameGroups.PatternExample))));
            Check(FilenameGroups.Preview("업무_2026_계획.pdf", @"^(?<group>[^_]+)_").Matches.Single() == "업무_");
            Check(FilenameGroups.Preview("업무_2026_계획.pdf", @"^(?<group>[^_]+)_").Groups.Single() == "업무");
        });
        add("연습의 잘못된 식·빈 식·길이·시간 제한", () =>
        {
            Throws<ArgumentException>(() => FilenameGroups.Preview("a.pdf", "["));
            Throws<ArgumentException>(() => FilenameGroups.Preview("a.pdf", ""));
            Throws<ArgumentException>(() => FilenameGroups.Preview("a.pdf", new string('a', 2001)));
            Throws<FilenamePatternTimeoutException>(() => FilenameGroups.Preview(new string('a', 10000) + "!", "^(a+)+$"));
        });
        add("사용자 정규식 캡처·전체 일치·반복 캡처·빈 값", () =>
        {
            var options = new AnalysisOptions(CustomPattern: FilenameGroups.PatternExample);
            FilenameGroups.Validate(options);
            Check(FilenameGroups.Extract("[a][a][b][A].pdf", options).SetEquals(new[] { "a", "b", "A" }));
            Check(FilenameGroups.Extract("업무_2026.pdf", new AnalysisOptions(CustomPattern: @"^(?<group>[^_]+)_")).SetEquals(new[] { "업무" }));
            Check(FilenameGroups.Extract("2026-10.pdf", new AnalysisOptions(CustomPattern: @"\d{4}")).SetEquals(new[] { "2026" }));
            Check(FilenameGroups.Extract("abc.pdf", new AnalysisOptions(CustomPattern: @"(?<group>a)(?<group>b)")).SetEquals(new[] { "a", "b" }));
            Check(FilenameGroups.Extract("[ ].pdf", options).Count == 0);
            Check(FilenameGroups.Extract("[ a ].pdf", options).Contains(" a "));
            var spaced = Storage.Clone(new RuleSpec { Match = NameMatch.Group, Pattern = " a ", GroupExpression = options.CustomPattern });
            Check(RuleMatcher.Matches(spaced, "[ a ].pdf", 0) && !RuleMatcher.Matches(spaced, "[a].pdf", 0));
            Check(FilenameGroups.Extract("[a][A].pdf", new AnalysisOptions(CustomPattern: @"(?i)\[(?<group>a)\]")).Count == 2);
            Throws<ArgumentException>(() => FilenameGroups.Validate(new AnalysisOptions(CustomPattern: "[")));
            Throws<ArgumentException>(() => FilenameGroups.Validate(new AnalysisOptions(CustomPattern: new string('a', 2001))));
        });
        add("정규식 분석·규칙 저장 왕복·정확한 그룹 비교", () =>
        {
            using var f = new Fixture();
            foreach (var name in new[] { "업무_1.pdf", "업무_2.pdf", "업무외_3.pdf", "plain.txt" }) f.Write(name, "x");
            var options = new AnalysisOptions(CustomPattern: @"^(?<group>[^_]+)_");
            using var a = Scan(f, options);
            Check(a.Folders().Single().Matched == 3 && a.FileCount(f.Folder.Id, "업무") == 2);
            var rule = FilenameGroups.Rule(f.Folder, "업무", options); rule.Destination = f.Target;
            rule = Storage.Clone(rule); PathSafety.ValidateRule(f.Folder, rule);
            Check(rule.GroupExpression == options.CustomPattern && rule.Pattern == "업무");
            Check(RuleMatcher.Matches(rule, "업무_1.pdf", 0));
            Check(!RuleMatcher.Matches(rule, "업무외_3.pdf", 0));
            Check(!RuleMatcher.Matches(rule, "업무_1.pdf", 1));
        });
        add("정규식 시간 초과·실패 분석 임시 저장소 정리", () =>
        {
            using var f = new Fixture();
            Throws<ArgumentException>(() => FilenameGroups.Extract(new string('a', 10000) + "!", new AnalysisOptions(CustomPattern: "^(a+)+$")));
            Throws<ArgumentException>(() => Scan(f, new AnalysisOptions(CustomPattern: "[")));
            Check(!Directory.Exists(Path.Combine(f.Root, "analysis")));
            f.Write(new string('a', 200) + "!.txt", "must stay");
            var slow = new AnalysisOptions(CustomPattern: "^(a+)+$");
            Throws<FilenamePatternTimeoutException>(() => Scan(f, slow));
            Check(!Directory.EnumerateDirectories(Path.Combine(f.Root, "analysis")).Any());
            f.Rule.Match = NameMatch.Group; f.Rule.Pattern = "a"; f.Rule.GroupExpression = slow.CustomPattern;
            Throws<FilenamePatternTimeoutException>(() => new Planner().ScanToDisk(f.Folder, [], Path.Combine(f.Root, "plans")));
            Check(!Directory.EnumerateDirectories(Path.Combine(f.Root, "plans")).Any());
            Check(File.Exists(f.SourceFile(new string('a', 200) + "!.txt")));
        });
        add("이동 경로 직접 편집 시 규칙명 중복 방지·새 경로·루트 보존", () =>
        {
            using var f = new Fixture();
            var final = Path.Combine(f.Target, "업무");
            Check(DestinationPaths.BaseFromDisplay(final, "업무", true) == f.Target);
            Check(DestinationPaths.BaseFromDisplay(final + Path.DirectorySeparatorChar, "업무", true) == f.Target);
            Check(DestinationPaths.BaseFromDisplay(f.Target, "업무", true) == f.Target);
            Check(DestinationPaths.BaseFromDisplay(final, "업무", false) == final);
            var rule = new RuleSpec { Name = "업무", Destination = DestinationPaths.BaseFromDisplay(final, "업무", true), AppendRuleName = true };
            Check(DestinationPaths.Resolve(rule) == final);
            rule.Name = "새 이름"; Check(DestinationPaths.Resolve(rule) == Path.Combine(f.Target, "새 이름"));
        });
        add("분석은 길이가 다른 괄호·대소문자·반복값을 구분", () =>
        {
            Check(FilenameGroups.Extract("[a][a][b][긴 이름][A] file.txt", BracketKind.Square).SetEquals(new[] { "[a]", "[b]", "[긴 이름]", "[A]" }));
            Check(FilenameGroups.Extract("[][ ] [[a]] [bad [nested]] [open", BracketKind.Square).Count == 0);
            Check(FilenameGroups.Extract("(a){b}[c]", BracketKind.Round).SetEquals(new[] { "(a)" }));
            Check(FilenameGroups.Extract("(a){b}[c]", BracketKind.Curly).SetEquals(new[] { "{b}" }));
            foreach (var key in new[] { "a", "[]", "[a][b]", "[[a]]", "x[a]" }) Throws<ArgumentException>(() => FilenameGroups.ValidateKey(key));
        });
        add("파일당 같은 값은 한 번 세고 그룹별 원본 목록 제공", () =>
        {
            using var f = new Fixture();
            foreach (var n in new[] { "[a][a] first.pdf", "[a][b] second.txt", "[A] third.pdf", "[b] fourth.jpg", "plain.bin", "[] empty.txt" }) f.Write(n, "contents");
            using var a = Scan(f); var s = a.Folders().Single();
            Check(s.Total == 6 && s.Matched == 4 && s.Unmatched == 2 && s.Groups == 3);
            Check(a.GroupCount(f.Folder.Id, 2) == 2 && a.FileCount(f.Folder.Id, "[a]") == 2);
            Check(a.Groups(f.Folder.Id).Sum(g => g.Count) == 5);
            Check(a.Files(f.Folder.Id, "[a]").All(p => p.Name.Contains("[a]", StringComparison.Ordinal)));
            Check(File.ReadAllText(f.SourceFile("[a][a] first.pdf")) == "contents" && !Directory.Exists(f.Target));
        });
        add("분석 하위 폴더·제외·목적지·분석 캐시 범위", () =>
        {
            using var f = new Fixture(); f.Folder.ExcludedFolders.Add(f.SourceFile("private"));
            foreach (var n in new[] { "[a] root.pdf", "sub/[a] child.pdf", "private/[x] secret.pdf", "sorted/[z] done.pdf", "._[a] metadata", ".DS_Store" }) f.Write(n, "data");
            using (var top = Scan(f)) Check(top.Folders().Single().Total == 1);
            using var a = FilenameAnalysis.Scan([f.Folder], [f.SourceFile("sorted")], f.SourceFile("cache"), new(Recursive: true));
            Check(a.Folders().Single().Total == 2 && a.FileCount(f.Folder.Id, "[a]") == 2);
        });
        add("분석 폴더별·전체 합계 및 없는 폴더 경고", () =>
        {
            using var f = new Fixture(); f.Write("[a] one.pdf", "x");
            var second = new FolderSpec { Name = "other", Path = Path.Combine(f.Root, "other") };
            Directory.CreateDirectory(second.Path); File.WriteAllText(Path.Combine(second.Path, "[a] two.pdf"), "y");
            var missing = new FolderSpec { Name = "missing", Path = Path.Combine(f.Root, "missing") };
            using var a = FilenameAnalysis.Scan([f.Folder, second, missing], [], Path.Combine(f.Root, "cache"), new());
            Check(a.Folders().Count == 3 && a.Folders()[0].Matched == 1 && a.Folders()[1].Matched == 1);
            Check(a.Folders()[2].Warnings == 1 && a.WarningCount == 1 && a.Warnings().Count == 1);
            Check(a.GroupCount(null, 2) == 1 && a.FileCount(null, "[a]") == 2 && a.FileCount(second.Id, "[a]") == 1);
        });
        add("대량 분석 그룹·파일은 100개씩 안정적으로 읽기", () =>
        {
            using var f = new Fixture();
            for (var i = 0; i < 205; i++) { f.Write($"[{i:D3}] item.pdf", "x"); f.Write($"[many] {i:D3}.pdf", "y"); }
            using var a = Scan(f);
            Check(a.GroupCount(f.Folder.Id) == 206 && a.Groups(f.Folder.Id).Count == 100);
            var groups = a.Groups(f.Folder.Id).Concat(a.Groups(f.Folder.Id, page: 1)).Concat(a.Groups(f.Folder.Id, page: 2)).ToArray();
            Check(groups.Length == 206 && groups.Select(g => g.Key).Distinct().Count() == 206);
            Check(a.Files(f.Folder.Id, "[many]").Count == 100 && a.Files(f.Folder.Id, "[many]", 1).Count == 100 && a.Files(f.Folder.Id, "[many]", 2).Count == 5);
            Check(a.Files(f.Folder.Id, "[many]", 3).Count == 0);
            Throws<ArgumentOutOfRangeException>(() => a.Groups(null, page: -1));
        });
        add("분석 SQL 구분값은 데이터로 취급하고 스냅샷 유지", () =>
        {
            using var f = new Fixture(); f.Write("[a' OR 1=1] quote.pdf", "x"); f.Write("[b] other.pdf", "y");
            using var a = Scan(f);
            Check(a.FileCount(null, "[a' OR 1=1]") == 1 && a.Files(null, "[unknown]").Count == 0);
            File.Delete(f.SourceFile("[b] other.pdf")); Check(a.FileCount(null, "[b]") == 1);
        });
        add("분석 규칙은 집계와 같은 구분값·대소문자·재귀 조건 사용", () =>
        {
            using var f = new Fixture(); f.Folder.UseRecentDestinations = true; f.Folder.RecentDestinations.Add(f.Target);
            var rule = FilenameGroups.Rule(f.Folder, "[a]", new(Recursive: true));
            Check(rule.Match == NameMatch.Group && rule.Pattern == "[a]" && rule.Destination == f.Target && rule.Recursive);
            Check(RuleMatcher.Matches(rule, f.SourceFile("[a][a] file.pdf"), 1));
            Check(!RuleMatcher.Matches(rule, f.SourceFile("[A] file.pdf"), 0));
            Check(!RuleMatcher.Matches(rule, f.SourceFile("[[a]] file.pdf"), 0));
            Check(!RuleMatcher.Matches(rule, f.SourceFile("[ab] file.pdf"), 0));
            Check(Storage.Clone(rule).Match == NameMatch.Group);
            rule.Pattern = "[a][b]"; Throws<ArgumentException>(() => PathSafety.ValidateRule(f.Folder, rule));
        });
        add("분석 임대는 사용 중 캐시를 보존하고 종료 시 정리", () =>
        {
            using var f = new Fixture(); f.Write("[a] file.pdf", "data");
            var first = Scan(f); var path = first.DirectoryPath;
            using (var second = Scan(f)) Check(Directory.Exists(path) && first.FileCount(null, "[a]") == 1);
            first.Dispose(); first.Dispose(); Check(!Directory.Exists(path));
        });
        add("취소 분석은 임시 DB를 정리하고 파일·설정 유지", () =>
        {
            using var f = new Fixture(); f.Write("[a] file.pdf", "data"); using var cancel = new CancellationTokenSource();
            Throws<OperationCanceledException>(() => FilenameAnalysis.Scan([f.Folder], [], Path.Combine(f.Root, "cache"), new(), cancel.Token, new Callback(_ => cancel.Cancel())));
            Check(!Directory.EnumerateDirectories(Path.Combine(f.Root, "cache")).Any());
            Check(File.ReadAllText(f.SourceFile("[a] file.pdf")) == "data" && f.Folder.Rules.Count == 1);
        });
        add("분석은 링크를 따라가지 않으며 일부 생략을 알림", () =>
        {
            if (OperatingSystem.IsWindows()) return;
            using var f = new Fixture(); f.Write("[a] root.pdf", "data");
            Directory.CreateDirectory(f.Target); File.WriteAllText(f.TargetFile("[b] outside.pdf"), "outside");
            Directory.CreateSymbolicLink(f.SourceFile("linked"), f.Target);
            using var a = Scan(f, new(Recursive: true));
            Check(a.Folders().Single().Total == 1 && a.WarningCount == 1 && a.FileCount(null, "[b]") == 0);
        });
    }
    private sealed class Callback(Action<AnalysisProgress> callback) : IProgress<AnalysisProgress>
    { public void Report(AnalysisProgress value) => callback(value); }
}
