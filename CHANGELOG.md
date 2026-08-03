# Changelog

Notable changes to PwrMon. Format loosely follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added

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
- `tools/SensorProbe` now reports ACPI thermal zones, drive temperatures, and the Intel GPU
  routes above, times each LibreHardwareMonitor hardware update, and runs LHM's storage
  detection behind a 20-second watchdog so a hang is reported instead of producing no dump.

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

### Fixed

- Log files are now pruned on the same retention window as history CSVs. They were rotating
  by name but never being deleted, so the log directory only ever grew. The README's
  "rotating daily logs" claim has been corrected to match.

### Added

- `SECURITY.md` with a private reporting channel and the trust boundaries stated explicitly.
- `CONTRIBUTING.md` and this changelog.
- `ISSUES.md` — the open punch list, moved out of the README.
- `tools/list-shipped-assemblies.ps1` — reconciles what actually ships against
  `THIRD-PARTY-NOTICES.md`, flagging anything that isn't plain MIT.
- Unit tests for the startup ACL predicate.
- `run-tests.ps1 -Configuration Release`, so tests can run while a Debug build is locked by
  a running instance.

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
