using System.Net.Http;

namespace TimeGuard.Core;

public readonly record struct DownloadProgress(long ReceivedBytes, long? TotalBytes)
{
    public int? Percent => TotalBytes is > 0
        ? (int)Math.Clamp(ReceivedBytes * 100L / TotalBytes.Value, 0, 100)
        : null;
}

public static class UpdateDownloader
{
    private const string ReleaseBase = "https://github.com/kbelousov-ency/TimeGuard/releases/latest/download/";

    public static async Task<string> DownloadAsync(
        string assetName,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(Path.GetTempPath(), $"TimeGuard-{Guid.NewGuid():N}-{assetName}");
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using HttpResponseMessage response = await client.GetAsync(
            ReleaseBase + Uri.EscapeDataString(assetName),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
        byte[] buffer = new byte[128 * 1024];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(new DownloadProgress(received, total));
        }
        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        return path;
    }
}
