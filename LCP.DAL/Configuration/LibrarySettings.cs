namespace LCP.DAL.Configuration;

public class LibrarySettings
{
    public const string SectionName = "LibrarySettings";

    public const string JsonFileName = "library.json";
    public const string TagsFileName = "tags.json";
    public const string ProductionInfoFileName = "productionInfo.json";
    public const string SettingsFileName = "settings.json";

    public string LibraryRootPath { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool SmartVideoGrouping { get; set; }

    public string ResolveSystemFilePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(LibraryRootPath))
            return string.Empty;

        return Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.GetFullPath(Path.Combine(LibraryRootPath, "SYSTEMFILES", relativePath));
    }
}
