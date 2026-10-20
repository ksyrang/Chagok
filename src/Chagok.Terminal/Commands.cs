using System.Text.Encodings.Web;
using System.Text.Json;
using FileOrganizer.Core;

namespace Chagok.Terminal;

public sealed class Commands(TerminalSession session, IRecycleBin recycle, CancellationToken token)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static string Help => HelpCatalog.Text("");

    public async Task<int> RunAsync(Arguments args)
    {
        var route = string.Join(' ', args.Words);
        if (route is "" or "tui")
        {
            args.Allow();
            if (args.Json) throw new ArgumentException("TUI에서는 --json을 사용할 수 없습니다.");
            if (Console.IsInputRedirected || Console.IsOutputRedirected) throw new ArgumentException("TUI에는 대화형 터미널이 필요합니다. help 또는 scan 등 명령을 사용하세요.");
            await new Tui(session, recycle, token).RunAsync(); return 0;
        }
        switch (route)
        {
            case "folders list": args.Allow(); Write(session.Settings.Folders.Select(f => new { f.Id, f.Name, f.Path, Rules = f.Rules.Count, f.Trigger, f.ProgramPath, f.RunMode }), args); break;
            case "folders add": case "folders update": case "folders clone":
            {
                args.Allow("folder", "name", "path", "exclude", "trigger", "program", "mode", "recent-destinations");
                var folder = route == "folders add" ? new FolderSpec() : Storage.Clone(session.Folder(args.Require("folder")));
                var oldPath = folder.Path;
                if (route == "folders clone") { folder = FolderDuplicator.Create(folder); _ = args.Require("name"); _ = args.Require("path"); }
                if (route == "folders add") { _ = args.Require("name"); _ = args.Require("path"); }
                folder.Name = args.Get("name") ?? folder.Name; folder.Path = args.Get("path") ?? folder.Path;
                folder.UseRecentDestinations = args.Boolean("recent-destinations", folder.UseRecentDestinations);
                folder.ExcludedFolders = args.Get("exclude") is { } excluded ? SplitPaths(excluded)
                    : oldPath.Length > 0 && !PathSafety.Comparer.Equals(oldPath, folder.Path)
                        ? FolderDuplicator.RebaseExclusions(oldPath, folder.Path, folder.ExcludedFolders) : folder.ExcludedFolders;
                ApplyTrigger(args, folder);
                session.PutFolder(folder); Write(folder, args); break;
            }
            case "folders remove":
            {
                args.Allow("folder", "yes"); var folder = session.Folder(args.Require("folder"));
                await ConfirmAsync(args, $"‘{folder.Name}’ 설정과 규칙 {folder.Rules.Count}개를 제거합니다. 실제 파일은 삭제하지 않습니다.");
                session.RemoveFolder(folder.Id); Write(new { removed = folder.Id }, args); break;
            }
            case "rules list": args.Allow("folder"); Write(session.Folder(args.Require("folder")).Rules, args); break;
            case "rules add": case "rules update":
            {
                args.Allow("folder", "rule", "name", "destination", "match", "pattern", "group-expression", "extensions", "recursive", "conflict", "create", "enabled", "trigger", "program", "mode", "append-rule-name");
                var folder = session.Folder(args.Require("folder"));
                var rule = route == "rules add" ? new RuleSpec() : Storage.Clone(TerminalSession.Rule(folder, args.Require("rule")));
                if (route == "rules add") { _ = args.Require("name"); _ = args.Require("destination"); _ = args.Require("match"); }
                rule.Name = args.Get("name") ?? rule.Name; rule.Destination = args.Get("destination") ?? rule.Destination;
                rule.Pattern = args.Get("pattern") ?? rule.Pattern;
                rule.GroupExpression = args.Get("group-expression") ?? rule.GroupExpression; rule.Extensions = args.Get("extensions") ?? rule.Extensions;
                if (args.Get("match") is { } match) rule.Match = Choice(match, new Dictionary<string, NameMatch> { ["any"] = NameMatch.Any, ["contains"] = NameMatch.Contains, ["starts"] = NameMatch.StartsWith, ["glob"] = NameMatch.Glob, ["group"] = NameMatch.Group });
                if (args.Get("conflict") is { } conflict) rule.Conflict = Choice(conflict, new Dictionary<string, ConflictMode> { ["rename"] = ConflictMode.Rename, ["replace"] = ConflictMode.Replace, ["skip"] = ConflictMode.Skip });
                rule.Enabled = args.Boolean("enabled", rule.Enabled); rule.Recursive = args.Boolean("recursive", rule.Recursive);
                rule.CreateDestination = args.Boolean("create", rule.CreateDestination);
                rule.AppendRuleName = args.Boolean("append-rule-name", rule.AppendRuleName);
                var trigger = new FolderSpec { Trigger = rule.Trigger, ProgramPath = rule.ProgramPath, RunMode = rule.RunMode };
                ApplyTrigger(args, trigger); rule.Trigger = trigger.Trigger; rule.ProgramPath = trigger.ProgramPath; rule.RunMode = trigger.RunMode;
                session.PutRule(folder.Id, rule); Write(rule, args); break;
            }
            case "rules remove":
            {
                args.Allow("folder", "rule", "yes"); var folder = session.Folder(args.Require("folder")); var rule = TerminalSession.Rule(folder, args.Require("rule"));
                await ConfirmAsync(args, $"‘{rule.Name}’ 규칙 설정을 제거합니다. 실제 파일은 삭제하지 않습니다.");
                session.RemoveRule(folder.Id, rule.Id); Write(new { removed = rule.Id }, args); break;
            }
            case "analyze":
            {
                args.Allow("folder", "brackets", "recursive", "minimum", "group", "page", "file-page", "regex");
                var analysisScope = args.Get("folder") is { } f ? session.Folder(f).Id : (Guid?)null;
                var options = new AnalysisOptions(args.Get("brackets") is { } b ? Choice(b, new Dictionary<string, BracketKind>
                    { ["square"] = BracketKind.Square, ["round"] = BracketKind.Round, ["curly"] = BracketKind.Curly }) : BracketKind.Square,
                    args.Boolean("recursive", false), args.Get("regex") ?? "");
                FilenameGroups.Validate(options);
                var minimum = args.Number("minimum", 1, 1, int.MaxValue);
                var page = args.Number("page", 1, 1, int.MaxValue) - 1L;
                var filePage = args.Number("file-page", 1, 1, int.MaxValue) - 1L;
                var key = args.Get("group");
                if (key is not null && options.CustomPattern.Length == 0 && FilenameGroups.ValidateKey(key) != options.Brackets)
                    throw new ArgumentException("--group의 괄호와 --brackets가 다릅니다.");
                using var analysis = await Task.Run(() => session.Analyze(options, token, analysisScope), token);
                var groupCount = analysis.GroupCount(analysisScope, minimum);
                if (page > Math.Max(0, (groupCount - 1) / 100)) throw new ArgumentException("그룹 페이지 범위를 벗어났습니다.");
                var fileCount = key is null ? 0 : analysis.FileCount(analysisScope, key);
                if (filePage > Math.Max(0, (fileCount - 1) / 100)) throw new ArgumentException("파일 페이지 범위를 벗어났습니다.");
                Write(new { analysis.CreatedUtc, analysis.Options, Folders = analysis.Folders(), GroupCount = groupCount,
                    Page = page + 1, Groups = analysis.Groups(analysisScope, minimum, page), FileCount = fileCount,
                    FilePage = filePage + 1, Files = key is null ? [] : analysis.Files(analysisScope, key, filePage),
                    analysis.WarningCount, Warnings = analysis.Warnings() }, args);
                return analysis.WarningCount > 0 ? 4 : 0;
            }
            case "scan": case "run":
            {
                args.Allow(route == "scan" ? ["folder", "rule", "page"] : ["folder", "rule", "yes"]);
                var folder = session.Folder(args.Require("folder"));
                HashSet<Guid>? rules = args.Get("rule") is { } rule ? [TerminalSession.Rule(folder, rule).Id] : null;
                if (route == "run" && !args.Yes && (args.Json || Console.IsInputRedirected))
                    throw new ArgumentException("비대화형 실행에는 --yes가 필요합니다. 먼저 scan으로 대상을 확인하세요.");
                using var plan = await Task.Run(() => session.Scan(folder, rules, token), token);
                if (route == "scan") { Write(PlanView(plan, args.Number("page", 1, 1, int.MaxValue)), args); return plan.WarningCount > 0 ? 4 : 0; }
                if (!args.Json) Write(PlanView(plan, 1), args);
                if (plan.SelectedCount == 0) { Write(new { message = "실행 가능한 항목이 없습니다.", total = plan.Count, warnings = plan.WarningCount }, args); return plan.Count > 0 || plan.WarningCount > 0 ? 4 : 0; }
                await ConfirmAsync(args, $"전체 페이지의 선택 {plan.SelectedCount}개를 이동합니다. 기존 대상 교체 {plan.SelectedReplaceCount}개, 검사 경고 {plan.WarningCount}개.");
                var report = await session.ExecuteAsync(plan, recycle, args.Json || Console.IsOutputRedirected ? null : Progress, token);
                if (!args.Json && !Console.IsOutputRedirected) Console.WriteLine();
                Write(new { report.Title, report.Total, report.Processed, report.Completed, report.Deferred, report.Failed, report.Preserved, report.Canceled, report.Remaining, report.Outcome, report.Error, warnings = plan.WarningCount }, args);
                return Outcome(report, plan.WarningCount);
            }
            case "history": args.Allow("limit"); Write(session.Storage.ReadHistory(args.Number("limit", 20)), args); break;
            case "settings show": args.Allow(); Write(session.Settings, args); break;
            case "settings set":
            {
                args.Allow("theme", "close", "confirm-exit", "auto-menu"); var settings = Storage.Clone(session.Settings);
                if (args.Get("theme") is { } theme) settings.Theme = Choice(theme, new Dictionary<string, ThemeMode> { ["system"] = ThemeMode.System, ["light"] = ThemeMode.Light, ["dark"] = ThemeMode.Dark });
                if (args.Get("close") is { } close) settings.CloseAction = Choice(close, new Dictionary<string, WindowCloseAction> { ["tray"] = WindowCloseAction.HideToTray, ["exit"] = WindowCloseAction.Exit });
                settings.ConfirmExitOnClose = args.Boolean("confirm-exit", settings.ConfirmExitOnClose);
                settings.AutoRegisterExplorerMenu = args.Boolean("auto-menu", settings.AutoRegisterExplorerMenu);
                session.Save(settings); Write(settings, args); break;
            }
            case "watch":
                args.Allow("folder", "yes");
                var scope = args.Get("folder") is { } name ? session.Folder(name).Id : (Guid?)null;
                if (session.Settings.Folders.Where(f => scope is null || f.Id == scope).Any(f => f.RunMode == RunMode.Automatic && f.Trigger != TriggerMode.Manual || f.Rules.Any(r => r.Enabled && r.Trigger != TriggerMode.Manual && r.RunMode == RunMode.Automatic)))
                    await ConfirmAsync(args, "저장된 자동 실행 조건에 따라 파일이 이동할 수 있습니다.");
                await WatchRunner.RunAsync(session, recycle, token, scope, args.Json, false); break;
            default: throw new ArgumentException("알 수 없는 명령입니다. Chagok-v0.5.0.exe help를 확인하세요.");
        }
        return 0;
    }

    private static void ApplyTrigger(Arguments args, FolderSpec folder)
    {
        if (args.Get("trigger") is { } trigger) folder.Trigger = Choice(trigger, new Dictionary<string, TriggerMode> { ["manual"] = TriggerMode.Manual, ["start"] = TriggerMode.ProgramStarted, ["stop"] = TriggerMode.ProgramStopped });
        if (args.Get("mode") is { } mode) folder.RunMode = Choice(mode, new Dictionary<string, RunMode> { ["confirm"] = RunMode.Confirm, ["auto"] = RunMode.Automatic });
        folder.ProgramPath = args.Get("program") ?? folder.ProgramPath;
    }
    private static T Choice<T>(string value, Dictionary<string, T> choices) => choices.TryGetValue(value, out var result)
        ? result : throw new ArgumentException("지원하지 않는 값: " + value + " (" + string.Join('/', choices.Keys) + ")");
    private static List<string> SplitPaths(string value) => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    private async Task ConfirmAsync(Arguments args, string message)
    {
        if (args.Yes) return;
        if (args.Json || Console.IsInputRedirected) throw new ArgumentException("확인이 필요합니다. 내용을 검토한 뒤 --yes를 지정하세요.");
        Console.WriteLine(TerminalText.Clean(message)); Console.Write("계속하려면 yes 입력 (기본: 취소): ");
        var answer = await Console.In.ReadLineAsync(token);
        if (!string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase)) throw new OperationCanceledException();
    }
    private static object PlanView(DiskPlanStore plan, int page)
    {
        var pages = Math.Max(1, (plan.Count + 99) / 100);
        if (page > pages) throw new ArgumentException($"페이지 범위는 1~{pages}입니다.");
        return new { folder = plan.Header.FolderName, total = plan.Count, executable = plan.ExecutableCount,
            selected = plan.SelectedCount, replacements = plan.SelectedReplaceCount, page, pages,
            warnings = plan.WarningCount, warningDetails = plan.ReadWarnings(), entries = plan.ReadPage(((long)page - 1) * 100) };
    }
    private static void Write(object value, Arguments args) => Console.WriteLine(JsonSerializer.Serialize(value, args.Json ? null : Json));
    internal static int Outcome(ExecutionReport report, long warnings) => report.Outcome switch
    { ExecutionOutcome.Canceled => 130, ExecutionOutcome.Error => 1, ExecutionOutcome.Attention => 4, _ => warnings > 0 ? 4 : 0 };
    internal static void Progress(ExecutionReport report, PlanEntry entry, TransferProgress? transfer)
    {
        var current = transfer is null ? "" : $" · {transfer.Stage} {transfer.CopiedBytes}/{transfer.TotalBytes} bytes";
        var filled = (int)(report.Percent / 5);
        var bar = new string('█', filled) + new string('░', 20 - filled);
        TerminalText.Status($"[{bar}] {report.Percent:0}% (파일 수) {report.Processed}/{report.Total} · {Path.GetFileName(entry.Source)}{current}");
    }
}
