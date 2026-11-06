using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shazim.Core.Interfaces;

namespace Shazim.App;

public class AppSettings
{
    public const int CurrentVersion = 5;

    public int Version { get; set; } = CurrentVersion;
    public AudioSettings Audio { get; set; } = new();
    public AutoShazamSettings AutoShazam { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public UpdateSettings Updates { get; set; } = new();
    public WidgetSettings Widget { get; set; } = new();
    public LyricsSettings Lyrics { get; set; } = new();
}

public static class NotificationStyle
{
    /// <summary>Small card drawn like the app, above the taskbar.</summary>
    public const string Popup = "popup";
    /// <summary>The usual Windows notification.</summary>
    public const string Windows = "windows";
    public const string None = "none";
}

public static class ThemeMode
{
    public const string Night = "nuit";
    public const string Midnight = "minuit";
    public const string Light = "clair";
    /// <summary>Light or dark, like Windows apps.</summary>
    public const string System = "systeme";
}

public class UpdateSettings
{
    /// <summary>Look for a new version on GitHub when the app starts (at most once a day).</summary>
    public bool AutoCheck { get; set; } = true;
    public DateTime? LastCheckUtc { get; set; }

    /// <summary>"Ignorer cette version": this version is not offered again.</summary>
    public string SkippedVersion { get; set; } = "";
}

public class WidgetSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }

    /// <summary>0.55 (see-through) .. 1 (opaque).</summary>
    public double Opacity { get; set; } = 1;
    public bool ShowLyrics { get; set; } = true;
}

public class LyricsSettings
{
    /// <summary>Look up synced lyrics on LRCLIB for identified tracks.</summary>
    public bool Enabled { get; set; } = true;
}

public static class ButtonEffect
{
    /// <summary>Smooth layered aura following the spectrum, logo pulsing on the beat.</summary>
    public const string Aura = "aura";
    /// <summary>Only the logo, pulsing on the beat.</summary>
    public const string Pulse = "pulse";
    public const string Off = "off";
}

public static class AudioSource
{
    public const string Pc = "pc";
    public const string Mic = "mic";
    public const string Both = "both";
}

public class AudioSettings
{
    /// <summary>What to listen to: "pc" (what the PC plays), "mic" (the room) or "both".</summary>
    public string Source { get; set; } = AudioSource.Pc;

    /// <summary>"default" or "out:{endpoint id}".</summary>
    public string OutputDeviceId { get; set; } = AudioDevice.DefaultId;

    /// <summary>"in:default" or "in:{endpoint id}".</summary>
    public string MicDeviceId { get; set; } = AudioDevice.DefaultMicId;

    /// <summary>Maximum listening time for an identification.</summary>
    public int CaptureDurationMs { get; set; } = 12000;

    /// <summary>Legacy single-device setting (v1-v3), read once for migration.</summary>
    [JsonPropertyName("deviceId")]
    public string? LegacyDeviceId { get; set; }

    /// <summary>Sources to open for the current mode.</summary>
    public IReadOnlyList<string> DeviceIds() => Source switch
    {
        AudioSource.Mic => new[] { MicDeviceId },
        AudioSource.Both => new[] { OutputDeviceId, MicDeviceId },
        _ => new[] { OutputDeviceId }
    };
}

public class AutoShazamSettings
{
    public bool Enabled { get; set; }
    public int IntervalMs { get; set; } = 30000;
    public int CaptureDurationMs { get; set; } = 12000;
    public int DuplicateWindowMinutes { get; set; } = 15;

    /// <summary>Turn Auto Shazim on every time the app starts.</summary>
    public bool AutoStart { get; set; }
}

public class HotkeySettings
{
    public bool Enabled { get; set; } = true;
    public string Identify { get; set; } = "Ctrl+Shift+S";
    public string ToggleAuto { get; set; } = "Ctrl+Shift+A";
}

public class NotificationSettings
{
    /// <summary>False when <see cref="Style"/> is "none" (kept for older versions of the file).</summary>
    public bool Enabled { get; set; } = true;
    public bool AutoOnly { get; set; }

    /// <summary>See NotificationStyle.</summary>
    public string Style { get; set; } = NotificationStyle.Popup;

    /// <summary>Windows plays its notification sound.</summary>
    public bool Sound { get; set; } = true;
}

public class GeneralSettings
{
    /// <summary>Closing the window keeps the app running in the notification area.</summary>
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }

    /// <summary>When launched with Windows, start hidden in the notification area.</summary>
    public bool StartMinimized { get; set; } = true;

    /// <summary>How the big button reacts to the music playing on the PC (see ButtonEffect).</summary>
    public string ButtonEffect { get; set; } = global::Shazim.App.ButtonEffect.Aura;

    /// <summary>Earlier on/off switch, read once for migration.</summary>
    [JsonPropertyName("reactiveButton")]
    public bool? LegacyReactiveButton { get; set; }

    /// <summary>Volume of the 30-second previews, 0..1. Starts low.</summary>
    public double PreviewVolume { get; set; } = 0.25;

    /// <summary>Icons-only sidebar, so pages get the full width.</summary>
    public bool SidebarCollapsed { get; set; }

    /// <summary>UI language code ("fr", "en", "es", "de"); empty = Windows language.</summary>
    public string Language { get; set; } = "";

    /// <summary>Accent colour key (see ThemeService), or "custom" for <see cref="CustomAccent"/>.</summary>
    public string Accent { get; set; } = "rouge";

    /// <summary>Colour picked by the user, "#RRGGBB".</summary>
    public string CustomAccent { get; set; } = "#FF4F7E";

    /// <summary>See ThemeMode.</summary>
    public string Theme { get; set; } = ThemeMode.Night;
}

/// <summary>Loads and saves %LOCALAPPDATA%\Shazim\settings.json.</summary>
public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _settingsPath;
    private readonly object _lock = new();
    private AppSettings _settings = new();

    public SettingsService() : this(Shazim.Core.AppPaths.SettingsFile)
    {
    }

    public SettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
        _settings = Load();
    }

    public string SettingsPath => _settingsPath;

    /// <summary>Raised after <see cref="Save"/>.</summary>
    public event EventHandler? Saved;

    public AppSettings GetSettings() => _settings;

    public void Save()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var tmp = _settingsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_settings, JsonOptions));
            File.Move(tmp, _settingsPath, overwrite: true);
        }
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            bool rewrite = false;
            using (var doc = JsonDocument.Parse(json))
            {
                if (!Has(doc.RootElement, "version")) settings.Version = 1; // files written before versioning
                // Earlier versions kept API keys for other services: drop that section from disk.
                if (Has(doc.RootElement, "recognition")) rewrite = true;
            }

            rewrite |= settings.Version < AppSettings.CurrentVersion;
            Migrate(settings);
            Normalize(settings);
            _settings = settings;
            if (rewrite) Save();
            return settings;
        }
        catch (Exception)
        {
            // A corrupt file must not prevent the app from starting; keep a copy for inspection.
            try { File.Copy(_settingsPath, _settingsPath + ".corrupt", overwrite: true); } catch { }
            return new AppSettings();
        }
    }

    private static bool Has(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object &&
        e.EnumerateObject().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static void Migrate(AppSettings s)
    {
        if (s.Version < 2)
        {
            if (s.Audio.CaptureDurationMs < 12000) s.Audio.CaptureDurationMs = 12000;
            if (s.AutoShazam.CaptureDurationMs < 12000) s.AutoShazam.CaptureDurationMs = 12000;
            // Older builds saved "enabled" permanently; only AutoStart should survive restarts.
            s.AutoShazam.Enabled = false;
        }

        // v4: one device became "source + output + microphone".
        if (!string.IsNullOrWhiteSpace(s.Audio.LegacyDeviceId))
        {
            var id = s.Audio.LegacyDeviceId!;
            if (id.StartsWith("in:", StringComparison.Ordinal))
            {
                s.Audio.Source = AudioSource.Mic;
                s.Audio.MicDeviceId = id;
            }
            else
            {
                s.Audio.Source = AudioSource.Pc;
                s.Audio.OutputDeviceId = id == AudioDevice.DefaultId || id.StartsWith("out:", StringComparison.Ordinal) ? id : "out:" + id;
            }
        }
        s.Audio.LegacyDeviceId = null;
        if (s.General.LegacyReactiveButton == false) s.General.ButtonEffect = ButtonEffect.Off;
        s.General.LegacyReactiveButton = null;

        // v5: notifications got a style; "off" became the "none" style.
        if (s.Version < 5 && s.Notifications is { Enabled: false }) s.Notifications.Style = NotificationStyle.None;
        s.Version = AppSettings.CurrentVersion;
    }

    private static void Normalize(AppSettings s)
    {
        s.Audio ??= new();
        s.AutoShazam ??= new();
        s.Hotkeys ??= new();
        s.Notifications ??= new();
        s.General ??= new();
        s.Updates ??= new();
        s.Widget ??= new();
        s.Lyrics ??= new();
        s.Updates.SkippedVersion ??= "";
        if (s.Notifications.Style is not (NotificationStyle.Popup or NotificationStyle.Windows or NotificationStyle.None)) s.Notifications.Style = NotificationStyle.Popup;
        s.Notifications.Enabled = s.Notifications.Style != NotificationStyle.None;
        if (s.General.Theme is not (ThemeMode.Night or ThemeMode.Midnight or ThemeMode.Light or ThemeMode.System)) s.General.Theme = ThemeMode.Night;
        if (Services.ThemeService.ParseHex(s.General.CustomAccent) == null) s.General.CustomAccent = "#FF4F7E";
        s.Widget.Opacity = Math.Clamp(double.IsFinite(s.Widget.Opacity) ? s.Widget.Opacity : 1, 0.55, 1);
        if (s.Audio.Source is not (AudioSource.Pc or AudioSource.Mic or AudioSource.Both)) s.Audio.Source = AudioSource.Pc;
        if (string.IsNullOrWhiteSpace(s.Audio.OutputDeviceId)) s.Audio.OutputDeviceId = AudioDevice.DefaultId;
        if (string.IsNullOrWhiteSpace(s.Audio.MicDeviceId)) s.Audio.MicDeviceId = AudioDevice.DefaultMicId;
        if (string.IsNullOrWhiteSpace(s.General.Accent)) s.General.Accent = "rouge";
        s.Audio.CaptureDurationMs = Math.Clamp(s.Audio.CaptureDurationMs, 4000, 20000);
        s.AutoShazam.CaptureDurationMs = Math.Clamp(s.AutoShazam.CaptureDurationMs, 4000, 20000);
        s.AutoShazam.IntervalMs = Math.Clamp(s.AutoShazam.IntervalMs, 5000, 3_600_000);
        s.AutoShazam.DuplicateWindowMinutes = Math.Clamp(s.AutoShazam.DuplicateWindowMinutes, 1, 1440);
        s.General.PreviewVolume = Math.Clamp(s.General.PreviewVolume, 0, 1);
        if (s.General.ButtonEffect is not (ButtonEffect.Aura or ButtonEffect.Pulse or ButtonEffect.Off)) s.General.ButtonEffect = ButtonEffect.Aura;
    }
}
