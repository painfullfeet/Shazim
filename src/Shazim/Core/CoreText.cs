namespace Shazim.Core;

/// <summary>
/// User-facing texts produced by Core (errors, device names). The app plugs its
/// translations in through <see cref="Resolver"/>; French is the built-in fallback.
/// </summary>
public static class CoreText
{
    /// <summary>Returns the translation of a key, or null to use the French fallback.</summary>
    public static Func<string, string?>? Resolver { get; set; }

    public static string L(string key, string french) => Resolver?.Invoke(key) ?? french;

    public static string F(string key, string french, params object?[] args) => string.Format(L(key, french), args);
}
