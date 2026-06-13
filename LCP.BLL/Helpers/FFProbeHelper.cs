using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NReco.VideoConverter;

namespace LCP.BLL.Helpers;

public static class FFProbeHelper
{
    public static double ProbeDuration(string videoPath, ILogger? logger = null)
    {
        try
        {
            var probe = new FFMpegConverter();
            probe.ExtractFFmpeg();

            var ffmpegPath = Path.Combine(probe.FFMpegToolPath, probe.FFMpegExeName);
            if (!File.Exists(ffmpegPath))
            {
                logger?.LogWarning("ffmpeg not found at {Path}", ffmpegPath);
                return 0;
            }

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
                logger?.LogWarning("Failed to start ffmpeg process for {VideoPath}", videoPath);
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

            logger?.LogWarning("Could not parse duration from ffmpeg output for {VideoPath}", videoPath);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to probe duration for {VideoPath}", videoPath);
        }
        return 0;
    }
}
