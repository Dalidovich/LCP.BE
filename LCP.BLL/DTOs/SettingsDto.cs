using LCP.Domain.Entities;

namespace LCP.BLL.DTOs;

public class SettingsDto
{
    public string Theme { get; set; } = "dark";
    public bool AnimeSpeedUp { get; set; }
    public bool WarmCache { get; set; }
    public bool RandomSort { get; set; }
    public bool Debug { get; set; }
    public bool StatisticsMode { get; set; }
    public List<VideoType> VideoTypeFilter { get; set; } = [];
}
