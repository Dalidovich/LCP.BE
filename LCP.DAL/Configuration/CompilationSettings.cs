namespace LCP.DAL.Configuration;

public class CompilationSettings
{
    public double MaxDurationSeconds { get; set; } = 600;
    public int MergeGapSeconds { get; set; } = 3;
    public int MinMomentSeconds { get; set; } = 5;
    public int MaxMomentSeconds { get; set; } = 30;
    public int MaxCandidatesPerCluster { get; set; } = 3;
    public int MinDistanceSeconds { get; set; } = 5;
    public double MaxCoveragePercent { get; set; } = 0.25;
    public double MaxCoverageSeconds { get; set; } = 120;
    public double VideoRepeatPenalty { get; set; } = 0.5;
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
}
