# Contributing

PwrMon is a small, opinionated tool. The fastest way to get a change merged is to match what
it already is rather than to broaden it.

## The ethos, so PRs don't get bounced

**Live watts first.** The question PwrMon answers is "what is this machine pulling right
now?" Battery *health summaries* are explicitly not the point — every other tool does those.

**Zero bloat.** One portable exe, four direct dependencies, no installer required, no
telemetry, no auto-update, no account. A change that adds a dependency needs to justify it.

**Honest numbers.** Where a value is measured, say so; where it's estimated, say that too.
PwrMon exists partly because firmware lies — on this project's reference machine the battery
reported `Charging=True` at 86 W while actually draining to flat. Measured capacity trend is
what settled it. Don't paper over contradictions; surface them.

## Reporting bugs

Check [ISSUES.md](ISSUES.md) first — the known punch list is there and doesn't need
duplicating. For anything sensor- or hardware-related, run the probe and attach the output:

```powershell
dotnet run --project tools/SensorProbe
```

Include your CPU/GPU, Windows version, whether Memory Integrity is on, whether you were
elevated, and the sensor tier shown in the status bar.

**Security problems go to [SECURITY.md](SECURITY.md), not the issue tracker.**

## Hardware coverage is the most useful contribution

PwrMon is verified on exactly one machine (Zenbook UX3404VA, i7-13700H, Iris Xe). AMD
hardware, discrete GPUs, and non-Intel iGPUs are all untested. A `SensorProbe` dump from
unfamiliar hardware — even with no code attached — is genuinely valuable.

## Development

```powershell
dotnet build PwrMon.sln
dotnet run --project src/PwrMon
./run-tests.ps1
```

Two things that will bite you:

- The running app locks its own build output. Stop PwrMon before rebuilding, or use
  `./run-tests.ps1 -Configuration Release` when a Debug instance is running.
- Full CPU/iGPU sensors need admin plus PawnIO on Memory-Integrity systems. Without them
  you'll land in the EMI tier, which is a legitimate configuration to develop against —
  most users are in it.

## Code

Match the surrounding style; there's no formatter to run. Existing code is nullable-enabled,
uses modern C# freely, and comments explain *why* rather than *what* — keep that.

Tests cover pure logic only (`PowerMath`, `HistoryStore` round-tripping, `UnitFormatter`,
the model computed properties, and the startup ACL predicate). Hardware-coupled code isn't
unit tested; verify those changes on real hardware and say so in the PR.

If you touch anything security-relevant — elevation, the PawnIO path, file parsing, the
single-instance signals — call it out explicitly in the PR description.

Changing dependencies means re-running the notices check and updating
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md):

```powershell
powershell -ExecutionPolicy Bypass -File tools/list-shipped-assemblies.ps1
```

## Pull requests

One change per PR. Say what hardware you tested on. If it's a UI change, a screenshot in the
PR (not committed to the repo) helps.
