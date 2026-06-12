namespace LCP.BLL.DTOs;

public class SettingsDto
{
    public string Theme { get; set; } = "dark";
    public bool AnimeSpeedUp { get; set; }
    public bool WarmCache { get; set; }
    public bool Debug { get; set; }
    public bool StatisticsMode { get; set; }
}
