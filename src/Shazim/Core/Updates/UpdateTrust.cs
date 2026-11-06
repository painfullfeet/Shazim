using System.Security.Cryptography;

namespace Shazim.Core.Updates;

/// <summary>
/// What an update must prove before it is installed:
/// 1. it comes from this repository's release downloads (https, github.com),
/// 2. SHA256SUMS.txt is signed with the release key (ECDSA P-256), whose private half never
///    leaves the author's PC — so a hijacked GitHub account or release cannot forge a valid update,
/// 3. the installer matches the signed checksum.
/// </summary>
public static class UpdateTrust
{
    public const string AssetPrefix = "https://github.com/painfullfeet/Shazim/releases/download/";
    public const string SignatureFileName = "SHA256SUMS.txt.sig";

    /// <summary>Public release key (SubjectPublicKeyInfo, base64). Private key: tools/ReleaseSigner, kept off the repository.</summary>
    public const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEZzby2kqm19DvCwyJbH7ZVGfC8Zr69g4ONP1IpzYQdFYsYiOucDfvVy4POx30Jn+9Wa8zqSyG/X8JDwZKiTbezw==";

    /// <summary>True for a download link of this repository's releases.</summary>
    public static bool IsTrustedAssetUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
               uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
               // Checked after normalisation, so "../" cannot leave the repository.
               uri.AbsolutePath.StartsWith("/painfullfeet/Shazim/releases/download/", StringComparison.OrdinalIgnoreCase) &&
               !url.Contains("..", StringComparison.Ordinal) && !url.Contains('%');
    }

    /// <summary>Checks the base64 signature of <paramref name="data"/> against <paramref name="publicKey"/> (default: the release key).</summary>
    public static bool VerifySignature(byte[] data, string? signatureText, string publicKey = PublicKey)
    {
        if (string.IsNullOrWhiteSpace(signatureText)) return false;
        try
        {
            var signature = Convert.FromBase64String(signatureText.Trim());
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (FormatException) { return false; }
        catch (CryptographicException) { return false; }
    }
}
