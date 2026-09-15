namespace LCP.BLL.DTOs;

public sealed record CompilationCandidate(string VideoId, int Start, int Duration, double Popularity, double Score, int[] Heat);
