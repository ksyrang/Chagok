using Chagok.Core;

static class ConditionChecks
{
    public static void Register(Action<string, Action> add)
    {
        void Check(bool condition) { if (!condition) throw new Exception("조건 선택 검사 실패"); }
        void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("입력 검증 필요"); }
        FilenameConditionSet Set(params (FilenameConditionKind Kind, string Value)[] terms) => new()
        { Terms = terms.Select(t => new FilenameCondition { Kind = t.Kind, Value = t.Value }).ToList() };
        bool Match(FilenameConditionSet set, string name) => FilenameGroups.Extract(name, set.ToOptions()).Count > 0;
        add("선택 조건 OR는 x 시작 또는 y 정확히 같음", () =>
        {
            var set = Set((FilenameConditionKind.StartsWith, "x"), (FilenameConditionKind.Equal, "y"));
            foreach (var name in new[] { "[xyz] 문서.pdf", "[x].txt", "[y].pdf" }) Check(Match(set, name));
            foreach (var name in new[] { "[yellow].pdf", "[ax].pdf", "[X].pdf", "xyz.pdf", "[x] [abc]".Replace("[x]", "[]") }) Check(!Match(set, name));
            Check(FilenameGroups.Extract("[xyz][xyz][y]", set.ToOptions()).SetEquals(new[] { "xyz", "y" }));
        });
        add("선택 조건 AND와 네 비교 연산은 같은 괄호 안에서 판단", () =>
        {
            var set = Set((FilenameConditionKind.StartsWith, "x"), (FilenameConditionKind.EndsWith, "y")); set.All = true;
            Check(Match(set, "[x123y].pdf") && !Match(set, "[x123][123y].pdf"));
            var contains = Set((FilenameConditionKind.Contains, "middle"));
            Check(Match(contains, "[a-middle-b].pdf") && !Match(contains, "[a-mid][dle-b]"));
            var exact = Set((FilenameConditionKind.Equal, "a b")); Check(Match(exact, "[a b]") && !Match(exact, "[ a b ]"));
        });
        add("선택 조건 값은 정규식 메타문자도 리터럴", () =>
        {
            foreach (var value in new[] { "x.*", "a|b", "(?i)", "a+b", "x$", "a\\b" })
            {
                var set = Set((FilenameConditionKind.Equal, value));
                Check(Match(set, "[" + value + "]")); Check(!Match(set, "[anything]"));
            }
        });
        add("선택 대상 괄호와 JSON·규칙 변환은 조건을 보존", () =>
        {
            using var f = new Fixture(); var set = Set((FilenameConditionKind.Equal, "업무"));
            foreach (var kind in Enum.GetValues<BracketKind>())
            {
                set.Brackets = kind; var pair = FilenameGroups.Pair(kind); var filename = $"{pair.Open}업무{pair.Close}.pdf";
                var options = set.ToOptions(true); Check(Match(set, filename));
                var rule = Storage.Clone(FilenameGroups.Rule(f.Folder, "업무", options)); rule.Destination = f.Target;
                PathSafety.ValidateRule(f.Folder, rule);
                Check(rule.GroupConditions is not null && rule.GroupConditions.Compile() == rule.GroupExpression && rule.Recursive);
                Check(RuleMatcher.Matches(rule, filename, 0));
            }
        });
        add("조건에서 분석·그룹 규칙·단일 검사까지 같은 범위 유지", () =>
        {
            using var f = new Fixture();
            foreach (var name in new[] { "[xyz] one.pdf", "[x] two.pdf", "[y] three.pdf", "[yellow] four.pdf", "plain.pdf" }) f.Write(name, "unchanged");
            var set = Set((FilenameConditionKind.StartsWith, "x"), (FilenameConditionKind.Equal, "y"));
            var options = set.ToOptions();
            using var store = FilenameAnalysis.Scan([f.Folder], [], Path.Combine(f.Root, "analysis"), options);
            Check(store.Folders().Single().Matched == 3 && store.GroupCount(f.Folder.Id) == 3);
            Check(store.Files(f.Folder.Id, "xyz").Single().Name == "[xyz] one.pdf");
            set.Terms[0].Value = "changed";
            Check(store.Options.Conditions!.Terms[0].Value == "x");
            var rule = FilenameGroups.Rule(f.Folder, "xyz", store.Options); rule.Destination = f.Target;
            f.Folder.Rules = [Storage.Clone(rule)];
            var plan = f.Scan(); Check(plan.Entries.Count == 1 && Path.GetFileName(plan.Entries[0].Source) == "[xyz] one.pdf");
            Check(!Directory.Exists(f.Target) && File.ReadAllText(f.SourceFile("[xyz] one.pdf")) == "unchanged");
        });
        add("전체 선택 조건의 식은 문자열 비교와 일치", () =>
        {
            var values = new[] { "x", "y", "xy", "x+y", "axyb", "x.*", "Y", "a b", "업무", "x y" };
            foreach (var kind in Enum.GetValues<FilenameConditionKind>())
                foreach (var all in new[] { false, true })
                    foreach (var brackets in Enum.GetValues<BracketKind>())
                    {
                        var set = Set((kind, "x"), (FilenameConditionKind.Equal, "y")); set.All = all; set.Brackets = brackets;
                        var pair = FilenameGroups.Pair(brackets);
                        foreach (var value in values)
                        {
                            var first = kind switch { FilenameConditionKind.StartsWith => value.StartsWith("x", StringComparison.Ordinal),
                                FilenameConditionKind.EndsWith => value.EndsWith("x", StringComparison.Ordinal), FilenameConditionKind.Contains => value.Contains("x", StringComparison.Ordinal), _ => value == "x" };
                            var expected = all ? first && value == "y" : first || value == "y";
                            Check(Match(set, $"{pair.Open}{value}{pair.Close}.pdf") == expected);
                        }
                    }
        });
        add("통합 분류의 제한 없음은 괄호별 기본 집계와 중첩 제외를 보존", () =>
        {
            foreach (var kind in Enum.GetValues<BracketKind>())
                foreach (var include in new[] { true, false })
                {
                    var json = "{\"Brackets\":\"" + kind + "\",\"LimitContent\":false,\"IncludeBrackets\":" + (include ? "true" : "false")
                        + ",\"Terms\":[{\"Kind\":\"Equal\",\"Value\":\"\"}]}";
                    var set = System.Text.Json.JsonSerializer.Deserialize<FilenameConditionSet>(json)!;
                    var (a, z) = FilenameGroups.Pair(kind);
                    var name = $"{a}업무{z}{a}업무{z}{a} {z}{a}outer{a}inner{z}{z}{a}개인{z}{a}unclosed{a}skip{z}";
                    var options = set.ToOptions();
                    FilenameGroups.Validate(options);
                    Check(FilenameGroups.Extract(name, options).SetEquals(include ? new[] { $"{a}업무{z}", $"{a}개인{z}" } : new[] { "업무", "개인" }));
                    Check(options.CustomPattern == ""); // 제한 없음은 기존 괄호 파서를 사용한다.
                }
        });
        add("통합 분류의 괄호 포함과 분석 파생 규칙은 같은 파일을 선택", () =>
        {
            using var f = new Fixture();
            foreach (var limited in new[] { true, false })
                foreach (var include in new[] { true, false })
                {
                    var set = System.Text.Json.JsonSerializer.Deserialize<FilenameConditionSet>(
                        "{\"LimitContent\":" + (limited ? "true" : "false") + ",\"IncludeBrackets\":" + (include ? "true" : "false")
                        + ",\"Terms\":[{\"Kind\":\"StartsWith\",\"Value\":\"x\"}]}")!;
                    var options = set.ToOptions(); var key = include ? "[xyz]" : "xyz";
                    Check(FilenameGroups.Extract("[xyz][yellow]", options).Contains(key));
                    var rule = Storage.Clone(FilenameGroups.Rule(f.Folder, key, options)); rule.Destination = f.Target;
                    PathSafety.ValidateRule(f.Folder, rule);
                    Check(RuleMatcher.Matches(rule, "[xyz].pdf", 0));
                    Check(!RuleMatcher.Matches(rule, "[yellow].pdf", 0));
                    Check(FilenameGroups.Extract("[xyz].pdf", rule.GroupConditions!.ToOptions()).Contains(key));
                }
        });
        add("통합 제한 없음의 미리보기·디스크 분석·규칙 검사는 동일", () =>
        {
            using var f = new Fixture();
            foreach (var name in new[] { "[업무] one.pdf", "[업무] two.pdf", "[개인].pdf", "[a[업무]].pdf", "plain.pdf" }) f.Write(name, "unchanged");
            var set = FilenameConditionSet.Unrestricted(); set.IncludeBrackets = false;
            var options = set.ToOptions();
            var preview = FilenameGroups.Preview("[업무][a[개인]]", options);
            Check(preview.Matches.SequenceEqual(new[] { "[업무]" }) && preview.Groups.SetEquals(new[] { "업무" }));
            using var store = FilenameAnalysis.Scan([f.Folder], [], Path.Combine(f.Root, "analysis"), options);
            Check(store.Folders().Single().Matched == 3 && store.GroupCount(f.Folder.Id) == 2);
            Check(store.FileCount(f.Folder.Id, "업무") == 2);
            var rule = FilenameGroups.Rule(f.Folder, "업무", store.Options); rule.Destination = f.Target;
            f.Folder.Rules = [Storage.Clone(rule)]; var plan = f.Scan();
            Check(plan.Entries.Count == 2 && plan.Entries.All(e => Path.GetFileName(e.Source).StartsWith("[업무]")));
            Check(!Directory.Exists(f.Target));
            set.LimitContent = true; // 숨겨 둔 기존 조건과 다른 모드가 저장된 분석을 바꾸지 않는다.
            Check(store.Options.Conditions!.LimitContent == false);
        });
        add("제한 없는 내용 그룹의 공백·대소문자와 파생 규칙은 정확히 보존", () =>
        {
            using var f = new Fixture(); var set = FilenameConditionSet.Unrestricted(); set.IncludeBrackets = false;
            var options = set.ToOptions(); var key = " 업무 ";
            Check(FilenameGroups.Extract("[ 업무 ][업무][업무 ][업무]", options).SetEquals(new[] { " 업무 ", "업무", "업무 " }));
            var rule = Storage.Clone(FilenameGroups.Rule(f.Folder, key, options)); rule.Destination = f.Target;
            PathSafety.ValidateRule(f.Folder, rule);
            Check(rule.Pattern == key && RuleMatcher.Matches(rule, "[ 업무 ].pdf", 0) && !RuleMatcher.Matches(rule, "[업무].pdf", 0));
            Check(FilenameGroups.Extract("[x][X][ x]", options).SetEquals(new[] { "x", "X", " x" }));
        });
        add("기존 조건 JSON은 내용 제한과 괄호 없는 그룹 의미를 보존", () =>
        {
            var set = System.Text.Json.JsonSerializer.Deserialize<FilenameConditionSet>(
                "{\"Brackets\":\"Round\",\"All\":false,\"Terms\":[{\"Kind\":\"Equal\",\"Value\":\"업무\"}]}")!;
            Check(FilenameGroups.Extract("(업무)(개인)", set.ToOptions()).SetEquals(new[] { "업무" }));
            Check(set.Compile().Length > 0);
        });
        add("선택 조건 오류·길이·부정확 메타데이터 차단", () =>
        {
            using var f = new Fixture();
            Throws(() => new FilenameConditionSet { Terms = [] }.Compile());
            Throws(() => Set((FilenameConditionKind.Equal, " ")).Compile());
            Throws(() => Set((FilenameConditionKind.Equal, "[x]")).Compile());
            Throws(() => Set((FilenameConditionKind.Equal, "x\n")).Compile());
            Throws(() => Set((FilenameConditionKind.Equal, new string('x', 101))).Compile());
            Throws(() => new FilenameConditionSet { Terms = Enumerable.Range(0, 11).Select(_ => new FilenameCondition { Value = "x" }).ToList() }.Compile());
            var options = new FilenameConditionSet().ToOptions();
            Throws(() => FilenameGroups.Validate(options with { CustomPattern = "different" }));
            var rule = FilenameGroups.Rule(f.Folder, "x", options); rule.Destination = f.Target; rule.GroupExpression = "different";
            Throws(() => PathSafety.ValidateRule(f.Folder, rule));
        });
    }
}
