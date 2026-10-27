namespace Chagok.Core;

public sealed record ProcessObservation(int ProcessId, string? ExecutablePath);
public sealed record ProcessTransition(string Path, TriggerMode Trigger);

public sealed class ProcessPresenceTracker(IEnumerable<string> targets)
{
    private readonly HashSet<string> targetPaths = new(targets.Select(PathSafety.Normalize), StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, string> active = [];
    private readonly HashSet<int> initiallyUnreadable = [];
    private bool initialized;

    public IReadOnlyList<ProcessTransition> Observe(IEnumerable<ProcessObservation> observations, bool complete = true)
    {
        if (!complete) return [];
        var next = new Dictionary<int, string>(); var observed = new HashSet<int>();
        foreach (var observation in observations)
        {
            observed.Add(observation.ProcessId);
            if (observation.ExecutablePath is { } path)
            {
                path = PathSafety.Normalize(path);
                if (targetPaths.Contains(path)) next[observation.ProcessId] = path;
            }
            else
            {
                if (!initialized) initiallyUnreadable.Add(observation.ProcessId);
                if (active.TryGetValue(observation.ProcessId, out var previous)) next[observation.ProcessId] = previous;
            }
        }
        var before = active.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var after = next.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // 빈 표본이 없어도 기존 실행본이 전부 사라졌으면 재실행으로 구분한다.
        var restarted = before.Intersect(after, StringComparer.OrdinalIgnoreCase)
            .Where(path => !next.Any(item => StringComparer.OrdinalIgnoreCase.Equals(item.Value, path)
                && active.TryGetValue(item.Key, out var previous) && StringComparer.OrdinalIgnoreCase.Equals(previous, path)))
            .ToArray();
        active = next;
        if (!initialized) { initialized = true; return []; }
        var transitions = before.Except(after, StringComparer.OrdinalIgnoreCase).Concat(restarted)
            .Select(p => new ProcessTransition(p, TriggerMode.ProgramStopped))
            .Concat(after.Except(before, StringComparer.OrdinalIgnoreCase).Concat(restarted)
                .Where(p => next.Any(item => StringComparer.OrdinalIgnoreCase.Equals(item.Value, p) && !initiallyUnreadable.Contains(item.Key)))
                .Select(p => new ProcessTransition(p, TriggerMode.ProgramStarted))).ToArray();
        initiallyUnreadable.IntersectWith(observed);
        initiallyUnreadable.ExceptWith(next.Keys);
        return transitions;
    }
}
