using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace TimeGuard.Parent;

public static class UpdateLauncher
{
    public static async Task<string> DownloadAndLaunchAsync(string assetName, CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(Path.GetTempPath(), assetName);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        byte[] bytes = await client.GetByteArrayAsync($"https://github.com/kbelousov-ency/TimeGuard/releases/latest/download/{assetName}", cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return path;
    }
}
