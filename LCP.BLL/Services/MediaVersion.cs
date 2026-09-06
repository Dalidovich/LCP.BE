using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LCP.BLL.Services;

internal readonly record struct MediaSource(DateTime LastWriteUtc, long Length);

internal static class MediaVersion
{
    public static MediaSource? Probe(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return null;

        var ticks = info.LastWriteTimeUtc.Ticks;
        var truncated = new DateTime(ticks - ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        return new MediaSource(truncated, info.Length);
    }

    public static string Compute(MediaSource source, string discriminator)
    {
        var payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{source.LastWriteUtc.Ticks}:{source.Length}:{discriminator}");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    public static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
