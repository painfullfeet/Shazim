namespace Shazim.Core.Security;

/// <summary>
/// Decides which links may be handed to Windows. Links come from network answers (Shazam,
/// LRCLIB, GitHub), so a malicious answer must never be able to launch a local file, a UNC
/// share or an arbitrary protocol handler (file:, ms-*, search-ms:, javascript:...).
/// </summary>
public static class LinkPolicy
{
    // Web pages, plus the desktop apps of the platforms Shazim links to.
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "https", "http", "spotify", "deezer"
    };

    /// <summary>Returns the normalised URI to open, or null when the link must be refused.</summary>
    public static string? Sanitize(string? link)
    {
        if (string.IsNullOrWhiteSpace(link) || link.Length > 2048) return null;
        link = link.Trim();
        if (link.Any(char.IsControl)) return null;
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)) return null;
        if (!AllowedSchemes.Contains(uri.Scheme)) return null;
        if (uri.IsUnc || uri.IsFile) return null;

        if (uri.Scheme is "https" or "http")
        {
            // A web link needs a real host and no "user:password@" part (used to disguise hosts).
            if (string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        }
        return uri.AbsoluteUri;
    }

    public static bool IsSafe(string? link) => Sanitize(link) != null;
}
