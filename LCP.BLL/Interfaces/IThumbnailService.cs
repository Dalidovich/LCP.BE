using LCP.BLL.DTOs;

namespace LCP.BLL.Interfaces;

public interface IThumbnailService
{
    Task<ThumbnailResult?> GetThumbnailAsync(string videoId);
    Task<ThumbnailResult?> GetThumbnailPreviewAsync(string videoId, double timecode);
    void InvalidateCache(string videoId);
}
