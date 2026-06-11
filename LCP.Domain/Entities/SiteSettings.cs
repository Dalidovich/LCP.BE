namespace LCP.Domain.Entities;

public class SiteSettings
{
    public string Theme { get; set; } = "dark";
    public bool AnimeSpeedUp { get; set; }
    public bool WarmCache { get; set; }
}
