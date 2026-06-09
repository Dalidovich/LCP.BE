namespace LCP.DAL.Configuration;

public class LibrarySettings
{
    public const string SectionName = "LibrarySettings";

    public string JsonFilePath { get; set; } = "library.json";
    public string TagsFilePath { get; set; } = "tags.json";
    public string LibraryRootPath { get; set; } = string.Empty;

    public string ResolveSystemFilePath(string relativePath)
    {
        return Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.GetFullPath(Path.Combine(LibraryRootPath, "SYSTEMFILES", relativePath));
    }
}
