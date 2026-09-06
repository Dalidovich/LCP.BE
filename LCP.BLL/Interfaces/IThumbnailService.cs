using LCP.BLL.DTOs;

namespace LCP.BLL.Interfaces;

public interface IThumbnailService
{
    Task<MediaIdentity?> GetIdentityAsync(string videoId, double? timecode = null);
    Task<ThumbnailResult?> GetThumbnailAsync(string videoId, double? timecode = null);
    void InvalidateCache(string videoId);
    void ClearAllCache();
}
