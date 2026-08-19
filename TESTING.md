# Testing

PwrMon has a small, deliberately narrow test bench targeting pure logic/math, not the
hardware-coupled parts of the app.

## Running

```powershell
.\run-tests.ps1
```

or directly:

```powershell
dotnet test PwrMon.sln
```

Both build `src\PwrMon.Tests\PwrMon.Tests.csproj` (xUnit) against a project reference to
`PwrMon.csproj` and run everything. `run-tests.ps1` forwards the `dotnet test` exit code, so it's
safe to use as a CI/pre-commit gate.

## What's covered

- **`Models\PowerSample.cs`** — the computed properties: `PowerSample.NetW`/`CurrentA`,
  `BatteryStaticInfo.WearPct`, `SessionStats.AvgDischargeW`, `PowerEvent.Label`. All branches and
  guard conditions (e.g. `VoltageV <= 1`, `DesignWh == 0`, the tiny-time floor on
  `AvgDischargeW`) are exercised.
- **`Services\PowerMath.cs`** — pure formulas lifted out of `Sampler`'s hot loop (see below).
  Every function, every branch/guard, and the documented boundary values (e.g. `EmaAlpha` at
  `dt == tau`, `IsGap`'s two boundary conditions, the `0.5 W` guards on the time estimates, the
  contradiction thresholds) are tested directly, with no `Sampler` involved.
- **`Services\HistoryStore.cs`** — `FormatLine`/`ParseLine` round-tripping, entirely in memory:
  core fields, optional (nullable) fields, null-optionals round-tripping through empty CSV
  fields, a legacy 11-column line (pre-`CpuPlatformW`) still parsing with that field `null`,
  malformed/short lines returning `null`, and a header line being rejected.
- **`Services\UnitFormatter.cs`** — `Power`, `Energy`, `Duration`, `Percent`, covering unit modes,
  the signed/`+` threshold, the decimal-count threshold, the mAh voltage fallback, and all the
  `Duration` bucket boundaries.
- **`Services\DrawProfile.cs`** — the time-weighted histogram (`Add`/`Percentile`, time-weighting
  vs. sample-counting, saturation at the top bin, nonsense-input rejection), the trust gate and
  its one-time "just learned" transition flag, and decay's halving. Instances are constructed
  fresh in memory; see the safety rule below for why `Save`/`Load` are excluded.
- **`Services\PowerMath.cs`'s heavy-draw helpers** — `CapacityDerivedHeavyDrawW`'s scaling,
  clamping, and its fallback on unreadable capacity; `IsHeavyDraw`'s hysteresis, including a
  walk of a load across the trip point that asserts it flips state at most once per direction.

## What's deliberately NOT covered

This app is heavily hardware-coupled, and testing that coupling would mean mocking away most of
what the tests are supposed to verify. Out of scope for this bench:

- **`Services\BatteryReader.cs`, `Services\HardwareReader.cs`** — WMI, RAPL, and
  LibreHardwareMonitor native reads. These need real (or heavily faked) hardware/drivers to
  exercise meaningfully; a unit test against them would mostly be testing the mocks.
- **The stateful `Sampler` loop, its threading, and `PeriodicTimer` scheduling** — `Sampler.Tick()`
  itself (as opposed to the math it calls out to) mutates a web of instance fields
  (`_emaCharge`, `_capTrend`, `_baselineW`, …) in a specific order, on a background thread, driven
  by real `BatteryReader`/`HardwareReader` instances. That state machine is exactly the kind of
  thing this bench's seam-based approach set out to leave untouched — see "Future test surface"
  below for how it could be tested without a hardware dependency.
- **WPF `Views\*`** — no UI/XAML testing here.
- **`Services\ThemeService.cs`, `Services\TrayService.cs`, `Services\StartupHelper.cs`,
  `Services\Log.cs`, `App.xaml.cs`** — OS/shell/tray integration and logging; not pure logic.
- **Settings and draw-profile persistence (`AppSettings.Load`/`Save`, `DrawProfile.Load`/`Save`)**
  — see the safety rule below; these touch the user's real `%LocalAppData%\PwrMon` files and are
  never exercised by tests.
- **`tools\SensorProbe`** — out of scope per the task brief; it's a standalone diagnostic tool,
  not part of the app under test.

## The `PowerMath` extraction

`Sampler.Tick()` and `Sampler.SanitizeDirection()` contained several small, pure formulas
(EMA smoothing, the sleep-gap test, the time-to-full/empty estimates, the wall-input division,
the capacity-trend slope, and the two firmware-contradiction checks) inlined directly in the hot
loop. `Services\PowerMath.cs` lifts those formulas out, unchanged, into standalone static
functions that take their tunable constants (`tau`, thresholds, the sampling interval, adapter
efficiency) as parameters instead of reading them from instance fields.

`Sampler` now calls `PowerMath` at each of those sites — same operands, same order, same
result — so the formulas are independently testable without touching the surrounding stateful
loop, threading, `_capTrend` queue management, or hardware reads. The refactor is intentionally
minimal: the loop's structure, field mutations, and ordering are untouched. See the project
report / commit history for the exact list of call sites that were replaced (and the two
structurally-different EMA-shaped computations — CPU-package smoothing and the learned baseline
— that were deliberately left inline because they weren't part of the extraction).

## Safety rule: never touch the user's real settings/history

`AppSettings.Current` is a process-wide static. Its instance properties have public setters, but
`AppSettings.Load()`/`AppSettings.Save()` read and write the user's **real** file at
`%LocalAppData%\PwrMon\settings.json` (and history under `AppSettings.Dir\history\`).

**Tests must never call `AppSettings.Load()` or `AppSettings.Save()`, and must never write under
`AppSettings.Dir`.** `UnitFormatterTests` sets `AppSettings.Current.PowerUnit`/`EnergyUnit`
in memory only, explicitly at the start of each test that depends on them (since `Current` is
shared, order-independence isn't otherwise guaranteed). Assembly-wide test parallelization is
disabled (`AssemblyInfo.cs`: `[assembly: CollectionBehavior(DisableTestParallelization = true)]`)
so these tests can't race each other — or anything else — over that shared static.

`HistoryStoreTests` exercises `FormatLine`/`ParseLine` purely in memory (strings in, `PowerSample`
out) and never touches disk or `HistoryStore.HistoryDir`.

**The same rule covers the log, but couldn't be met by discipline alone.** No test logs on
purpose — the services under test (`DrainAlertService`, `StartupHelper`, `UpdateService`,
`DrawProfile`) call `Log` themselves, and `Log` resolves to the user's real
`%LocalAppData%\PwrMon\logs\`. So every run appended synthetic lines like
`drain-on-AC alert: 31.0 W at 12%` to the live diagnostic log. That is worse than untidy: on
2026-08-19 those fake entries were briefly mistaken for the real incident while investigating
an actual false-positive drain alert. `Log.DirectoryOverride` is the seam, and
`AssemblyInfo.cs` sets it to a temp directory from a `[ModuleInitializer]` — early enough to
beat the first test, which an xUnit fixture wouldn't reliably do. `DirectionArbiter` also
deliberately does no logging of its own for the same reason; its caller logs the transitions.

The same rule applies to `DrawProfile.Save()`/`Load()`, which read and write
`%LocalAppData%\PwrMon\drawprofile.json` — a real user's learned battery history, not a
throwaway file. `DrawProfileTests` exercises everything through fresh in-memory instances and
`Add()` calls, never `Save()`/`Load()`. A round-trip test was drafted once during development
and caught before being committed — it would have overwritten a live soak's profile with
synthetic test data the moment `run-tests.ps1` ran. If persistence round-tripping is ever worth
testing directly, it needs an injectable path first (see "Future test surface" below, which
already flags the same gap for `HistoryStore`).

## Future test surface

Ideas for extending coverage later, none of which fit this pass's "pure logic only" scope:

- **The full `Sampler.Tick()` state machine**, by injecting fake `BatteryReading`/
  `HardwareReading` sources (both `BatteryReader`/`HardwareReader` would need an interface or
  delegate seam) and asserting the resulting `PowerSample`/`SessionStats`/`Estimates` sequence
  across multiple ticks — including AC-transition events, sleep-gap handling, and baseline
  learning.
- ~~**`SanitizeDirection`'s `_capTrend` queue behavior** directly — window trimming, flag-change
  resets, and the override-flip logging — once/if it's extracted from `Sampler` into something
  independently constructible.~~ **Done 2026-08-19**: extracted as `DirectionArbiter` and
  covered by `DirectionArbiterTests`, which replays both real incidents this surface has
  produced — the 2026-08-13 sustained drain it must convict, and the 2026-08-19 post-plug-in
  settling artifact it must acquit. `Sampler` keeps the logging and the magnitude handling.
- **`HistoryStore` file I/O** (`Append`'s flush timing, `LoadRecent`, `CleanupOldFiles`,
  `ExportRange`) against a redirectable/injectable directory instead of the hardcoded
  `AppSettings.Dir`-derived path, so it can be tested against a temp directory without touching
  the safety rule above.
- **`DrawProfile.Save`/`Load` round-tripping** — same shape of gap, same fix: an injectable path
  instead of the hardcoded one, so the sparse-JSON round trip (including a corrupt-file fallback
  to an empty profile) can be tested against a temp directory instead of skipped entirely.
