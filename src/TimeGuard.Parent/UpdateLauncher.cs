using System.Diagnostics;
using TimeGuard.Core;

namespace TimeGuard.Parent;

public static class UpdateLauncher
{
    public static async Task<string> DownloadAndLaunchAsync(
        string assetName,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string path = await UpdateDownloader.DownloadAsync(assetName, progress, cancellationToken).ConfigureAwait(false);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
        return path;
    }
}
