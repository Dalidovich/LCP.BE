using System.Diagnostics;
using System.Text.RegularExpressions;
using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.Domain.Entities;
using Microsoft.Extensions.Logging;
using NReco.VideoConverter;

namespace LCP.BLL.Services;

public class VideoProcessingService : IVideoProcessingService
{
    private readonly ILogger<VideoProcessingService> _logger;
    private static string? _ffmpegExePath;

    public VideoProcessingService(ILogger<VideoProcessingService> logger)
    {
        _logger = logger;
    }

    private static string GetFfmpegExePath()
    {
        if (_ffmpegExePath is not null)
            return _ffmpegExePath;

        var toolDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LCP", "ffmpeg");
        Directory.CreateDirectory(toolDir);

        var converter = new FFMpegConverter();
        converter.FFMpegToolPath = toolDir;
        converter.ExtractFFmpeg();

        var exePath = Path.Combine(toolDir, converter.FFMpegExeName);
        if (!File.Exists(exePath))
            throw new InvalidOperationException($"ffmpeg not found at {exePath}");

        _ffmpegExePath = exePath;
        return exePath;
    }

    public double ProbeDuration(string videoPath)
    {
        try
        {
            var ffmpegPath = GetFfmpegExePath();

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-i \"{videoPath}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("Failed to start ffmpeg process for {VideoPath}", videoPath);
                return 0;
            }

            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            var match = Regex.Match(stderr, @"Duration: (\d+):(\d+):(\d+)\.(\d+)");
            if (match.Success)
            {
                var h = int.Parse(match.Groups[1].Value);
                var m = int.Parse(match.Groups[2].Value);
                var s = int.Parse(match.Groups[3].Value);
                var ms = int.Parse(match.Groups[4].Value.PadRight(3, '0')[..3]);
                return new TimeSpan(0, h, m, s, ms).TotalSeconds;
            }

            _logger.LogWarning("Could not parse duration from ffmpeg output for {VideoPath}", videoPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to probe duration for {VideoPath}", videoPath);
        }
        return 0;
    }

    public byte[]? ExtractFrame(string videoPath, double timecode)
    {
        try
        {
            var ffmpeg = new FFMpegConverter();
            ffmpeg.FFMpegToolPath = Path.GetDirectoryName(GetFfmpegExePath())!;

            using var ms = new MemoryStream();

            float? frameTime = timecode >= 0
                ? (float)timecode
                : 1f;

            _logger.LogInformation("Generating thumbnail for {VideoPath} at {Seek}s", videoPath, frameTime);

            ffmpeg.GetVideoThumbnail(videoPath, ms, frameTime);
            return ms.Length > 0 ? ms.ToArray() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate thumbnail for {VideoPath}", videoPath);
            return null;
        }
    }

    public byte[]? GeneratePreview(string videoPath, PreviewResolution resolution, List<PreviewSlice> slices)
    {
        var (width, height) = resolution switch
        {
            PreviewResolution.Preview360 => (640, 360),
            _ => (256, 144)
        };

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var ffmpeg = new FFMpegConverter();
            ffmpeg.FFMpegToolPath = Path.GetDirectoryName(GetFfmpegExePath())!;

            if (slices.Count == 1)
            {
                using var ms = new MemoryStream();
                ffmpeg.ConvertMedia(videoPath, null, ms, Format.mp4, new ConvertSettings
                {
                    Seek = (float)slices[0].Start,
                    MaxDuration = (float)slices[0].Duration,
                    CustomOutputArgs = $"-an -preset ultrafast -vf scale={width}:{height}"
                });

                _logger.LogInformation("Generated {Resolution} preview for {VideoPath} ({Size} bytes)",
                    resolution, videoPath, ms.Length);

                return ms.Length > 0 ? ms.ToArray() : null;
            }

            var segmentFiles = new List<string>();
            for (var i = 0; i < slices.Count; i++)
            {
                var segFile = Path.Combine(tempDir, $"seg{i}.mp4");
                ffmpeg.ConvertMedia(videoPath, null, segFile, Format.mp4, new ConvertSettings
                {
                    Seek = (float)slices[i].Start,
                    MaxDuration = (float)slices[i].Duration,
                    CustomOutputArgs = $"-an -preset ultrafast -vf scale={width}:{height}"
                });
                segmentFiles.Add(segFile);
            }

            var outputFile = Path.Combine(tempDir, "preview.mp4");
            ffmpeg.ConcatMedia(segmentFiles.ToArray(), outputFile, Format.mp4, new ConcatSettings
            {
                ConcatVideoStream = true,
                ConcatAudioStream = false,
                CustomOutputArgs = "-preset ultrafast"
            });

            var data = File.ReadAllBytes(outputFile);
            _logger.LogInformation(
                "Generated {Resolution} preview for {VideoPath} ({Size} bytes) from {Count} slices",
                resolution, videoPath, data.Length, slices.Count);

            return data.Length > 0 ? data : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate {Resolution} preview for {VideoPath}",
                resolution, videoPath);
            return null;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
