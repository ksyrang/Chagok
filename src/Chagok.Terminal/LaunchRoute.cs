namespace Chagok.Terminal;

public static class LaunchRoute
{
    public static bool IsGui(IReadOnlyList<string> args)
        => args.Count == 0 || args[0] == "--add-rule";
}
