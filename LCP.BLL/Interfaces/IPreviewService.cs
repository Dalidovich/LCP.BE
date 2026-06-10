using LCP.BLL.DTOs;

namespace LCP.BLL.Interfaces;

public interface IPreviewService
{
    Task<PreviewResult?> GetPreviewAsync(string videoId, PreviewResolution resolution);
    void InvalidateCache(string videoId);
}
