using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PwrMon.Services;

/// <summary>
/// Checks for, verifies, and launches PwrMon updates.
///
/// <para><b>Why this exists at all.</b> Until now PwrMon had no update path: a fix could be
/// released but never reached anyone who had already installed it. For the one app here that
/// runs elevated and talks to a kernel driver, "we can ship a patch but not deliver it" is
/// the wrong place to be.</para>
///
/// <para><b>What makes it safe.</b> Not HTTPS — TLS proves you reached github.com, not that
/// what you got is something Brett built. The trust root is an ECDSA P-256 public key
/// compiled into this binary (<see cref="PublicKeyBase64"/>). The release manifest is signed
/// with the matching private key, which lives offline and never enters this repository. The
/// chain runs:
/// <list type="number">
///   <item>fetch <c>latest.json</c> and its detached signature;</item>
///   <item>verify the signature over the manifest's exact bytes — anything else stops here;</item>
///   <item>read the installer's expected SHA-256 <i>out of the now-trusted manifest</i>;</item>
///   <item>download the installer and require its hash to match.</item>
/// </list>
/// Signing the manifest rather than the installer is what makes one signature enough: the
/// manifest carries the installer's hash, so authenticating the manifest transitively
/// authenticates the bytes it names.</para>
///
/// <para><b>What this deliberately is not.</b> It never installs anything on its own. Every
/// path ends at a prompt, because the last step hands a binary an elevation prompt — the same
/// line the PawnIO installer flow draws in MainWindow.xaml.cs, for the same reason.</para>
///
/// <para><b>When PwrMon gets a code-signing certificate</b>, add
/// <see cref="Authenticode.TryVerify"/> as a second gate on the downloaded installer. It is
/// not used today because PwrMon's own installer is unsigned, so the check could only ever
/// fail. The signature chain above is what stands in for it meanwhile.</para>
/// </summary>
public static class UpdateService
{
    /// <summary>
    /// ECDSA P-256 public key (SubjectPublicKeyInfo, base64) that release manifests must
    /// verify against. Generate the pair with tools/new-release-key.ps1 and paste the public
    /// half here; the private half never belongs in this repository.
    ///
    /// While this holds the placeholder, <see cref="IsConfigured"/> is false and no update
    /// check runs at all — not even a network request. An unconfigured updater does nothing,
    /// rather than doing something unverified.
    ///
    /// Rotated 2026-08-15. The original key (used to sign v1.6.0 and v1.6.1) could not be
    /// found after an uninstall/cleanup pass on the machine it lived on and is presumed lost
    /// — see docs/RELEASING.md's rotation note. This orphans those two manifests: a copy of
    /// PwrMon built with this key will correctly refuse them, same as it would refuse a
    /// tampered one. Low-stakes here since this repo has no outside installs yet.
    /// </summary>
    private const string PublicKeyBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE5RtAByi8zYVSrfrM/WCPKxOciRDpqRmS60hOIhRwJ46UyvLFIju5PFkXJB8LTETBWHFJ4IUmDQmw43KZyEoWfQ==";

    private const string ManifestUrl =
        "https://github.com/brettkcherry/PwrMon/releases/latest/download/latest.json";
    private const string SignatureUrl = ManifestUrl + ".sig";

    /// <summary>An installer smaller than this is a 404 page or a truncated download.</summary>
    private const int MinInstallerBytes = 1_000_000;

    /// <summary>Cap on what we'll pull down, so a bad manifest can't fill the disk.</summary>
    private const long MaxInstallerBytes = 400L * 1024 * 1024;

    public static bool IsConfigured => IsConfiguredKey(PublicKeyBase64);

    /// <summary>
    /// Is this a usable signing key, rather than the placeholder shipped before
    /// <c>tools/new-release-key.ps1</c> has been run?
    ///
    /// Split out from <see cref="IsConfigured"/> so the rule can be tested against both a
    /// placeholder and a real key, rather than only against whatever this particular build
    /// happens to carry — a test that asserts the shipped constant is a placeholder starts
    /// failing the day the feature begins working, which is exactly backwards.
    ///
    /// It also parses the key rather than only pattern-matching it, because a mistyped paste
    /// would otherwise pass here, let the check run, and surface as
    /// <see cref="UpdateStatus.SignatureInvalid"/> — which tells the user something is
    /// interfering with their updates, about what is really a typo in this repository.
    /// </summary>
    internal static bool IsConfiguredKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith("REPLACE_ME", StringComparison.Ordinal))
            return false;
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key.Trim()), out _);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The running build, normalised to major.minor.build.</summary>
    public static Version CurrentVersion => Normalize(
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public sealed record Manifest
    {
        [JsonPropertyName("version")] public string Version { get; init; } = "";
        [JsonPropertyName("url")] public string Url { get; init; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; init; } = "";
        [JsonPropertyName("notes")] public string? Notes { get; init; }
    }

    /// <summary>Outcome of a check. <paramref name="Manifest"/> is set only when Available.</summary>
    public sealed record CheckResult(UpdateStatus Status, Manifest? Manifest = null, string? Detail = null);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        // GitHub rejects requests with no User-Agent on some endpoints; send a real one
        // rather than discovering that intermittently.
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"PwrMon/{CurrentVersion}");
        return c;
    }

    /// <summary>
    /// Fetch and verify the release manifest. Never throws — every failure comes back as a
    /// status, because an update check failing is not a reason for the app to misbehave.
    /// </summary>
    public static async Task<CheckResult> CheckAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new CheckResult(UpdateStatus.NotConfigured);

        try
        {
            var manifestBytes = await Http.GetByteArrayAsync(ManifestUrl, ct);
            var signatureText = await Http.GetStringAsync(SignatureUrl, ct);

            if (!VerifyManifest(manifestBytes, signatureText, PublicKeyBase64))
            {
                // Loud on purpose. A manifest that fails verification is not "no update
                // today" — either the release was mis-signed or something is interfering.
                Log.Error("update: manifest signature did not verify — ignoring it");
                return new CheckResult(UpdateStatus.SignatureInvalid);
            }

            var manifest = JsonSerializer.Deserialize<Manifest>(manifestBytes);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version)
                                 || string.IsNullOrWhiteSpace(manifest.Url)
                                 || string.IsNullOrWhiteSpace(manifest.Sha256))
                return new CheckResult(UpdateStatus.Failed, Detail: "manifest is incomplete");

            if (!IsHttpsGitHubUrl(manifest.Url))
            {
                // The manifest is signed, so this should be unreachable — but a signing key
                // is exactly the thing you don't want to be the *only* control on where the
                // app will fetch an executable from.
                Log.Error($"update: manifest url rejected: {manifest.Url}");
                return new CheckResult(UpdateStatus.Failed, Detail: "manifest url is not a PwrMon release asset");
            }

            if (!Version.TryParse(manifest.Version, out var offered))
                return new CheckResult(UpdateStatus.Failed, Detail: $"unreadable version '{manifest.Version}'");

            return Normalize(offered) > CurrentVersion
                ? new CheckResult(UpdateStatus.Available, manifest)
                : new CheckResult(UpdateStatus.UpToDate);
        }
        catch (OperationCanceledException)
        {
            return new CheckResult(UpdateStatus.Failed, Detail: "cancelled");
        }
        catch (Exception ex)
        {
            // Offline, DNS down, no release published yet: all ordinary, none worth a dialog.
            Log.Info($"update check unavailable: {ex.GetType().Name}: {ex.Message}");
            return new CheckResult(UpdateStatus.Failed, Detail: ex.Message);
        }
    }

    /// <summary>
    /// Download the installer named by an already-verified manifest, check its hash, and
    /// return the path it was staged at. Returns null on any failure (logged).
    /// </summary>
    public static async Task<string?> DownloadAsync(Manifest manifest, CancellationToken ct = default)
    {
        // A fresh GUID directory per attempt. A predictable staging path is pre-creatable by
        // another process running as this user, which would let it swap the installer between
        // the hash check and the launch — and the launch is the step that raises UAC, so that
        // swap would be an elevation. Same reasoning as the PawnIO flow.
        var stage = Path.Combine(Path.GetTempPath(), "PwrMon-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stage);
            var dest = Path.Combine(stage, "PwrMon-Setup.exe");

            using (var response = await Http.GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > MaxInstallerBytes)
                    throw new InvalidOperationException("installer is implausibly large");

                await using var src = await response.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(dest);
                await src.CopyToAsync(dst, ct);
            }

            var size = new FileInfo(dest).Length;
            if (size < MinInstallerBytes)
                throw new InvalidOperationException($"download too small ({size} bytes)");

            var actual = Sha256Hex(await File.ReadAllBytesAsync(dest, ct));
            if (!HashesMatch(actual, manifest.Sha256))
            {
                Log.Error($"update: hash mismatch — manifest said {manifest.Sha256}, file is {actual}");
                TryDelete(stage);
                return null;
            }

            return dest;
        }
        catch (Exception ex)
        {
            Log.Error("update download", ex);
            TryDelete(stage);
            return null;
        }
    }

    // ─────────────────────────── verification primitives ───────────────────────────
    // Split out and internal so they can be tested against known-good and known-tampered
    // inputs without touching the network. They are the entire security argument; testing
    // the parts that only move bytes around would be the wrong half to cover.

    /// <summary>
    /// True when <paramref name="signatureBase64"/> is a valid ECDSA-SHA256 signature over
    /// <paramref name="manifestBytes"/> for the given SubjectPublicKeyInfo key.
    /// Any malformed input is a failure, never an exception escaping to the caller.
    /// </summary>
    internal static bool VerifyManifest(byte[] manifestBytes, string signatureBase64, string publicKeyBase64)
    {
        try
        {
            var sig = Convert.FromBase64String(signatureBase64.Trim());
            var spki = Convert.FromBase64String(publicKeyBase64.Trim());

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
            return ecdsa.VerifyData(manifestBytes, sig, HashAlgorithmName.SHA256);
        }
        catch (Exception ex)
        {
            Log.Error("update: signature verification threw", ex);
            return false;
        }
    }

    internal static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Case- and whitespace-insensitive hash comparison, in constant time.</summary>
    internal static bool HashesMatch(string a, string b)
    {
        var x = System.Text.Encoding.ASCII.GetBytes(a.Trim().ToLowerInvariant());
        var y = System.Text.Encoding.ASCII.GetBytes(b.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(x, y);
    }

    /// <summary>
    /// The manifest is signed, so this is belt-and-braces — but it bounds what a compromised
    /// signing key could point the app at, which a signature by definition cannot.
    /// </summary>
    internal static bool IsHttpsGitHubUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps
        && (u.Host == "github.com" || u.Host == "objects.githubusercontent.com")
        && u.AbsolutePath.StartsWith("/brettkcherry/PwrMon/releases/download/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Compare on major.minor.build only. <c>Version.Parse("1.5.0")</c> leaves Revision at -1
    /// while an assembly version has it at 0, so the raw values of the same release do not
    /// compare equal — normalising both is what stops "1.4.0" being seen as newer than itself.
    /// </summary>
    internal static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* a temp directory we couldn't clean is not worth failing over */ }
    }
}

public enum UpdateStatus
{
    /// <summary>No public key compiled in — the updater is inert. See UpdateService.PublicKeyBase64.</summary>
    NotConfigured,
    UpToDate,
    Available,
    /// <summary>Reachable but the manifest did not verify. Treated as hostile, not as "no update".</summary>
    SignatureInvalid,
    /// <summary>Offline, no release yet, malformed manifest — ordinary, non-alarming failures.</summary>
    Failed,
}
