# Security policy

PwrMon reads hardware sensors, can create an elevated scheduled task, and can download a
kernel-mode driver installer. That's a bigger surface than a typical desktop utility, so
this document states plainly what the trust boundaries are and how to report a problem.

## Reporting a vulnerability

**Please don't open a public issue for a security problem.**

Use GitHub's private vulnerability reporting:
[**Report a vulnerability**](https://github.com/brettkcherry/PwrMon/security/advisories/new).
It's private between you and the maintainer until a fix ships.

What helps:

- The PwrMon version (Settings → About, or the exe's file properties)
- Windows version, and whether Memory Integrity (HVCI) is on
- Which sensor tier you were in — the status bar shows it
- Steps to reproduce, and what you expected instead
- Whether you were running elevated

This is a personal project, not a funded one. Expect an acknowledgement within about a week.
There's no bug bounty, but you'll be credited in the advisory and CHANGELOG unless you'd
rather not be.

## Supported versions

Only the latest release gets fixes. There are no maintained branches.

## What's in scope

- Privilege escalation: anything that gets code running with more rights than the user gave it
- The PawnIO download and verification path
- The autostart mechanisms (HKCU Run key, elevated scheduled task)
- The single-instance signalling (mutex and named events)
- Parsing of files under `%LocalAppData%\PwrMon\` — settings, history CSVs
- Anything causing PwrMon to make a network request other than the consented PawnIO download

## What's out of scope

- **PawnIO itself.** It's a separate project — report to
  [namazso/PawnIO.Setup](https://github.com/namazso/PawnIO.Setup). PwrMon only downloads,
  verifies and launches its official installer.
- **LibreHardwareMonitor and other dependencies.** Report upstream; tell us too so we can
  bump the pin.
- **SmartScreen warnings on unsigned builds.** Known and expected until code signing is in
  place. See "Verifying a download" below.
- Anything requiring administrator rights to set up in the first place — an attacker who is
  already admin doesn't need PwrMon.
- Physical access attacks.

## Trust boundaries, stated

**Sensor tiers.** The default tier uses Windows' Energy Meter performance counters, opened
read-only. No driver, no elevation. The optional tier uses LibreHardwareMonitor with PawnIO;
PwrMon opens LHM with only CPU and GPU enabled, so the SMBus, EC and LPC PawnIO modules are
never loaded.

**No WinRing0.** PwrMon does not ship or load WinRing0, the MSR driver on Microsoft's
vulnerable-driver blocklist that most tools in this category rely on.

**The PawnIO download.** Downloaded only on explicit request, into a freshly created
randomly named temp directory, then verified with `WinVerifyTrust` before anything executes.
The verified signer is shown to you and you confirm it before the installer runs. If
verification fails, PwrMon refuses to run the file and sends you to the PawnIO website.

**Elevated autostart.** The scheduled task runs PwrMon with administrator rights at logon
without a UAC prompt, so the executable it points at must not be replaceable by a
non-administrator. PwrMon checks the ACL of the exe and its directory and refuses to create
the task from a user-writable location — a portable copy in Downloads or `%LocalAppData%`
won't get elevated autostart. Install it, or move it under Program Files. Normal
(non-elevated) autostart works from anywhere.

**Single-instance signalling.** The named events carry an explicit DACL granting only the
current user. Note that a process already running as you can terminate PwrMon regardless —
that isn't a boundary this can enforce.

**Data.** Everything stays under `%LocalAppData%\PwrMon\`. No identifiers are collected, and
nothing is transmitted anywhere, ever.

## Verifying a download

Releases are currently **unsigned**, so Windows SmartScreen will warn. Until signing is in
place, verify with the SHA-256 checksums published on the release page:

```powershell
Get-FileHash .\PwrMon.exe -Algorithm SHA256
```

Only download from the [GitHub releases page](https://github.com/brettkcherry/PwrMon/releases).
PwrMon has no auto-update mechanism and will never ask to install anything other than PawnIO,
and only when you click the button.
