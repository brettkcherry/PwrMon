# Releasing PwrMon

PwrMon can now update itself. This is how a release gets built, signed, and delivered — and
why each step is shaped the way it is.

## Once: create the release signing key

The updater refuses any manifest that does not verify against a public key compiled into the
binary. That key pair does not exist yet, so `UpdateService.PublicKeyBase64` holds a
placeholder and the updater is **completely inert** — it does not even make a network request.
That is deliberate: an unconfigured updater does nothing rather than something unverified.

```bash
./tools/new-release-key.ps1
```

Paste the printed public key into `src/PwrMon/Services/UpdateService.cs`, replacing the
placeholder, and rebuild. The private half is written to `~/.pwrmon/release-key.txt` and must
never enter this repository.

### Why this key matters more than the GitHub account

PwrMon's installer runs with administrator rights. Anyone holding this private key can hand
every PwrMon install an elevated binary of their choosing. HTTPS proves bytes came from
github.com; only this signature proves they came from you. Losing the GitHub account alone is
survivable — an attacker could publish a release, but every installed copy would refuse it.
Losing the account *and* this key is not survivable.

So: one working copy, one offline backup, nothing else. Not a cloud drive, not a note, not a
second laptop. A key with five copies has five ways to leak.

The backup is not optional, because the opposite failure is just as real: lose this key with
no copy and you can never update an installed copy again — only ask people to reinstall by
hand, which most will never see.

**Rotation.** Generating a new key does not reset anything; it orphans every existing install,
since they all carry the old public key. To rotate, ship an update signed with the **current**
key whose binary contains the **new** public key, wait for adoption, and only then retire the
old one. `tools/new-release-key.ps1` refuses to overwrite an existing key for this reason.

## Cutting a release

1. Bump `<Version>` in `src/PwrMon/PwrMon.csproj` and update `CHANGELOG.md`.

2. Build both flavours:

```bash
./tools/publish.ps1
```

3. Build the installer from `installer/PwrMon.iss` (Inno Setup), producing
   `installer/Output/PwrMon-Setup.exe`.

4. Sign the release:

```bash
./tools/sign-release.ps1 -Version 1.5.0
```

That writes `latest.json` (version, download URL, installer SHA-256) and `latest.json.sig`
(detached ECDSA-SHA256 signature over the manifest's exact bytes), and verifies its own output
before finishing.

5. Create the GitHub release tagged `v1.5.0` and upload **all three** assets:
   `PwrMon-Setup.exe`, `latest.json`, `latest.json.sig`.

6. Publish it.

Publishing is what moves `releases/latest`, which is the endpoint installed copies read.
A draft is invisible to them, so a half-finished release cannot escape.

> The signature covers the exact bytes of `latest.json`. Do not reformat, prettify, or
> re-save it after signing — an editor adding a trailing newline is enough to invalidate the
> signature, and every client will correctly refuse the update.

## What an installed copy does

`UpdateService` runs this chain, in this order:

1. Fetch `latest.json` and `latest.json.sig`.
2. Verify the signature over the manifest's exact bytes. Anything else stops here, loudly —
   a manifest that fails verification is treated as hostile, not as "no update today".
3. Read the installer's expected SHA-256 out of the now-trusted manifest.
4. Check the URL is a PwrMon release asset on GitHub over HTTPS. The manifest is signed, so
   this should be unreachable — but a signing key should not be the *only* control on where
   the app will fetch an executable from.
5. Download to a fresh GUID-named temp directory and require the hash to match. A predictable
   staging path is pre-creatable by another process running as this user, which would let it
   swap the installer between the hash check and the launch — and the launch is the step that
   raises UAC, so that swap would be an elevation.
6. Ask the user, then run the installer and exit so it can replace the running exe.

Nothing installs automatically. The last step hands a binary an elevation prompt, which is
the same line the PawnIO installer flow draws, for the same reason.

## Still open

- **No code signing.** PwrMon's installer is unsigned, so users get a SmartScreen warning and
  `Authenticode.TryVerify` cannot be used on our own installer — the signature chain above
  stands in for it. When a certificate exists, add Authenticode as a second gate in
  `UpdateService.DownloadAsync`; the hook is noted in that file's summary.
- **No startup check.** The updater is reached from Settings → UPDATES. An automatic check at
  launch needs somewhere to surface the result, and the main window's banner is currently
  owned by sensor-tier state — deciding which of the two wins is a product call, not a
  mechanical one.
- **No release workflow.** Releases are built locally. If that ever moves to CI, the signing
  key becomes a repository secret and `.github/workflows/` must pin every action to a full
  commit SHA before it does.
