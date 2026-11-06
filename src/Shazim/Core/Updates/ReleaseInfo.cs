using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shazim.Core.Updates;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

/// <summary>A published GitHub release of Shazim.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string Notes, string PageUrl, IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>The single-file installer, "Shazim-Setup-x.y.z.exe".</summary>
    public ReleaseAsset? SetupAsset => Assets.FirstOrDefault(a =>
        a.Name.StartsWith("Shazim-Setup", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>"SHA256SUMS.txt", the checksums of the files of the release.</summary>
    public ReleaseAsset? ChecksumAsset => Assets.FirstOrDefault(a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));

    public string DisplayVersion => $"{Version.Major}.{Version.Minor}.{Math.Max(0, Version.Build)}";
}

/// <summary>Reading of GitHub's "latest release" answer, versions and checksum files.</summary>
public static class UpdateParser
{
    private static readonly Regex VersionPattern = new(@"^\s*v?(\d+)(?:\.(\d+))?(?:\.(\d+))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"v2.1.0", "2.1", "V3" → Version (3 parts). Null when it is not a version.</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var m = VersionPattern.Match(tag);
        if (!m.Success) return null;
        int Part(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value) : 0;
        try { return new Version(Part(1), Part(2), Part(3)); }
        catch (OverflowException) { return null; }
    }

    /// <summary>True when <paramref name="remote"/> is a later version than <paramref name="current"/> (revision ignored).</summary>
    public static bool IsNewer(Version remote, Version current) => Normalize(remote) > Normalize(current);

    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>Parses the JSON of /releases/latest. Drafts, pre-releases and unknown tags give null.</summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Bool(root, "draft") || Bool(root, "prerelease")) return null;

            var tag = Str(root, "tag_name");
            var version = ParseVersion(tag);
            if (version == null) return null;

            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in list.EnumerateArray())
                {
                    var name = Str(a, "name");
                    var url = Str(a, "browser_download_url");
                    if (name.Length == 0 || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
                    long size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                    assets.Add(new ReleaseAsset(name, url, size));
                }
            }
            return new ReleaseInfo(version, tag, Str(root, "body"), Str(root, "html_url"), assets);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the SHA-256 of <paramref name="fileName"/> in a "sha256sum" style file
    /// ("&lt;64 hex&gt;  name" or "&lt;64 hex&gt; *name"). Lower-case hex, or null.
    /// </summary>
    public static string? FindChecksum(string sums, string fileName)
    {
        foreach (var raw in sums.Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            var m = Regex.Match(line, @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
            if (m.Success && string.Equals(m.Groups[2].Value.Trim(), fileName, StringComparison.OrdinalIgnoreCase))
                return m.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>A few readable lines from the Markdown release notes (first language section only).</summary>
    public static string Summary(string? notes, int maxLines = 6)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "";
        var lines = new List<string>();
        bool started = false;
        foreach (var raw in notes.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("---") || line.StartsWith("<details", StringComparison.OrdinalIgnoreCase)) { if (started) break; continue; }
            if (line.StartsWith('#'))
            {
                // A second heading in another language ends the summary.
                if (started && lines.Count > 0 && Regex.IsMatch(line, @"english|español|deutsch|sha-?256", RegexOptions.IgnoreCase)) break;
                continue;
            }
            if (line.Length == 0 || line.StartsWith("|") || line.StartsWith("```")) continue;
            line = Regex.Replace(line, @"^[-*+]\s+", "• ");
            line = Regex.Replace(line, @"\*\*|__|`", "");
            line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]+\)", "$1");
            lines.Add(line);
            started = true;
            if (lines.Count >= maxLines) break;
        }
        return string.Join("\n", lines);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Lower-case hex SHA-256 of a file.</summary>
    public static async Task<string> HashFileAsync(string path, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
