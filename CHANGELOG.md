# Changelog

Notable changes to PwrMon. Format loosely follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added

- **New theme: "Redshift"** — a night-watch red palette, after the red lighting an
  observatory dome or a submarine control room switches to before dark work, because
  long-wavelength light spares the eye's dark adaptation while blue-white light destroys it.
  Red-cast black background, warm rose text instead of white, everything on the red→amber arc.
  Deliberately not fully monochrome: Green/Orange/Red/Blue carry battery *state*, so they stay
  separable within the warm band (amber good, burnt orange draining, hot pink-red alarm, quiet
  rose idle), with one nebula violet for the iGPU series. Every color clears 5.3:1 against the
  background and card.
- **Theme list reordered darkest→lightest.** The four light themes (Paper, Chalk, Frost,
  Meadow) now sit at the end instead of in the middle. People cycle the picker top-to-bottom
  with a dark theme applied and their eyes adapted to it, so a white background part-way down
  the list was a flashbang. The order is presentation only — the setting persists by name, not
  index, so no saved theme was remapped.
- **Mini-graph is now resizable**, down to a 150×90 floor, via a corner drag grip — the
  window is borderless and transparent, so there's no OS chrome to hit-test for standard
  edge-drag resize. Size persists like position.
- **Precision-honesty staleness indicator** (MULTIMETER-STUDY.md §7.1). The fuel gauge
  republishes on its own quantized ~15–30 s cadence regardless of how often PwrMon polls it;
  the hero readout and Power Flow card used to print two decimal places off that reading
  every tick regardless of whether the gauge had actually said anything new. Now: once a raw
  reading has held its exact value past 20 s, the readout drops to whole watts and the hero
  subtext says how long it's been held, instead of implying a tenths-place-fresh number.
  CPU/iGPU watts (RAPL, refreshes every tick), the 30 s-smoothed estimates, and the Power
  Budget card's already-labelled `≈` estimates are unaffected — this only applies to the raw,
  single-sample battery flow numbers that were the actual complaint.
- **Temperature readings.** A THERMAL card showing drive temperature, CPU package, hottest
  core and throttle headroom (degrees remaining before the hottest core throttles), plus
  `CPU °C` and `Drive °C` chart series and two new history columns (`cpu_temp_c`,
  `drive_temp_c`). Columns are append-only, so existing history keeps loading.
  - **Drive temperature works in the default tier** — no administrator and no driver. It's
    read with `IOCTL_STORAGE_QUERY_PROPERTY` on a query-only volume handle, on a 10-second
    cadence of its own rather than the sampler's, since the drive moves slowly and each read
    is a device round-trip. LibreHardwareMonitor's storage sensors were not used: they open
    `\\.\PhysicalDriveN`, which needs administrator, and hung indefinitely when tested
    elevated on the reference machine.
  - CPU-side temperatures ride the same driver as CPU watts, so they need admin + PawnIO and
    show a lock glyph otherwise.
  - **iGPU temperature is not available** on Intel integrated graphics through any route that
    doesn't load a kernel driver. See "Temperature coverage" in the README for the four
    approaches tested and why each fails.
- **"Restart as admin" now offered from the EMI tier, not just NeedsAdmin.** Watts already
  work without elevation there, so the banner reframes it as an upgrade ("add CPU temperature
  and throttle headroom too") rather than a warning. This is what makes the THERMAL card's
  🔒 rows actionable instead of a dead end — and what first exercised the mutex bug below.
- `tools/SensorProbe` now reports ACPI thermal zones, drive temperatures, and the Intel GPU
  routes above, times each LibreHardwareMonitor hardware update, and runs LHM's storage
  detection behind a 20-second watchdog so a hang is reported instead of producing no dump.
- `SECURITY.md` with a private reporting channel and the trust boundaries stated explicitly.
- `CONTRIBUTING.md` and this changelog.
- `ISSUES.md` — the open punch list, moved out of the README.
- `tools/list-shipped-assemblies.ps1` — reconciles what actually ships against
  `THIRD-PARTY-NOTICES.md`, flagging anything that isn't plain MIT.
- Unit tests for the startup ACL predicate.
- `run-tests.ps1 -Configuration Release`, so tests can run while a Debug build is locked by
  a running instance.

### Fixed

- **Implausible RAPL power spikes could plot as real readings.** CPU package/cores/platform
  and iGPU watts have no hardware-level sanity bound of their own, and a driver misread of a
  stale or wrapped energy counter — observed around AC plug/unplug — could report a wattage
  no laptop chip can physically draw. Readings above 500 W are now dropped as a miss instead
  of plotted, mirroring the cap the EMI fallback path already applied to itself.
- **Stat card labels could overflow onto a second line** when a fixed-width card, a font
  change, or a precision change widened the value column — "Throttle headroom" on the new
  THERMAL card was the trigger. `StatLabel` now wraps, and the PROCESSOR and SESSION cards
  are slightly wider to fit their longest labels without wrapping in the first place.
- **The Interface font dropdown was already open every time Settings opened.** The font
  pickers auto-open their dropdown when you type, and that handler was guarded only by the
  `_initializing` flag the constructor clears on its last line. But an editable `ComboBox`
  doesn't create its `PART_EditableTextBox` until the template is applied at first layout —
  after the constructor has finished. WPF then syncs that fresh TextBox to the `Text` the
  constructor set and raises `TextChanged`, with `_initializing` already `false`, which fell
  through to the force-open. Both font pickers were opened this way; only the second stayed
  open, because opening a ComboBox popup takes mouse capture and closes the first — which is
  why the symptom looked specific to Interface font. Now gated on `IsKeyboardFocusWithin`:
  a programmatic Text sync never has keyboard focus, real typing always does. That also stops
  a programmatic set from clobbering the filter `SetFontComboSelection` installs just before
  it, which would have collapsed the curated list on Revert.
- **The elevated "Restart as admin" handoff could crash the new instance silently.** The old
  instance released its single-instance mutex by exiting rather than calling `ReleaseMutex`,
  so Windows marked it abandoned; `.WaitOne` on an abandoned mutex throws
  `AbandonedMutexException`, and that happened before `OnStartup` had registered any exception
  handler — so the new elevated process died instantly with no window and nothing logged. It
  looked like clicking the button just closed the app. Predates the temperature work; it sat
  under the original NeedsAdmin-tier button and just never got exercised until the EMI-tier
  banner above sent more users through it. Fixed on both sides: the old instance now releases
  the mutex explicitly before shutting down, and the new instance treats an abandoned wait as
  success rather than failure, so even a hard crash of the old instance can't wedge a future
  elevation. Verified end to end: log shows a clean
  `starting → shutting down → starting(--replace) → elevated=True` sequence with no crash
  event in between.
- **Self-contained single-file publishes didn't launch outside the publish folder.**
  `PublishSingleFile` was still emitting 11 native DLLs (`PresentationNative_cor3.dll`,
  `wpfgfx_cor3.dll`, `libSkiaSharp.dll`, …) alongside the exe; moving just the exe — which is
  what the installer's `[Files]` section did — threw `DllNotFoundException` in WPF's
  `HwndSubclass` the instant a window was created. `IncludeNativeLibrariesForSelfExtract` is
  now set in the csproj so every publish profile bundles them into the one exe, making
  "portable exe" actually portable. Verified by copying the lone exe into an empty folder and
  confirming a window opens.
- Log files are now pruned on the same retention window as history CSVs. They were rotating
  by name but never being deleted, so the log directory only ever grew. The README's
  "rotating daily logs" claim has been corrected to match.
- **Desktop shortcut is now unchecked by default in the installer**, matching autostart
  (already unchecked).
- **`BatteryReader` leaked a WMI result collection on every read.** `ManagementObjectSearcher
  .Get()` returns an `IDisposable` collection; the individual `ManagementBaseObject`s inside
  it were disposed, but the collection itself wasn't, at every call site — twice a second in
  the hot path. Now wrapped in `using`.
- **One history row could land in the wrong daily file at midnight.** `HistoryStore.Append`
  was appending the line to the buffer before checking whether the day had changed, so the
  first sample past midnight got flushed into the previous day's CSV alongside it. The check
  now runs first: a day change flushes the old buffer under its own day before the new
  sample joins.
- **The sensor tier could get stuck reporting Full after the driver stopped mid-session.**
  `Tier` gated on an ever-set high-water mark, so one good RAPL reading pinned it at `Full`
  for the rest of the session even if PawnIO later crashed or HVCI re-enabled — the recovery
  banner never came back. Replaced with a consecutive-miss streak per source (LHM, EMI):
  eight ticks without a real reading and the tier falls back, same threshold already used
  elsewhere for warm-up so a single transient miss still can't cause a flap.

### Security

- **Elevated autostart is now refused from user-writable locations.** The scheduled task runs
  PwrMon as administrator at logon with no UAC prompt; if the executable sits somewhere a
  non-administrator can replace it (a portable copy in Downloads or `%LocalAppData%`), that
  task is a privilege-escalation path. PwrMon now checks the ACL of the exe and its parent
  directory and declines, explaining why. Normal autostart is unaffected.
- **The PawnIO installer is now signature-verified before it runs.** Previously the only
  check was a minimum file size. PwrMon now verifies the Authenticode signature with
  `WinVerifyTrust` and shows the verified signer for confirmation before executing anything.
  The download also stages into a freshly created, randomly named temp directory instead of
  a fixed path, closing the window in which another process running as the user could have
  swapped the file between the write and the launch.
- **Cross-instance signals carry an explicit DACL** granting only the current user, rather
  than inheriting the process token's default. The signalling path now uses `OpenExisting`
  so it can never create an unsecured handle, and a shutdown triggered over that signal is
  logged instead of looking like an unexplained exit.
- **Build paths no longer leak into stack traces.** Embedded PDBs were baking the build
  machine's directory layout into the binary; `PathMap` normalises them.

### Changed

- `THIRD-PARTY-NOTICES.md` now covers every component that ships, not just the direct
  dependencies. This added four MPL-2.0 components (RAMSPDToolkit-NDD, DiskInfoToolkit,
  BlackSharp.Core alongside LibreHardwareMonitorLib), one Apache-2.0 (HidSharp) and one
  zlib/libpng (GLFW), each of which carries redistribution obligations that weren't
  previously documented.
- Installer version corrected to match the assembly version.

## [1.3.1]

Feature-frozen soak release. Verified on Zenbook UX3404VA (i7-13700H, Iris Xe).

### Added

- Windows EMI energy counters as the default sensor tier — CPU/iGPU watts with no driver and
  no elevation required.
- Power Budget card: exact system draw on battery; learned-baseline estimate on AC.
- Six live-switchable themes; user-selectable numeral and interface fonts.
- TradingView-style chart pan/zoom: time-only axis, cursor-anchored, clamped to data extent,
  Y auto-fit, LIVE returns to realtime at the current zoom span.
- Floating mini-graph, tray icon rendering live wattage as the icon, CSV export.
- History persistence across restarts with backfill and retention.

### Fixed

- **Fuel-gauge direction override.** When firmware charge/discharge flags contradict the
  measured capacity trend, the 90-second capacity trend wins. This is the fix for a real
  incident: a bad USB-C PD negotiation drained the battery at ~83 W to flat while ACPI
  firmware reported `Charging=True, ChargeRate=86W` and Windows' own tray showed green the
  whole way down. Surfaces as a red "PLUGGED IN — DRAINING" hero state.
- Wall-estimate honesty: CPU EMA time constant matched to the fuel gauge's publish cadence,
  blanked during adapter-assist when wall draw is genuinely unknowable.
- Backfill/live-sample race that could throw on non-ascending chart data.
- Hero text keeping the previous theme's colours after a mid-session theme switch.
- WinForms tray exceptions bypassing the dispatcher handler and popping a modal dialog.
