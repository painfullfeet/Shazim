using System.Security.Cryptography;
using System.Text;
using Shazim.Core.Updates;
using Xunit;

namespace Shazim.Tests;

public class UpdateTrustTests
{
    private static (string PublicKey, Func<byte[], string> Sign) NewKey()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()),
                data => Convert.ToBase64String(ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
    }

    [Fact]
    public void ValidSignature_IsAccepted()
    {
        var (pub, sign) = NewKey();
        var data = Encoding.UTF8.GetBytes("abc  Shazim-Setup-2.2.0.exe\n");
        Assert.True(UpdateTrust.VerifySignature(data, sign(data), pub));
    }

    [Fact]
    public void TamperedChecksums_AreRejected()
    {
        var (pub, sign) = NewKey();
        var data = Encoding.UTF8.GetBytes("abc  Shazim-Setup-2.2.0.exe\n");
        var sig = sign(data);
        data[0] ^= 1;
        Assert.False(UpdateTrust.VerifySignature(data, sig, pub));
    }

    [Fact]
    public void SignatureFromAnotherKey_IsRejected()
    {
        var (_, signWithAttackerKey) = NewKey();
        var data = Encoding.UTF8.GetBytes("abc  Shazim-Setup-2.2.0.exe\n");
        Assert.False(UpdateTrust.VerifySignature(data, signWithAttackerKey(data))); // checked against the real release key
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    public void MissingOrGarbageSignature_IsRejected(string? sig) =>
        Assert.False(UpdateTrust.VerifySignature(new byte[] { 1, 2, 3 }, sig));

    [Theory]
    [InlineData("https://github.com/painfullfeet/Shazim/releases/download/v2.2.0/Shazim-Setup-2.2.0.exe", true)]
    [InlineData("https://github.com/attacker/Shazim/releases/download/v9/Shazim-Setup-9.0.0.exe", false)]
    [InlineData("https://github.com.evil.example/painfullfeet/Shazim/releases/download/x.exe", false)]
    [InlineData("http://github.com/painfullfeet/Shazim/releases/download/v2.2.0/x.exe", false)]
    [InlineData("https://github.com/painfullfeet/Shazim/releases/download/../../../attacker/x.exe", false)]
    public void OnlyThisRepositoryDownloads_AreTrusted(string url, bool trusted) =>
        Assert.Equal(trusted, UpdateTrust.IsTrustedAssetUrl(url));

    [Fact]
    public void EmbeddedReleaseKey_IsAValidP256Key()
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(UpdateTrust.PublicKey), out _);
        Assert.Equal(256, ecdsa.KeySize);
    }
}
