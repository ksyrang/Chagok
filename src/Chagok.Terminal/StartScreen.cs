using FileOrganizer.Core;

namespace Chagok.Terminal;

public static class StartScreen
{
    public static IReadOnlyList<string> Build(int width, FolderSpec? folder)
    {
        var left = (width - 1) / 2; var right = width - left - 1;
        var rows = new List<string>(BrandBanner.Build(width))
        {
            "차곡 · 정리 방식 선택",
            "기본 검사 폴더: " + (folder?.Name ?? "없음 · F로 등록하거나 2로 임시 검사"),
            folder?.Path ?? "기준 폴더 방식은 미등록 폴더도 이번에만 검사할 수 있습니다.",
            Dashboard.Top("1 규칙으로 정리", left) + " " + Dashboard.Top("2 기준 폴더로 정리", right),
            Dashboard.Row("선택한 등록 폴더의", left) + " " + Dashboard.Row("정리된 하위 폴더명으로", right),
            Dashboard.Row("기존 규칙 목록을 엽니다.", left) + " " + Dashboard.Row("목적지 후보를 찾습니다.", right),
            Dashboard.Row("R 개별 / A 폴더 전체", left) + " " + Dashboard.Row("검사 위치 변경 가능", right),
            Dashboard.Row("기존 검사·실행 유지", left) + " " + Dashboard.Row("후보 확인 후 명시 실행", right),
            Dashboard.Bottom(left) + " " + Dashboard.Bottom(right),
            "↑↓ 기본 폴더 선택 · 1/Enter 기존 규칙 · 2 기준 폴더",
            "P 파일명 분석 · F 폴더 등록 · H 기록 · S 설정",
            "F1/? 도움말 · Q/Esc 종료 · Ctrl+C 안전하게 중단"
        };
        return rows.Select(s => TerminalText.Fit(s, width)).ToArray();
    }
}
