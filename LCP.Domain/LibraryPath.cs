namespace LCP.Domain;

public static class LibraryPath
{
    public static string Normalize(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return relativePath;

        return relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
    }

    public static string Combine(string rootPath, string relativePath) =>
        Path.Combine(rootPath, Normalize(relativePath));

    public static string ToArchiveEntryName(string relativePath) =>
        Normalize(relativePath).Replace(Path.DirectorySeparatorChar, '/');
}
