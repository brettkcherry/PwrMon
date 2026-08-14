# Changelog

Notable changes to PwrMon. Format loosely follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Changed

- **"Heavy draw" is now measured against this machine, not against a 70 Wh Zenbook.** The tray
  icon turned red above a flat 60 W. That number is 0.86C on the reference machine's pack — it
  was "about an hour of runtime left", worked out by hand once and hard-coded as watts, and it
  fails in both directions on anything else. A tablet-class machine peaking at 15 W never
  reached it, so red never fired and the colour carried no information at all. A gaming laptop
  idling at 30 W sat red permanently, which carries just as little.

  No constant survives that spread, and no constant can be derived from the hardware either —
  nothing predicts what a machine *can* draw without watching it. So PwrMon now watches.
  `DrawProfile` keeps a time-weighted histogram of observed off-AC discharge in 1 W bins, and
  the icon reads heavy above that machine's own 90th percentile: not "this is a big number"
  but "this is more than you normally pull". It's weighted by seconds rather than samples, so
  changing the sampling interval doesn't silently reweight months of history, and it halves
  itself every ~100 battery-hours so an ageing pack and changing habits don't get averaged in
  forever. The profile lives in `%LocalAppData%\PwrMon\drawprofile.json` — its own file, since
  it's observed data rather than a preference, and 200 bins would drown a settings file people
  are invited to hand-edit.

  Before there's enough history to say anything (30 battery-minutes), the threshold falls back
  to the draw that would flatten *this* pack in ~1.2 h, and to the old 60 W only when the
  battery can't be read at all. Two thresholds rather than one — trips at p90, clears at p75 —
  because a load parked on a single threshold flickers the icon between two colours.

  Learning is gated to genuine off-AC discharge, the same guard the system baseline already
  uses: draining while plugged in is an abnormal state and a lower bound on the real draw, so
  folding it in would teach the profile the wrong shape.

## [1.6.0] — 2026-08-13

### Changed

- **License: MIT → GPL-3.0, dual-licensed with a commercial option.** Changed 2026-08-11,
  while the repository was still private with zero users — so no copy of PwrMon was ever
  distributed under MIT and nobody's rights changed. The open license is GPL-3.0
  ([LICENSE](LICENSE)); a separate [commercial license](COMMERCIAL-LICENSE.md) covers anyone
  who needs to embed PwrMon — or just its driverless RAPL sensor layer — in a closed-source
  product.

### Added

- **The mini graph now plots eight series, not just Net watts** — CPU, iGPU, Wall, Battery %,
  Load %, CPU and drive temperature, each keeping the colour it already wears on the main
  history chart. Which one is showing is a per-click cycle, opted into from the right-click
  menu (ships with just Net enrolled, so clicking does nothing until a second series is
  added). Cycling skips any series this machine can't currently measure — landing on a
  permanently empty graph reads as a bug, not as a missing sensor. The window-drag and the
  click share one mouse handler (`DragMove()` is modal and only returns once the button is
  back up), told apart afterward by whether the window actually travelled, with a few pixels
  of slop for hand tremor.

  The tray icon also now opens on a single left click instead of the shell's usual double
  click — the icon's whole job is the number on it, and a closer look shouldn't cost two.
- **Always on top**, for the mini graph — a checkable item on its own context menu and the
  tray menu, mirroring the existing click-through toggle. The window-length and opacity menu
  items are fixed alongside it: they were always clickable but never showed which option was
  currently active. A 15-minute "show last" option too.
- **"Plugged in but draining" now actually alerts you.** PwrMon has detected this state since
  the 2026-07-16 TB4 incident, but only ever showed it passively — a red hero state in a
  window that's usually closed, and a tray tooltip you have to hover. On 2026-08-13 the same
  failure recurred: the charger stopped holding the machine up at 10:47, the detector was
  correct the entire way down, and the laptop still went from 91% to flat over 70 minutes
  with no warning from PwrMon or from Windows (which sees "AC connected" and stays quiet).
  A smoke detector with no siren.

  Now the same signal pushes a tray notification when the state is confirmed, and again at
  50 / 35 / 20 / 15 / 10% on the way down — the first alert can easily land while you're away
  from the machine. Alerts carry a sound by default (Settings → Behavior turns the sound off;
  the alerts themselves stay on). Strictly scoped to the AC-contradiction case: normal
  on-battery low-battery warnings are Windows' job and it does those fine.

- **Wall input is now a chart series.** The estimate already existed on the Power Budget card;
  it just had nowhere to live over time. Plotted on the left (watts) axis alongside Net and
  CPU, since it's the outermost envelope of the same quantity. Off by default.

  It required a new `wall_w` CSV column, so history written before this has no wall data — the
  reader treats the missing column as absent rather than zero, and old files keep loading
  unchanged (the columns have always been append-only). The series is also deliberately blank
  while the battery is assisting the adapter: the wall's share is genuinely unknowable then,
  and a plausible-looking line there would be an invention.

  Each of the 13 themes got its own wall colour rather than reusing an existing slot, picked
  from whichever hue family that palette wasn't already spending on a series. `ThemePaletteTests`
  now enforces this in CIELAB: every series pair within a theme must clear ΔE 15, and wall must
  clear ΔE 25 against the plot background. That check also caught two pre-existing collisions
  it wasn't written to look for yet — Meadow and Phosphor both had Battery % landing on nearly
  the same color as Net — fixed alongside it.

- **PwrMon can update itself.** Settings → Updates checks for a new release, verifies it, and
  runs the installer. Until now a fix could be released but never reached anyone who had
  already installed — for the one app here that runs elevated and talks to a kernel driver,
  "we can ship a patch but not deliver it" was the wrong place to be.

  Because PwrMon's installer isn't code-signed, Authenticode can't vouch for it, so the trust
  root is an ECDSA P-256 public key compiled into the binary. The release manifest is signed
  with the matching private key; the manifest carries the installer's SHA-256, so one
  signature authenticates both. A manifest that fails verification is reported as a failure,
  not swallowed as "no update available". There is no background check and no automatic
  install — see [SECURITY.md](SECURITY.md) and [docs/RELEASING.md](docs/RELEASING.md).

  The updater is inert in any build where the signing key hasn't been configured: it makes no
  network request at all rather than doing something unverified.
- **Release signing tooling** — `tools/new-release-key.ps1` generates the key pair,
  `tools/sign-release.ps1` produces and self-verifies `latest.json` + `latest.json.sig`. Both
  require PowerShell 7.
- **Dependabot** ([.github/dependabot.yml](.github/dependabot.yml)) watching NuGet and GitHub
  Actions, with a cooldown so a freshly published version waits a few days before being
  offered — compromised packages are usually caught within a day or two, and the people they
  reach are the ones who upgraded within hours.

### Fixed

- **A stat label could break mid-word instead of at a space.** "Temperature" wrapped as
  "Temperatu" / "re" once a wide monospace numeral font (Cascadia Mono) pushed the Auto-sized
  value column wide enough to squeeze the label column — visible in the Processor card's
  screenshot before this shipped. `TextWrapping="Wrap"` breaks anywhere it has to;
  `WrapWithOverflow` only breaks at a word boundary and lets a single long word overflow into
  the value column's slack instead, so "Cores / Platform" still wraps at its space and
  "Temperature" no longer splits. One shared style in `Themes/Dark.xaml`, so every theme's
  cards inherit the fix together.

### Security

- **CI actions are pinned to full commit SHAs** rather than tags. A tag is mutable: whoever
  controls an action's repository can repoint `v7` at new code, and every workflow using it
  picks that up silently. Harmless while the workflow holds no secrets, and not harmless the
  moment a release workflow with a signing key sits beside it.

- **Contributor License Agreement** ([CLA.md](CLA.md)) — one comment on a first PR, keeping
  your own copyright. It's what lets a contribution be covered by both licenses rather than
  the open one alone. Bug reports and hardware dumps need no agreement.
- **[TRADEMARK.md](TRADEMARK.md)** — PwrMon and the bolt icon are unregistered trademarks of
  Brett Cherry. The code is open, the identity isn't; forks rename.
- **A real hardware-contribution path.** `SensorProbe` now writes a timestamped dump file
  beside itself and prints the path, instead of leaving output in a console window to be
  selected and copied — that step was where reports got lost. It also warns up front that the
  dump carries battery serial and drive model, which is worth knowing before posting it
  publicly. Paired with a structured hardware-report issue form and a README section that
  leads with the gap: **verified on one laptop, AMD untested, and a dump where nothing works
  is worth as much as one where everything does.**

- **Sideways scrolling on the main chart.** Shift+wheel, or a tilt-wheel/two-finger
  horizontal swipe, now pans the time axis the same way dragging does — WPF doesn't route
  horizontal wheel events on its own, so this required hooking the window's HWND directly.
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
- **Mini-graph is now resizable**, down to a 150×90 floor, via invisible drag grips on all
  four edges and all four corners — the window is borderless and transparent, so there's no
  OS chrome to hit-test for standard edge-drag resize. Dragging from the top or left edge
  grows the window in that direction while the opposite edge stays anchored, matching native
  resize behavior; dragging from anywhere in the body still moves the whole widget. Size and
  position persist the same way as before. (Replaces the earlier corner-only grip, which also
  drew a visible triangle glyph in the corner — removed, since the grip is felt, not seen.)
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

- **Launching the app did nothing visible when slim mode was on.** Slim mode means "free the
  dashboard's memory when you close it" — that's what its Settings checkbox says, and what its
  close handler does. But startup also gated the *initial* window on it, so anyone with slim
  mode enabled launched straight into the tray with no window: double-click the exe or the
  Start-menu shortcut, and to all appearances nothing happened. Only `--minimized` and the
  `StartMinimized` setting suppress the first show now.
- **The "Start minimized to tray" setting was silently ignored.** It was read out of
  `AppSettings.Current` several lines *before* `AppSettings.Load()` ran, so it always saw a
  defaults instance rather than the user's saved value — the checkbox only ever worked via the
  `--minimized` argument the autostart entry passes. Moved the read after the load.
- The startup show/hide decision, the second-instance (`--replace`) decision, and the
  close-to-tray/slim-mode close decision are now pure functions in the new
  `Services/WindowLifecycle.cs`, called from `App.OnStartup` and `MainWindow_Closing` instead
  of being inlined. Same behavior, but now pinned by 20 unit tests covering the full
  `CloseToTray x SlimMode x StartMinimized` matrix — two of the three bugs above came from
  exactly this surface having zero coverage.
- **Chart wheel-zoom was too sensitive**, especially on precision trackpads sending many
  small-delta events per gesture — it zoomed on a fixed step per event instead of scaling by
  the actual delta, so a light two-finger scroll could blow past the intended range in one
  swipe. Now scales proportionally to wheel delta with a gentler per-notch step.
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
