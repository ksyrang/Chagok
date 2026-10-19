using System.Text.Json;
using System.Text.Encodings.Web;

namespace Chagok.Terminal;

public sealed record OptionHelp(string Name, string Type, string Description, bool Required = false,
    string? Default = null, string[]? Choices = null);
public sealed record CommandHelp(string Name, string Title, string Description, string Usage,
    string[] Effects, string Confirmation, bool WindowsOnly, OptionHelp[] Options, string[] Examples, string[] Notes);

public static class HelpCatalog
{
    public const string Version = "0.5.6";
    public const string Executable = "Chagok-v0.5.6.exe";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static OptionHelp O(string name, string description, bool required = false, string type = "string", string? fallback = null, params string[] choices)
        => new(name, type, description, required, fallback, choices.Length == 0 ? null : choices);
    private static readonly OptionHelp Folder = O("folder", "등록된 검사 폴더의 전체 ID 또는 이름", true);
    private static readonly OptionHelp Rule = O("rule", "선택 폴더에 등록된 규칙의 전체 ID 또는 이름", true);
    private static readonly OptionHelp Yes = O("yes", "전체 범위 실행 또는 설정 제거를 미리 승인합니다. 값을 쓰지 않는 플래그입니다.", type: "flag", fallback: "false");
    private static readonly OptionHelp[] Trigger = [
        O("trigger", "실행 조건. 프로그램 감지는 watch가 실행 중일 때만 동작합니다.", false, "enum", null, "manual", "start", "stop"),
        O("program", "start/stop 조건의 실제 .exe 전체 경로. 해당 조건을 저장하려면 파일이 있어야 합니다.", type: "absolutePath"),
        O("mode", "감지 후 목록 확인 또는 자동 실행. auto는 파일을 확인 없이 이동할 수 있습니다.", false, "enum", null, "confirm", "auto")];
    private static readonly OptionHelp[] FolderFields = [O("name", "표시 이름"), O("path", "존재하는 검사 폴더의 전체 경로. 다른 검사 폴더와 겹칠 수 없습니다.", type: "absolutePath"),
        O("exclude", "제외할 하위 폴더 전체 경로를 세미콜론으로 구분합니다.", type: "pathList"), ..Trigger];
    private static readonly OptionHelp[] RuleFields = [O("name", "규칙 이름"), O("destination", "이동할 폴더의 전체 경로", type: "absolutePath"),
        O("match", "파일명 비교 방식", false, "enum", null, "any", "contains", "starts", "glob"),
        O("pattern", "contains/starts/glob에서 필수인 단어 또는 와일드카드 패턴"),
        O("extensions", "쉼표로 구분한 확장자. 빈 값이면 전체 파일 형식입니다."),
        O("recursive", "하위 폴더 포함", false, "boolean", null, "on", "off", "true", "false"),
        O("conflict", "이름 충돌 처리. replace는 기존 대상을 휴지통으로 보낸 뒤 교체합니다.", false, "enum", null, "rename", "replace", "skip"),
        O("create", "목적지 폴더 자동 생성", false, "boolean", null, "on", "off", "true", "false"),
        O("enabled", "규칙 사용 여부", false, "boolean", null, "on", "off", "true", "false"), ..Trigger];
    public static IReadOnlyList<OptionHelp> CommonOptions { get; } = Array.AsReadOnly(new[] {
        O("json", "결과를 JSON으로 출력합니다. TUI에서는 사용할 수 없습니다. watch는 NDJSON입니다.", type: "flag", fallback: "false"),
        O("data-dir", "별도 설정·기록 폴더의 전체 경로. 기본값은 GUI와 같은 %LOCALAPPDATA%\\FileOrganizer입니다. help/version은 설정을 읽지 않습니다.", type: "absolutePath") });
    public static IReadOnlyList<string> Safety { get; } = Array.AsReadOnly(new[] {
        "GUI·CLI·TUI는 같은 설정을 사용하며 한 번에 하나만 실행합니다. 트레이 GUI도 실행 중입니다. help/version은 예외입니다.",
        "scan은 파일을 이동하지 않습니다. run은 이전 scan을 재사용하지 않고 새로 검사합니다.",
        "--yes는 현재 페이지가 아니라 전체 실행 가능한 항목을 승인합니다. 파일 이동과 설정 제거는 사용자 승인 범위에서 실행하세요.",
        "같은 이름이 여러 개면 전체 ID를 사용합니다. update에서 생략한 값은 유지합니다.",
        "remove는 등록 설정만 제거합니다. 실제 폴더·파일을 삭제하지 않습니다.",
        "복사·해시 검증 후 원본을 삭제합니다. 휴지통 실패 시 영구 삭제로 대체하지 않습니다. 취소해도 이미 끝난 이동은 유지됩니다.",
        "파일 내용 중복 검사·예약 실행·자동 되돌리기는 제공하지 않습니다. 무결성 해시와 중복 정리는 다릅니다." });
    public static IReadOnlyDictionary<int, string> ExitCodes { get; } = new Dictionary<int, string> {
        [0] = "성공", [1] = "실행 오류", [2] = "잘못된 인수 또는 승인 필요", [3] = "다른 차곡 사용 중", [4] = "부분 처리·경고 등 확인 필요", [130] = "취소" };
    private static CommandHelp C(string name, string title, string description, string usage, string[] effects,
        string confirmation, OptionHelp[] options, string[] examples, string[]? notes = null, bool windows = false)
        => new(name, title, description, Executable + " " + usage, effects, confirmation, windows, options,
            examples.Select(e => Executable + " " + e).ToArray(), notes ?? []);
    private static OptionHelp[] Required(OptionHelp[] fields, params string[] names)
        => fields.Select(o => names.Contains(o.Name) ? o with { Required = true } : o).ToArray();
    public static IReadOnlyList<CommandHelp> Commands { get; } = Array.AsReadOnly(new[] {
        C("tui", "화면으로 사용", "폴더·규칙을 보고 선택한 항목을 검사·실행합니다. F1 또는 ?로 기능 도움말을 엽니다.", "tui", ["설정 편집 가능", "확인 후 파일 이동 가능"], "이동 전 전체 선택 개수로 확인", [], ["tui"],
            ["Tab 영역 전환 → ↑↓ 규칙 선택 → R 이 규칙만 검사 → Enter → Y로 이동합니다. A는 폴더 전체입니다.", "F 폴더 추가, N 규칙 추가, E 편집, Space 규칙 켜기/끄기. 입력 중 Esc는 저장하지 않고 돌아가기입니다.", "S 설정은 ↑↓/1~4로 항목을 골라 ←→/Enter/Space로 값을 바꿉니다. S 저장, Esc 변경 취소입니다.", "폴더/규칙 폼은 ↑↓/Tab/1~9로 항목 선택, Enter/E 텍스트 편집, ←→ 선택값 변경, I 전체 값 확인입니다. S 검토 후 Y 저장. 입력칸 Esc는 항목 취소, 목록 Esc는 전체 폼 취소입니다.", "대화형 터미널이 필요하며 --json은 지원하지 않습니다."]),
        C("folders list", "검사 폴더 조회", "등록된 폴더의 ID·이름·경로·규칙 개수·실행 조건을 조회합니다.", "folders list", ["설정 조회"], "없음", [], ["folders list --json"]),
        C("folders add", "검사 폴더 추가", "이름·검사 위치·제외 경로·실행 조건을 등록합니다. 저장만으로 파일은 이동하지 않습니다.", "folders add --name 이름 --path 전체경로", ["설정 저장"], "명령 실행으로 저장", Required(FolderFields, "name", "path"), ["folders add --name 다운로드 --path C:\\Data\\Inbox"], ["폴더 기본값: 수동, 목록 확인. 검사 폴더는 이미 존재해야 합니다."]),
        C("folders update", "검사 폴더 편집", "지정한 필드만 바꾸고 나머지는 유지합니다. 검사 경로 변경 시 생략한 제외 경로는 같은 상대 위치로 재배치합니다.", "folders update --folder ID [옵션]", ["설정 저장"], "명령 실행으로 저장", [Folder, ..FolderFields], ["folders update --folder 다운로드 --name 받은파일"]),
        C("folders clone", "폴더와 규칙 복제", "새 이름·검사 경로로 폴더와 모든 규칙을 복제합니다. 새 ID를 부여하고 원본은 유지합니다.", "folders clone --folder ID --name 새이름 --path 새경로", ["설정 저장"], "명령 실행으로 저장", [Folder, ..Required(FolderFields, "name", "path")], ["folders clone --folder 다운로드 --name 다른자료 --path D:\\Inbox"], ["규칙의 이동 목적지는 유지합니다. 겹치는 검사 경로는 거부합니다."]),
        C("folders remove", "검사 폴더 등록 제거", "폴더와 그 폴더의 규칙 설정을 제거합니다. 실제 파일과 폴더는 유지합니다.", "folders remove --folder ID [--yes]", ["등록 설정 제거"], "대화형 yes 또는 --yes. JSON/리디렉션에서는 --yes 필수", [Folder, Yes], ["folders remove --folder 다운로드 --yes"]),
        C("rules list", "규칙 조회", "선택한 검사 폴더의 규칙과 전체 ID·조건·목적지·사용 여부를 조회합니다.", "rules list --folder ID", ["설정 조회"], "없음", [Folder], ["rules list --folder 다운로드 --json"]),
        C("rules add", "규칙 추가", "파일명·확장자 조건, 이동 목적지, 이름 충돌 처리와 실행 조건을 저장합니다.", "rules add --folder ID --name 이름 --destination 전체경로 --match any|contains|starts|glob", ["설정 저장"], "명령 실행으로 저장", [Folder, ..Required(RuleFields, "name", "destination", "match")], ["rules add --folder 다운로드 --name PDF --destination D:\\Sorted\\PDF --match any --extensions .pdf"], ["기본값: 켜짐, 하위 폴더 제외, 목적지 자동 생성, 이름 번호 추가, 수동, 목록 확인.", "한 파일은 위에서 처음 일치하는 켜진 규칙 하나로 처리합니다. R 또는 run --rule로 개별 범위를 지정할 수 있습니다."]),
        C("rules update", "규칙 편집", "선택한 규칙의 지정한 필드만 수정합니다. 생략한 값과 ID·순서를 유지합니다.", "rules update --folder ID --rule ID [옵션]", ["설정 저장"], "명령 실행으로 저장", [Folder, Rule, ..RuleFields], ["rules update --folder 다운로드 --rule PDF --enabled off"]),
        C("rules remove", "규칙 제거", "선택한 규칙 설정만 제거합니다. 이미 이동한 파일과 원본 폴더는 유지합니다.", "rules remove --folder ID --rule ID [--yes]", ["등록 설정 제거"], "대화형 yes 또는 --yes. JSON/리디렉션에서는 --yes 필수", [Folder, Rule, Yes], ["rules remove --folder 다운로드 --rule PDF --yes"]),
        C("scan", "이동 대상 검사", "폴더 또는 개별 규칙의 이동 계획을 만듭니다. 파일은 이동하지 않고 결과 100건씩 조회합니다.", "scan --folder ID [--rule ID] [--page 1]", ["설정 조회", "임시 검사 캐시 생성·정리"], "없음", [Folder, Rule with { Required = false }, O("page", "100건 단위 페이지 번호", type: "integer", fallback: "1")], ["scan --folder 다운로드 --rule PDF --json", "scan --folder 다운로드 --page 2 --json"], ["run은 이 계획을 재사용하지 않습니다. TUI에서는 검사한 목록의 개별 파일을 선택할 수 있습니다."]),
        C("run", "검사 후 이동", "새로 검사한 실행 가능 항목 전체를 승인 후 이동합니다. --rule이 있으면 그 규칙만 검사합니다.", "run --folder ID [--rule ID] [--yes]", ["파일 복사·검증·원본 삭제", "조건에 따라 폴더 생성·기존 대상 휴지통 이동", "실행 기록 저장"], "대화형 yes 또는 --yes. JSON/리디렉션에서는 --yes 필수", [Folder, Rule with { Required = false }, Yes], ["run --folder 다운로드 --rule PDF", "run --folder 다운로드 --rule PDF --yes --json"], ["--yes는 모든 페이지의 실행 가능한 항목 승인입니다. 이전 scan 이후 바뀐 파일도 새 검사에 포함될 수 있습니다.", "자동화는 먼저 scan 결과를 검토하고 사용자가 허용한 범위에서만 --yes로 실행하세요."]),
        C("history", "실행 기록 조회", "최근 감지·실행 기록에서 시간·원본·대상·결과·이유를 조회합니다.", "history [--limit 20]", ["실행 기록 조회"], "없음", [O("limit", "최근 기록 개수: 1~500", type: "integer", fallback: "20")], ["history --limit 50 --json"]),
        C("settings show", "공유 설정 조회", "GUI·CLI·TUI가 공유하는 앱 설정과 등록 정보를 조회합니다.", "settings show", ["설정 조회"], "없음", [], ["settings show --json"]),
        C("settings set", "앱 설정 변경", "GUI 테마·창 닫기·종료 확인·탐색기 메뉴 자동 확인 설정을 저장합니다.", "settings set [옵션]", ["설정 저장"], "명령 실행으로 저장", [O("theme", "GUI 테마", false, "enum", null, "system", "light", "dark"), O("close", "GUI 창 닫기 동작", false, "enum", null, "tray", "exit"), O("confirm-exit", "GUI 종료 확인", false, "boolean", null, "on", "off", "true", "false"), O("auto-menu", "다음 GUI 시작 시 탐색기 메뉴 자동 확인", false, "boolean", null, "on", "off", "true", "false")], ["settings set --theme dark --close tray"], ["TUI 색은 터미널 팔레트를 따릅니다. 탐색기 등록은 다음 GUI 실행에서 처리합니다."]),
        C("watch", "프로그램 감지", "Windows 프로그램 시작·종료를 감지해 저장된 폴더·규칙 조건을 처리합니다.", "watch [--folder ID] [--yes]", ["프로그램 감지", "auto 조건이면 파일 자동 이동 가능", "감지·실행 기록 저장"], "자동 조건이 있으면 시작 전 yes 또는 --yes 필요. confirm 조건은 대기", [Folder with { Required = false }, Yes], ["watch --folder 다운로드", "watch --yes --json"], ["Windows 전용이며 실행 중인 동안만 감지합니다. 예약 실행이 아닙니다.", "JSON은 이벤트 한 줄씩 출력하는 NDJSON입니다. confirm 요청을 자동 승인하지 않습니다.", "WMI 실패 시 250ms 대체 확인을 사용합니다. 짧은 프로그램 실행과 경로 조회 제한 때문에 감지를 놓칠 수 있습니다."], windows: true),
        C("version", "버전 조회", "실행 버전을 조회합니다. 설정과 잠금을 열지 않습니다.", "version [--json]", ["정보 출력"], "없음", [], ["version --json"]),
        C("help", "기능 설명 조회", "명령별 입력·효과·승인·예시를 읽습니다. JSON은 AI와 자동화가 읽을 수 있는 구조입니다.", "help [명령] [--json]", ["정보 출력"], "없음", [], ["help run", "rules add --help", "help --json"], ["설정과 잠금을 열지 않아 GUI/TUI 실행 중에도 읽을 수 있습니다."])
    });
    public static CommandHelp[] Find(string topic)
    {
        topic = topic.Trim();
        var found = Commands.Where(c => topic.Length == 0 || c.Name == topic || c.Name.StartsWith(topic + " ", StringComparison.Ordinal)).ToArray();
        if (found.Length == 0) throw new ArgumentException("설명이 없는 명령입니다: " + topic + ". help로 목록을 확인하세요.");
        return found;
    }
    public static string Serialize(string topic) => JsonSerializer.Serialize(new { schemaVersion = 1, program = "Chagok", version = Version,
        executable = Executable, commands = Find(topic), commonOptions = CommonOptions, safety = Safety, exitCodes = ExitCodes }, Json);
    public static string[] Describe(CommandHelp command) => [command.Title + " / " + command.Name, "", command.Description,
        "", "사용법: " + command.Usage, "변경·효과: " + string.Join(" / ", command.Effects), "승인: " + command.Confirmation,
        "환경: " + (command.WindowsOnly ? "Windows 전용" : "Windows 배포 / 중립 로직은 macOS 개발 검증 가능"),
        "", "입력값", ..command.Options.Select(o => $"--{o.Name} ({o.Type}, {(o.Required ? "필수" : "선택")}) {o.Description}" + (o.Default is null ? "" : " 기본값: " + o.Default) + (o.Choices is null ? "" : " 허용값: " + string.Join('|', o.Choices))),
        "", "예시", ..command.Examples, "", "알아둘 점", ..command.Notes];
    public static string Text(string topic)
    {
        var found = Find(topic);
        if (topic.Length > 0) return string.Join(Environment.NewLine, found.SelectMany(c => Describe(c).Append("")))
            + "\n공통 옵션: --json / --data-dir 전체경로. help --json에서 공통 옵션·안전 기준·종료 코드를 확인하세요.";
        return $"차곡 · Chagok {Version}\n\n" + string.Join('\n', Commands.Select(c => c.Name.PadRight(18) + " " + c.Title + " · " + c.Description))
            + $"\n\n{Executable} help run: 개별 기능 설명\n{Executable} help --json: AI용 전체 명세\nTUI에서는 F1 또는 ?로 도움말\n\n" + string.Join('\n', Safety);
    }
}
