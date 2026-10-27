namespace Chagok.Core;

public static class FolderDuplicator
{
    public static FolderSpec Create(FolderSpec source)
    {
        var copy = Storage.Clone(source);
        copy.Id = Guid.NewGuid();
        copy.Name = source.Name + " 복사본";
        foreach (var rule in copy.Rules) rule.Id = Guid.NewGuid();
        return copy;
    }

    public static List<string> RebaseExclusions(string oldSource, string newSource, IEnumerable<string> paths)
    {
        var oldPath = PathSafety.Normalize(oldSource);
        var newPath = PathSafety.Normalize(newSource);
        return PathSafety.NormalizeExclusions(newPath, paths.Select(path =>
            PathSafety.IsWithin(path, oldPath)
                ? Path.Combine(newPath, Path.GetRelativePath(oldPath, PathSafety.Normalize(path)))
                : path));
    }
}
