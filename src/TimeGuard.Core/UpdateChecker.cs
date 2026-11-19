using System.Net.Http;

namespace TimeGuard.Core;

/// <summary>Результат сравнения установленной версии с последним релизом GitHub.</summary>
public sealed record UpdateCheckResult(Version Current, Version Latest)
{
    public bool IsNewer => UpdateChecker.IsNewer(Latest, Current);
}

/// <summary>
/// Определяет, есть ли релиз новее установленной версии. Скачивать установщик нужно только при <see cref="UpdateCheckResult.IsNewer"/>.
/// Версия приложения берётся из сборки (<c>Directory.Build.props</c>), версия релиза — из тега <c>vX.Y.Z</c>.
/// </summary>
public static class UpdateChecker
{
    private const string LatestReleaseUrl = "https://github.com/kbelousov-ency/TimeGuard/releases/latest";

    /// <summary>Версия установленной сборки в виде X.Y.Z.</summary>
    public static Version CurrentVersion => Normalize(typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>Читает тег последнего релиза без API (редирект /releases/latest → /releases/tag/vX.Y.Z), лимитов запросов нет.</summary>
    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        using HttpResponseMessage response = await client.GetAsync(
            LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        Uri? location = response.Headers.Location;
        if (location is null)
        {
            throw new InvalidOperationException($"GitHub не вернул адрес последнего релиза (HTTP {(int)response.StatusCode}).");
        }
        string tag = Uri.UnescapeDataString(location.OriginalString.TrimEnd('/').Split('/')[^1]);
        Version latest = ParseTag(tag)
            ?? throw new InvalidOperationException("На GitHub пока нет опубликованных релизов с версией.");
        return new UpdateCheckResult(CurrentVersion, latest);
    }

    /// <summary>«v1.2.4» или «1.2.4» → 1.2.4; всё остальное → null.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string text = tag.Trim();
        if (text.Length > 1 && (text[0] == 'v' || text[0] == 'V')) text = text[1..];
        return Version.TryParse(text, out Version? version) && version.Major >= 0 && version.Minor >= 0
            ? Normalize(version)
            : null;
    }

    public static bool IsNewer(Version latest, Version current) => Normalize(latest) > Normalize(current);

    // У «1.2.4» Revision = -1, и такая версия меньше «1.2.4.0»; приводим к одному виду.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
