namespace LCP.BLL.DTOs;

public class CompilationDto
{
    public string Id { get; set; } = string.Empty;
    public double Duration { get; set; }
    public List<CompilationMomentDto> Moments { get; set; } = [];
}

public class CompilationMomentDto
{
    public string VideoId { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public double Offset { get; set; }
    public int Start { get; set; }
    public int Duration { get; set; }
}
