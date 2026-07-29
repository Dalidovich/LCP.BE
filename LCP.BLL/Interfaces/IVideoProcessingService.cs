using LCP.BLL.DTOs;
using LCP.Domain.Entities;

namespace LCP.BLL.Interfaces;

public interface IVideoProcessingService
{
    double ProbeDuration(string videoPath);
    byte[]? ExtractFrame(string videoPath, double timecode);
    byte[]? GeneratePreview(string videoPath, PreviewResolution resolution, List<PreviewSlice> slices);
}
