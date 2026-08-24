namespace LCP.DAL.Configuration;

public class LibrarySettings
{
    public const string SectionName = "LibrarySettings";

    public const string JsonFileName = "library.json";
    public const string TagsFileName = "tags.json";
    public const string ProductionInfoFileName = "productionInfo.json";
    public const string SettingsFileName = "settings.json";

    public string LibraryRootPath { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public bool SmartVideoGrouping { get; set; }
    public double MaxSyncDeletionRatio { get; set; } = 0.5;
    public long ThumbnailCacheBytes { get; set; } = 64L * 1024 * 1024;
    public long PreviewCacheBytes { get; set; } = 512L * 1024 * 1024;
    public int FfmpegProbeTimeoutSeconds { get; set; } = 30;
    public int FfmpegConvertTimeoutSeconds { get; set; } = 300;
    public long MaxUploadBytes { get; set; } = 64L * 1024 * 1024 * 1024;

    public string ResolveSystemFilePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(LibraryRootPath))
            return string.Empty;

        return Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.GetFullPath(Path.Combine(LibraryRootPath, "SYSTEMFILES", relativePath));
    }
}
