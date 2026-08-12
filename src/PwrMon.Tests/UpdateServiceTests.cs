using System.Security.Cryptography;
using System.Text;
using PwrMon.Services;

namespace PwrMon.Tests;

/// <summary>
/// Covers the verification chain the updater rests on. These are the only tests here that
/// matter in a security sense: everything else in UpdateService moves bytes between a socket
/// and a temp file, and getting that wrong produces a failed download. Getting *these* wrong
/// produces a machine that installs an attacker's binary with administrator rights.
///
/// So each one asserts a refusal as much as an acceptance — a verifier that says yes to a
/// good signature is worth nothing unless it also says no to every near-miss.
/// </summary>
public class UpdateServiceTests
{
    private static (string PublicKey, ECDsa Key) NewKey()
    {
        var key = ECDsa.Create(ECCurve.CreateFromFriendlyName("nistP256"));
        return (Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), key);
    }

    private static string Sign(ECDsa key, byte[] data) =>
        Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256));

    private static readonly byte[] SampleManifest = Encoding.UTF8.GetBytes(
        """{"version":"1.5.0","url":"https://github.com/brettkcherry/PwrMon/releases/download/v1.5.0/PwrMon-Setup.exe","sha256":"abc123"}""");

    // ─────────────────────────── signature verification ───────────────────────────

    [Fact]
    public void GenuineSignature_Verifies()
    {
        var (pub, key) = NewKey();
        using (key)
        {
            Assert.True(UpdateService.VerifyManifest(SampleManifest, Sign(key, SampleManifest), pub));
        }
    }

    [Fact]
    public void TamperedManifest_IsRejected()
    {
        // The realistic attack: a valid signature over the release that WAS published,
        // replayed against a manifest whose download URL or hash has been edited.
        var (pub, key) = NewKey();
        using (key)
        {
            var signature = Sign(key, SampleManifest);
            var tampered = (byte[])SampleManifest.Clone();
            tampered[^2] ^= 0x01; // one bit, inside the sha256 field

            Assert.False(UpdateService.VerifyManifest(tampered, signature, pub));
        }
    }

    [Fact]
    public void SignatureFromAnotherKey_IsRejected()
    {
        // i.e. someone signed a perfectly well-formed manifest — just not with Brett's key.
        var (pub, key) = NewKey();
        var (_, attackerKey) = NewKey();
        using (key)
        using (attackerKey)
        {
            Assert.False(UpdateService.VerifyManifest(SampleManifest, Sign(attackerKey, SampleManifest), pub));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 at all!!")]
    [InlineData("YWJjZA==")] // valid base64, not a signature
    public void MalformedSignature_IsRejectedWithoutThrowing(string signature)
    {
        // A check that throws is a check that can be turned into a crash by anyone who can
        // serve a response, so malformed input has to come back as a plain false.
        var (pub, key) = NewKey();
        using (key)
        {
            Assert.False(UpdateService.VerifyManifest(SampleManifest, signature, pub));
        }
    }

    [Fact]
    public void MalformedPublicKey_IsRejectedWithoutThrowing()
    {
        var (_, key) = NewKey();
        using (key)
        {
            Assert.False(UpdateService.VerifyManifest(SampleManifest, Sign(key, SampleManifest), "garbage"));
        }
    }

    [Theory]
    [InlineData("REPLACE_ME_RUN_tools/new-release-key.ps1")]
    [InlineData("")]
    [InlineData("   ")]
    public void UnconfiguredKey_LeavesUpdaterInert(string key)
    {
        // Before the real key is pasted in, the updater must do nothing at all — not check,
        // not download, not "try anyway".
        Assert.False(UpdateService.IsConfiguredKey(key));
    }

    [Fact]
    public void MistypedKey_ReadsAsUnconfiguredRatherThanHostile()
    {
        // A paste error must not surface as SignatureInvalid. That status means "someone is
        // interfering with your updates", and saying it about a typo in this repository would
        // send the user hunting for an attacker who isn't there.
        Assert.False(UpdateService.IsConfiguredKey("not base64 at all!!"));
        Assert.False(UpdateService.IsConfiguredKey("YWJjZA==")); // valid base64, not a key
    }

    [Fact]
    public void RealKey_ReadsAsConfigured()
    {
        var (pub, key) = NewKey();
        using (key) Assert.True(UpdateService.IsConfiguredKey(pub));
    }

    [Fact]
    public void ThisBuildCarriesARealSigningKey()
    {
        // The public half belongs in git — it ships inside every binary. So this guards the
        // failure that would otherwise be silent: a key reverted to the placeholder disables
        // updates for everyone who installs that build, with nothing anywhere to say so.
        Assert.True(UpdateService.IsConfigured);
    }

    // ─────────────────────────── hash comparison ───────────────────────────

    [Theory]
    [InlineData("ABC123", "abc123")]
    [InlineData("  abc123  ", "abc123")]
    public void HashComparison_IgnoresCaseAndWhitespace(string a, string b) =>
        Assert.True(UpdateService.HashesMatch(a, b));

    [Theory]
    [InlineData("abc123", "abc124")]
    [InlineData("abc123", "")]
    [InlineData("abc123", "abc123a")]
    public void DifferentHashes_DoNotMatch(string a, string b) =>
        Assert.False(UpdateService.HashesMatch(a, b));

    // ─────────────────────────── version comparison ───────────────────────────

    [Fact]
    public void ManifestVersion_AndAssemblyVersion_CompareEqualForSameRelease()
    {
        // Version.Parse("1.4.0") leaves Revision at -1; an assembly version has it at 0, so
        // the raw values for one release do NOT compare equal. Without normalising, PwrMon
        // would offer every user an "update" to the build they are already running.
        Assert.Equal(UpdateService.Normalize(Version.Parse("1.4.0")),
                     UpdateService.Normalize(new Version(1, 4, 0, 0)));
    }

    [Fact]
    public void NewerVersion_IsGreater()
    {
        Assert.True(UpdateService.Normalize(Version.Parse("1.5.0")) >
                    UpdateService.Normalize(new Version(1, 4, 0, 0)));
    }

    [Fact]
    public void OlderVersion_IsNotGreater()
    {
        // A downgrade offered by a stale or rolled-back manifest must not be installed.
        Assert.False(UpdateService.Normalize(Version.Parse("1.3.9")) >
                     UpdateService.Normalize(new Version(1, 4, 0, 0)));
    }

    // ─────────────────────────── download URL policy ───────────────────────────

    [Fact]
    public void RealReleaseAssetUrl_IsAccepted() =>
        Assert.True(UpdateService.IsHttpsGitHubUrl(
            "https://github.com/brettkcherry/PwrMon/releases/download/v1.5.0/PwrMon-Setup.exe"));

    [Theory]
    // plain http — downgrade
    [InlineData("http://github.com/brettkcherry/PwrMon/releases/download/v1.5.0/PwrMon-Setup.exe")]
    // lookalike hosts: the classic way a URL passes a careless eye
    [InlineData("https://github.com.evil.example/brettkcherry/PwrMon/releases/download/v1.5.0/x.exe")]
    [InlineData("https://githubb.com/brettkcherry/PwrMon/releases/download/v1.5.0/x.exe")]
    // right host, wrong repository
    [InlineData("https://github.com/someoneelse/PwrMon/releases/download/v1.5.0/x.exe")]
    // right host and repo, but not a release asset path
    [InlineData("https://github.com/brettkcherry/PwrMon/raw/main/evil.exe")]
    [InlineData("file:///C:/Windows/System32/evil.exe")]
    [InlineData("not a url")]
    public void UnexpectedUrls_AreRejected(string url) =>
        Assert.False(UpdateService.IsHttpsGitHubUrl(url));
}
