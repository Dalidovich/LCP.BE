namespace LCP.BLL.DTOs;

public sealed record MomentCluster(string VideoId, int Start, int End, int MomentCount, long WatchedSeconds, int[] Heat)
{
    public int Duration => End - Start;
    public double Popularity => Duration > 0 ? (double)WatchedSeconds / Duration : 0;
}
