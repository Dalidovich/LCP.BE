using System.Diagnostics;
using System.Text.RegularExpressions;
using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NReco.VideoConverter;

namespace LCP.BLL.Services;

public class VideoProcessingService : IVideoProcessingService
{
    private readonly ILogger<VideoProcessingService> _logger;
    private readonly TimeSpan _probeTimeout;
    private readonly TimeSpan _convertTimeout;
    private static string? _ffmpegExePath;
    private static readonly SemaphoreSlim FfmpegLimiter = new(Math.Max(1, Environment.ProcessorCount / 2));

    public VideoProcessingService(ILogger<VideoProcessingService> logger, IOptions<LibrarySettings> settings)
    {
        _logger = logger;
        _probeTimeout = ResolveTimeout(settings.Value.FfmpegProbeTimeoutSeconds, 30);
        _convertTimeout = ResolveTimeout(settings.Value.FfmpegConvertTimeoutSeconds, 300);
    }

    private static TimeSpan ResolveTimeout(int configuredSeconds, int fallbackSeconds)
    {
        return TimeSpan.FromSeconds(configuredSeconds > 0 ? configuredSeconds : fallbackSeconds);
    }

    private static string GetFfmpegExePath()
    {
        if (_ffmpegExePath is not null)
            return _ffmpegExePath;

        var candidates = new[]
        {
            AppContext.BaseDirectory,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LCP", "ffmpeg")
        };

        foreach (var toolDir in candidates)
        {
            try
            {
                Directory.CreateDirectory(toolDir);

                var converter = new FFMpegConverter();
                converter.FFMpegToolPath = toolDir;
                converter.ExtractFFmpeg();

                var exePath = Path.Combine(toolDir, converter.FFMpegExeName);
                if (!File.Exists(exePath))
                    continue;

                _ffmpegExePath = exePath;
                return exePath;
            }
            catch (Exception)
            {
            }
        }

        throw new InvalidOperationException(
            "ffmpeg could not be extracted (tried exe directory and %LOCALAPPDATA%\\LCP\\ffmpeg)");
    }

    private static T RunThrottled<T>(Func<T> operation)
    {
        FfmpegLimiter.Wait();
        try
        {
            return operation();
        }
        finally
        {
            FfmpegLimiter.Release();
        }
    }

    public double ProbeDuration(string videoPath)
    {
        return RunThrottled(() => ProbeDurationCore(videoPath));
    }

    private double ProbeDurationCore(string videoPath)
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

            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)_probeTimeout.TotalMilliseconds))
            {
                _logger.LogWarning(
                    "ffmpeg probe exceeded {TimeoutSeconds}s for {VideoPath}; killing the process and reporting an unknown duration",
                    _probeTimeout.TotalSeconds, videoPath);
                KillProcessTree(process, videoPath);
                ObserveFailure(stderrTask);
                return 0;
            }

            var stderr = stderrTask.GetAwaiter().GetResult();

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

    private void KillProcessTree(Process process, string videoPath)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            process.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to kill the stalled ffmpeg process for {VideoPath}", videoPath);
        }
    }

    private static void ObserveFailure(Task task)
    {
        task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    private bool RunConverterBounded(FFMpegConverter converter, Action conversion, string videoPath)
    {
        var conversionTask = Task.Run(conversion);

        using var timeoutSource = new CancellationTokenSource();
        var timeout = Task.Delay(_convertTimeout, timeoutSource.Token);
        var finished = Task.WhenAny(conversionTask, timeout).GetAwaiter().GetResult();

        if (finished == conversionTask)
        {
            timeoutSource.Cancel();
            conversionTask.GetAwaiter().GetResult();
            return true;
        }

        _logger.LogWarning("ffmpeg conversion exceeded {TimeoutSeconds}s for {VideoPath}; aborting",
            _convertTimeout.TotalSeconds, videoPath);

        try
        {
            converter.Abort();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to abort the stalled ffmpeg conversion for {VideoPath}", videoPath);
        }

        ObserveFailure(conversionTask);
        return false;
    }

    public byte[]? ExtractFrame(string videoPath, double timecode)
    {
        return RunThrottled(() => ExtractFrameCore(videoPath, timecode));
    }

    private byte[]? ExtractFrameCore(string videoPath, double timecode)
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

            if (!RunConverterBounded(ffmpeg, () => ffmpeg.GetVideoThumbnail(videoPath, ms, frameTime), videoPath))
                return null;

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
        return RunThrottled(() => GeneratePreviewCore(videoPath, resolution, slices));
    }

    private byte[]? GeneratePreviewCore(string videoPath, PreviewResolution resolution, List<PreviewSlice> slices)
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
                var singleSliceDone = RunConverterBounded(ffmpeg, () =>
                    ffmpeg.ConvertMedia(videoPath, null, ms, Format.mp4, new ConvertSettings
                    {
                        Seek = (float)slices[0].Start,
                        MaxDuration = (float)slices[0].Duration,
                        CustomOutputArgs = $"-an -preset ultrafast -vf scale={width}:{height}"
                    }), videoPath);

                if (!singleSliceDone)
                    return null;

                _logger.LogInformation("Generated {Resolution} preview for {VideoPath} ({Size} bytes)",
                    resolution, videoPath, ms.Length);

                return ms.Length > 0 ? ms.ToArray() : null;
            }

            var segmentFiles = new List<string>();
            for (var i = 0; i < slices.Count; i++)
            {
                var segFile = Path.Combine(tempDir, $"seg{i}.mp4");
                var slice = slices[i];
                var segmentDone = RunConverterBounded(ffmpeg, () =>
                    ffmpeg.ConvertMedia(videoPath, null, segFile, Format.mp4, new ConvertSettings
                    {
                        Seek = (float)slice.Start,
                        MaxDuration = (float)slice.Duration,
                        CustomOutputArgs = $"-an -preset ultrafast -vf scale={width}:{height}"
                    }), videoPath);

                if (!segmentDone)
                    return null;

                segmentFiles.Add(segFile);
            }

            var outputFile = Path.Combine(tempDir, "preview.mp4");
            var concatDone = RunConverterBounded(ffmpeg, () =>
                ffmpeg.ConcatMedia(segmentFiles.ToArray(), outputFile, Format.mp4, new ConcatSettings
                {
                    ConcatVideoStream = true,
                    ConcatAudioStream = false,
                    CustomOutputArgs = "-preset ultrafast"
                }), videoPath);

            if (!concatDone)
                return null;

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
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete the temporary preview directory {TempDir}", tempDir);
            }
        }
    }
}
