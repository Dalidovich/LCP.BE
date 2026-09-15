namespace LCP.BLL.DTOs;

public sealed record SelectedMoment(string VideoId, int Start, int Duration, double Popularity, double Score);
