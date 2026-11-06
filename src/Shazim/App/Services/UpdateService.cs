using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Shazim.Core.Interfaces;
using Shazim.Core.Logging;
using Shazim.Core.Updates;

namespace Shazim.App.Services;

public enum UpdateCheckStatus { UpToDate, Available, Failed, Skipped }

public sealed record UpdateCheckResult(UpdateCheckStatus Status, ReleaseInfo? Release = null, string Message = "");

/// <summary>
/// Finds new versions on GitHub (public API, no token), downloads the installer, checks its
/// SHA-256 against the release's SHA256SUMS.txt and hands over to it ("--setup --quiet").
/// </summary>
public sealed class UpdateService
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/painfullfeet/Shazim/releases/latest";
    private const string UserAgent = "Shazim-Updater";
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(20);

    private readonly IAppHttpClientFactory _http;
    private readonly SettingsService _settings;
    private readonly ILogger<UpdateService> _logger;

    public UpdateService(IAppHttpClientFactory http, SettingsService settings, ILogger<UpdateService> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    public static Version CurrentVersion
    {
        get
        {
            var v = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>True when the automatic check is due (on, and not done in the last ~day).</summary>
    public bool AutoCheckDue()
    {
        var u = _settings.GetSettings().Updates;
        return u.AutoCheck && (u.LastCheckUtc is not { } last || DateTime.UtcNow - last > CheckEvery || last > DateTime.UtcNow);
    }

    /// <summary>
    /// Asks GitHub for the latest release. Never throws: offline, rate limits and odd answers
    /// give <see cref="UpdateCheckStatus.Failed"/> with a short message.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken ct = default)
    {
        var s = _settings.GetSettings();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));

            using var response = await _http.CreateClient().SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                int code = (int)response.StatusCode;
                _logger.LogInformation("Update check: HTTP {Status}", code);
                return new UpdateCheckResult(UpdateCheckStatus.Failed, Message: code is 403 or 429
                    ? Loc.T("update.rate_limited") : Loc.F("update.http_error", code));
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            s.Updates.LastCheckUtc = DateTime.UtcNow;
            _settings.Save();

            var release = UpdateParser.ParseRelease(json);
            if (release == null || release.SetupAsset == null)
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
            if (!UpdateParser.IsNewer(release.Version, CurrentVersion))
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, release);
            if (!manual && string.Equals(s.Updates.SkippedVersion, release.DisplayVersion, StringComparison.OrdinalIgnoreCase))
                return new UpdateCheckResult(UpdateCheckStatus.Skipped, release);

            _logger.LogInformation("Update available: {Version}", release.DisplayVersion);
            return new UpdateCheckResult(UpdateCheckStatus.Available, release);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, Message: Loc.T("update.offline"));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogInformation("Update check offline: {Message}", ex.Message);
            return new UpdateCheckResult(UpdateCheckStatus.Failed, Message: Loc.T("update.offline"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Update check failed: {Message}", ex.Message);
            return new UpdateCheckResult(UpdateCheckStatus.Failed, Message: Loc.T("update.offline"));
        }
    }

    public void Skip(ReleaseInfo release)
    {
        _settings.GetSettings().Updates.SkippedVersion = release.DisplayVersion;
        _settings.Save();
    }

    /// <summary>
    /// Downloads the installer to %TEMP%\Shazim-Update and verifies it. Returns its path.
    /// Throws <see cref="UpdateException"/> with a readable message on any problem.
    /// </summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default)
    {
        var setup = release.SetupAsset ?? throw new UpdateException(Loc.T("update.no_installer"));
        var sumsAsset = release.ChecksumAsset ?? throw new UpdateException(Loc.T("update.no_checksum"));
        var client = _http.CreateClient();

        try
        {
            // 1. The expected checksum.
            string sums;
            using (var sumsRequest = Get(sumsAsset.DownloadUrl))
            using (var timeout = Timeout(ct, 20))
            using (var r = await client.SendAsync(sumsRequest, timeout.Token).ConfigureAwait(false))
            {
                r.EnsureSuccessStatusCode();
                sums = await r.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            }
            var expected = UpdateParser.FindChecksum(sums, setup.Name) ?? throw new UpdateException(Loc.T("update.no_checksum"));

            // 2. The installer, with progress.
            var dir = Path.Combine(Path.GetTempPath(), "Shazim-Update");
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, Path.GetFileName(setup.Name));
            var partial = target + ".part";

            using (var request = Get(setup.DownloadUrl))
            using (var timeout = Timeout(ct, 600))
            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? setup.Size;
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                await using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                    done += read;
                    if (total > 0) progress?.Report(Math.Min(1, (double)done / total));
                }
            }

            // 3. Refuse anything that does not match the published checksum.
            var actual = await UpdateParser.HashFileAsync(partial, ct).ConfigureAwait(false);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(partial);
                _logger.LogWarning("Update rejected: checksum mismatch");
                throw new UpdateException(Loc.T("update.bad_checksum"));
            }
            TryDelete(target);
            File.Move(partial, target);
            progress?.Report(1);
            return target;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Update download failed: {Message}", ex.Message);
            throw new UpdateException(Loc.T("update.download_failed"));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new UpdateException(Loc.T("update.download_failed"));
        }
        catch (IOException ex)
        {
            _logger.LogWarning("Update download failed: {Message}", ex.Message);
            throw new UpdateException(Loc.T("update.download_failed"));
        }
    }

    /// <summary>Starts the verified installer quietly; it replaces Shazim and starts it again.</summary>
    public void Install(string installerPath)
    {
        _logger.LogInformation("Starting the installer for the update");
        Process.Start(new ProcessStartInfo(installerPath, "--setup --quiet")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installerPath)!
        });
    }

    private static HttpRequestMessage Get(string url)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, url);
        r.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        return r;
    }

    private static CancellationTokenSource Timeout(CancellationToken ct, int seconds)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return cts;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

public sealed class UpdateException : Exception
{
    public UpdateException(string message) : base(message) { }
}
