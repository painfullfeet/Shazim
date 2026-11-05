using Shazim.Core.Audio;
using Shazim.Core.Interfaces;
using Shazim.Core.Logging;
using Shazim.Core.Models;
using Shazim.Core.Services;

namespace Shazim.Core.Recognition;

public enum IdentifyPhase { Listening, Searching }

public readonly record struct IdentifyProgress(IdentifyPhase Phase, TimeSpan Elapsed, TimeSpan MaxDuration, float Level);

public enum IdentifyOutcomeKind { Match, NoMatch, Silence, Error, Cancelled }

public sealed class IdentifyOutcome
{
    public IdentifyOutcomeKind Kind { get; init; }
    public Track? Track { get; init; }
    public string Message { get; init; } = "";
    public bool IsAuto { get; init; }

    /// <summary>Source that heard the track (PC output or microphone name).</summary>
    public string SourceName { get; init; } = "";

    /// <summary>Auto mode found a track already detected within the duplicate window (not recorded again).</summary>
    public bool IsDuplicate { get; init; }
    public TimeSpan Duration { get; init; }
}

/// <param name="DeviceIds">One or more sources listened to at the same time (PC output and/or microphone).</param>
public sealed record IdentifyRequest(IReadOnlyList<string> DeviceIds, bool IsAuto, int MaxDurationMs = 12000, int DuplicateWindowMinutes = 15)
{
    public IdentifyRequest(string deviceId, bool IsAuto, int MaxDurationMs = 12000, int DuplicateWindowMinutes = 15)
        : this(new[] { deviceId }, IsAuto, MaxDurationMs, DuplicateWindowMinutes) { }
}

/// <summary>
/// Full identification pipeline: listen to every requested source at once, try to recognise
/// early (after 4 s), keep listening and retry with more audio (8 s, 12 s), then save the result.
/// </summary>
public class IdentificationService
{
    private static readonly TimeSpan SignatureWindow = TimeSpan.FromSeconds(ShazamSignature.MaxSeconds);
    private const float SilenceRms = 0.0012f; // about -58 dBFS

    private readonly IAudioCaptureService _capture;
    private readonly IMusicRecognitionService _recognition;
    private readonly LibraryService _library;
    private readonly DeduplicationService _dedup;
    private readonly ILogger<IdentificationService> _logger;

    public IdentificationService(IAudioCaptureService capture, IMusicRecognitionService recognition, LibraryService library,
        DeduplicationService dedup, ILogger<IdentificationService> logger)
    {
        _capture = capture;
        _recognition = recognition;
        _library = library;
        _dedup = dedup;
        _logger = logger;
    }

    /// <summary>Raised (on a worker thread) after a match was saved to the library.</summary>
    public event EventHandler<IdentifyOutcome>? TrackIdentified;

    public async Task<IdentifyOutcome> IdentifyAsync(IdentifyRequest request, IProgress<IdentifyProgress>? progress = null,
        CancellationToken ct = default)
    {
        var max = TimeSpan.FromMilliseconds(Math.Clamp(request.MaxDurationMs, 4000, 20000));
        var checkpoints = BuildCheckpoints(max);
        var started = DateTime.UtcNow;

        var sessions = new List<ICaptureSession>();
        string? openError = null;
        try
        {
            foreach (var id in request.DeviceIds.Distinct())
            {
                try { sessions.Add(await Task.Run(() => _capture.StartSession(id), ct).ConfigureAwait(false)); }
                catch (AudioCaptureException ex) { openError = ex.Message; _logger.LogWarning("Source {Id} unavailable: {Message}", id, ex.Message); }
            }
        }
        catch (OperationCanceledException)
        {
            sessions.ForEach(s => s.Dispose());
            return new IdentifyOutcome { Kind = IdentifyOutcomeKind.Cancelled, IsAuto = request.IsAuto };
        }

        if (sessions.Count == 0)
            return new IdentifyOutcome { Kind = IdentifyOutcomeKind.Error, IsAuto = request.IsAuto, Message = openError ?? "Aucune source audio disponible." };

        try
        {
            RecognitionResult? lastResult = null;
            bool heardSomething = false;
            var clock = sessions[0];

            for (int i = 0; i < checkpoints.Count; i++)
            {
                while (clock.Elapsed < checkpoints[i])
                {
                    progress?.Report(new IdentifyProgress(IdentifyPhase.Listening, clock.Elapsed, max, sessions.Max(s => s.Level)));
                    await Task.Delay(80, ct).ConfigureAwait(false);
                }

                // Every source that hears something is searched in parallel.
                var audible = sessions
                    .Select(s => (Session: s, Clip: s.Snapshot(SignatureWindow)))
                    .Where(x => x.Clip.Duration >= TimeSpan.FromSeconds(1.5) && AudioDsp.Rms(x.Clip.Samples) >= SilenceRms)
                    .ToList();
                if (audible.Count == 0)
                {
                    _logger.LogDebug("Checkpoint {Index}: silence on every source", i);
                    continue;
                }

                heardSomething = true;
                progress?.Report(new IdentifyProgress(IdentifyPhase.Searching, clock.Elapsed, max, sessions.Max(s => s.Level)));

                var results = await Task.WhenAll(audible.Select(async x =>
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    RecognitionResult r;
                    try { r = await _recognition.IdentifyAsync(x.Clip, ct).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Recognition crashed");
                        r = RecognitionResult.Failed(_recognition.ServiceName, CoreText.L("core.unexpected", "Erreur inattendue pendant la reconnaissance."));
                    }
                    _logger.LogInformation("{Source}: {Status} in {Ms} ms {Error}", x.Session.DeviceName, r.Status.ToString(),
                        watch.ElapsedMilliseconds, r.Error ?? "");
                    return (x.Session, Result: r);
                })).ConfigureAwait(false);

                var match = results.FirstOrDefault(r => r.Result.IsMatch);
                if (match.Result != null)
                    return await SaveAsync(match.Result, request, match.Session.DeviceName, DateTime.UtcNow - started).ConfigureAwait(false);

                lastResult = results.Any(r => r.Result.Status == RecognitionStatus.NoMatch)
                    ? results.First(r => r.Result.Status == RecognitionStatus.NoMatch).Result
                    : results[0].Result;

                // Network problems will not fix themselves in 4 seconds.
                if (lastResult.Status == RecognitionStatus.Error && IsFatal(lastResult))
                    break;
            }

            if (!heardSomething)
            {
                var names = string.Join(CoreText.L("core.and", " et "), sessions.Select(s => $"« {s.DeviceName} »"));
                return new IdentifyOutcome
                {
                    Kind = IdentifyOutcomeKind.Silence,
                    IsAuto = request.IsAuto,
                    Message = CoreText.F("core.silence", "Aucun son détecté sur {0}. Lancez une musique puis réessayez.", names)
                };
            }

            if (lastResult is { Status: RecognitionStatus.Error })
                return new IdentifyOutcome { Kind = IdentifyOutcomeKind.Error, IsAuto = request.IsAuto, Message = lastResult.Error ?? CoreText.L("core.failed", "La reconnaissance a échoué.") };

            return new IdentifyOutcome
            {
                Kind = IdentifyOutcomeKind.NoMatch, IsAuto = request.IsAuto,
                Message = CoreText.L("core.no_match", "Morceau introuvable. Réessayez pendant un passage plus caractéristique (refrain, mélodie).")
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Identification cancelled after {Seconds:F1} s", (DateTime.UtcNow - started).TotalSeconds);
            return new IdentifyOutcome { Kind = IdentifyOutcomeKind.Cancelled, IsAuto = request.IsAuto };
        }
        finally
        {
            foreach (var s in sessions) s.Dispose();
        }
    }

    private static bool IsFatal(RecognitionResult result) => result.IsFatal;

    internal static List<TimeSpan> BuildCheckpoints(TimeSpan max)
    {
        var list = new List<TimeSpan>();
        for (var t = TimeSpan.FromSeconds(4); t < max; t += TimeSpan.FromSeconds(4))
            list.Add(t);
        list.Add(max);
        return list;
    }

    private async Task<IdentifyOutcome> SaveAsync(RecognitionResult result, IdentifyRequest request, string sourceName, TimeSpan duration)
    {
        var track = result.Track!;
        _dedup.SetWindow(request.DuplicateWindowMinutes);
        bool duplicate = _dedup.IsDuplicate(track.Id);

        if (!(request.IsAuto && duplicate))
        {
            await _library.AddTrackAsync(track).ConfigureAwait(false);
            await _library.RecordDetectionAsync(track.Id, result.Confidence, sourceName, request.IsAuto).ConfigureAwait(false);
        }

        _logger.LogInformation("Identified {Artist} - {Title} on {Source} in {Seconds:F1} s (duplicate: {Dup})",
            track.Artist, track.Title, sourceName, duration.TotalSeconds, duplicate);

        var stored = await _library.GetTrackAsync(track.Id).ConfigureAwait(false) ?? track;
        var outcome = new IdentifyOutcome
        {
            Kind = IdentifyOutcomeKind.Match,
            Track = stored,
            IsAuto = request.IsAuto,
            SourceName = sourceName,
            IsDuplicate = request.IsAuto && duplicate,
            Duration = duration
        };

        if (!outcome.IsDuplicate)
        {
            try { TrackIdentified?.Invoke(this, outcome); }
            catch (Exception ex) { _logger.LogError(ex, "TrackIdentified handler failed"); }
        }
        return outcome;
    }
}
