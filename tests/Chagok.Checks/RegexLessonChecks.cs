using Chagok.Core;

static class RegexLessonChecks
{
    public static void Register(Action<string, Action> add)
    {
        void Check(bool value) { if (!value) throw new Exception("정규식 연습 안내 검사 실패"); }
        add("예시별 고정 문법과 수정 부분은 원래 식을 정확히 구성", () =>
        {
            Check(FilenameRegexLessons.All.Count == 4);
            foreach (var lesson in FilenameRegexLessons.All)
            {
                Check(lesson.TryGetFragment(lesson.Pattern, out var fragment) && fragment == lesson.Fragment);
                Check(lesson.Compose(fragment) == lesson.Pattern);
                Check(FilenameGroups.Preview(lesson.Sample, lesson.Pattern).Groups.Contains(lesson.ExpectedGroup));
                Check(!lesson.TryGetFragment("different", out _));
            }
        });
        add("강조 부분 수정은 고정 문법을 보존하며 x 시작 또는 y 정확 비교", () =>
        {
            foreach (var lesson in FilenameRegexLessons.All.Take(2))
            {
                var pattern = lesson.Compose(@"x[^\[\]]*|y");
                Check(lesson.TryGetFragment(pattern, out var fragment) && fragment == @"x[^\[\]]*|y");
                foreach (var name in new[] { "[xyz] one.pdf", "[x].pdf", "[y].pdf" }) Check(FilenameGroups.Preview(name, pattern).Groups.Count == 1);
                foreach (var name in new[] { "[yellow].pdf", "[ax].pdf", "[z].pdf" }) Check(FilenameGroups.Preview(name, pattern).Groups.Count == 0);
            }
        });
        add("연습 추천 식과 설명은 실제 그룹 결과와 일치", () =>
        {
            foreach (var lesson in FilenameRegexLessons.All)
                foreach (var choice in lesson.Choices)
                {
                    Check(choice.Explanation.Length > 0 && choice.Input.Length > 0);
                    var preview = FilenameGroups.Preview(choice.Input, lesson.Compose(choice.Fragment));
                    Check(preview.Groups.Contains(choice.ExpectedGroup));
                }
        });
        add("전체 직접 수정의 강조 분해는 다른 구조를 추측하지 않음", () =>
        {
            var lesson = FilenameRegexLessons.All[1];
            Check(!lesson.TryGetFragment(@"^(?<group>[^_]+)_", out _));
            Check(lesson.TryGetFragment(lesson.Compose("["), out var invalid) && invalid == "[");
            try { FilenameGroups.Preview("[x].pdf", lesson.Compose(invalid)); }
            catch (ArgumentException) { return; }
            throw new Exception("잘못된 수정 식을 오류로 알려야 함");
        });
    }
}
