using LCP.BLL.DTOs;

namespace LCP.BLL.Interfaces;

public interface IPreviewService
{
    Task<MediaIdentity?> GetIdentityAsync(string videoId, PreviewResolution resolution);
    Task<PreviewResult?> GetPreviewAsync(string videoId, PreviewResolution resolution);
    void InvalidateCache(string videoId);
    void ClearAllCache();
}
