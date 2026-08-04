# ⚡ PwrMon

Live, real-time power telemetry for Windows laptops — because every battery app on the
Microsoft Store shows you *health summaries* when what you actually want to know is
**what is my machine pulling right now, in watts?**

No ads, no telemetry, no installer required. One portable exe. The only network access is
the optional PawnIO driver download — and only when you explicitly click the button.

## What it shows

- **Live power flow** — charge/discharge wattage straight from the battery's fuel gauge
  (ACPI via WMI), plus net flow, voltage, and current. PwrMon polls as often as twice a
  second; how often that number actually *changes* is the gauge's call, and most publish on
  their own cadence (~15–30 s on this project's reference machine).
- **CPU & iGPU silicon power** — RAPL package, cores and iGPU watts with **no administrator
  and no kernel driver**, read from Windows' own Energy Meter counters. Elevation plus PawnIO
  adds platform (PSys) power and CPU temperatures. See "Sensor tiers" below.
- **Temperatures** — drive temperature read straight from the disk with no administrator or
  driver required, so it works in every tier; CPU package, hottest core and throttle headroom
  arrive with the full sensor tier. See "Temperature coverage" below for what isn't available.
- **History chart** — pan/zoomable multi-series chart (net W, CPU W, iGPU W, battery %,
  CPU load, CPU °C, drive °C) over 5 minutes to 48 hours, with AC plug/unplug and resume
  event markers.
  History persists across restarts (daily CSV files, configurable retention).
- **Time estimates that aren't garbage** — time-to-empty / time-to-full computed from
  30-second smoothed rates, not Windows' famously bogus `EstimatedRunTime`.
- **Battery health** — wear % (design vs. actual full-charge capacity), cycle count,
  chemistry, design capacity.
- **Session stats** — energy drawn/charged this session, average and peak draw with
  timestamp, time on battery.
- **Tray ticker** — the live wattage (or battery %) rendered *as* the tray icon,
  color-coded: green charging, orange discharging, red heavy draw.
- **Floating mini-graph** — borderless always-on-top sparkline of the last 1–5 minutes;
  draggable, translucent, optional click-through.
- **Adjustable units** — W/mW, Wh/mAh, sampling interval 0.5–5 s.
- **CSV export** of any visible chart range.

## Building

Requires the .NET 8 SDK.

```powershell
# one-time: generate the app icon
powershell -ExecutionPolicy Bypass -File tools/gen-icon.ps1

# debug run
dotnet run --project src/PwrMon

# portable single-file publish (framework-dependent, ~small exe; needs .NET 8 Desktop Runtime)
dotnet publish src/PwrMon -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -o publish/framework

# fully self-contained single exe (no runtime needed, bigger file)
# IncludeNativeLibrariesForSelfExtract is set in the csproj and is load-bearing: without it
# PublishSingleFile still emits 11 native DLLs (PresentationNative_cor3, wpfgfx_cor3,
# libSkiaSharp…) alongside the exe, and moving the exe on its own gives DllNotFoundException
# in HwndSubclass as soon as a window opens.
dotnet publish src/PwrMon -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o publish/portable
```

## Sensor tiers (what needs elevation — and what doesn't)

PwrMon reads CPU/iGPU power from whichever of two sources is available, and prefers the one
that asks nothing of you:

| Tier | What you get | Requirement |
|------|--------------|-------------|
| **Default** — every user | Battery watts + health, **CPU/iGPU watts** via Windows' Energy Meter counters, CPU load, drive °C | nothing |
| **Full** — elevated | Everything above, **plus** CPU platform (PSys) power, package and hottest-core temperature, throttle headroom | admin + [PawnIO](https://pawnio.eu/), a signed HVCI-compatible sensor driver |

The default tier is the part most tools in this category don't have: no elevation, no kernel
driver, no UAC prompt, and unaffected by Memory Integrity. Elevation is an **upgrade** for
platform power and temperatures — not a prerequisite for watts.

Where a machine exposes no Energy Meter provider, the default tier has no CPU/iGPU watts and
the dashboard shows a banner with one-click fixes (restart elevated / get PawnIO / re-detect).
That path is verified on Intel; **it has not been tested on AMD** — see [ISSUES.md](ISSUES.md).

Battery wattage — the number that actually tells you your total system draw on battery —
works at every tier, elevated or not.

> On battery, **discharge rate = total system draw**. That's physics, not an estimate.
> When plugged in, the wall draw isn't measurable on most laptops; you get charge rate
> into the battery plus CPU/iGPU silicon power.

## Temperature coverage

| Sensor | Source | Tier |
|--------|--------|------|
| Drive | `IOCTL_STORAGE_QUERY_PROPERTY` on a query-only volume handle | every tier — no admin, no driver |
| CPU package / hottest core / throttle headroom | LibreHardwareMonitor MSRs | admin + PawnIO, same as CPU watts |

**iGPU temperature is not available** on Intel integrated graphics, and it isn't for lack of
trying. LibreHardwareMonitor exposes no temperature sensor for Intel GPUs; Level Zero Sysman
can't initialise on a stock driver install (no `HKLM\SOFTWARE\Khronos\OneAPI\LevelZero`
registration); Intel's IGCL `ctlEnumTemperatureSensors` returns `CTL_RESULT_ERROR_ZE_LOADER`
because it's implemented over Level Zero; and the kernel's own `D3DKMT` adapter perf data
returns zeros, which is why Task Manager shows no iGPU temperature either. Tools that do
report it load a kernel driver and read the GPU's thermal registers directly — see the
"No WinRing0" boundary in [SECURITY.md](SECURITY.md). On a monolithic mobile die the iGPU
shares the package thermal domain, so **CPU package temperature tracks it closely**.

Battery temperature is exposed by some machines through the WMI `BatteryTemperature` class;
where the class has no instances (as on this project's reference machine) there's nothing to
read. `tools/SensorProbe` reports all of the above for your hardware.

## Data & files

Everything lives in `%LocalAppData%\PwrMon\`:

- `settings.json` — preferences
- `history\history-YYYY-MM-DD.csv` — one file per day, pruned after the retention window
- `history\events.csv` — AC/resume event marks
- `logs\` — one file per day, pruned on the same retention window as history

## Architecture

```
src/PwrMon/
  Services/
    BatteryReader.cs    WMI root\wmi ACPI battery classes (rates mW, capacities mWh)
    HardwareReader.cs   LibreHardwareMonitor wrapper + sensor-tier detection
    DriveTemperature.cs drive °C via a query-only volume handle (no admin, no driver)
    Sampler.cs          polling loop, EMA smoothing, session stats, AC/sleep events
    HistoryStore.cs     daily CSV persistence, backfill, retention, export
    TrayService.cs      dynamic GDI+ tray icon (live wattage as the icon)
    StartupHelper.cs    HKCU Run key / elevated Task Scheduler autostart
  Views/
    MainWindow          dashboard: hero readout, stat cards, ScottPlot history chart
    MiniGraphWindow     borderless topmost sparkline
    SettingsWindow      behavior/autostart/history settings
tools/
  SensorProbe/          console dump of every sensor this machine exposes
  gen-icon.ps1          renders Assets/app.ico
```

Dependencies: [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0),
[ScottPlot](https://scottplot.net/) (MIT), System.Management. Everything that ships is listed
in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Privacy & security

- **No telemetry, no analytics, no auto-update, no crash reporting.** There is exactly one
  outbound URL in the entire codebase: the optional PawnIO installer download, behind an
  explicit consent dialog.
- **No WinRing0.** Most tools in this category read CPU power through WinRing0, a driver on
  Microsoft's vulnerable-driver blocklist. PwrMon doesn't ship it and never loads it — the
  default path uses Windows' own Energy Meter counters (no driver, no elevation), and the
  optional upgrade path is PawnIO, which is HVCI-compatible and signed.
- **Elevation is optional and never silent.** Battery telemetry needs none. The PawnIO
  download is signature-verified and its signer shown to you before anything runs.
- **Your data stays local.** Everything is under `%LocalAppData%\PwrMon\` and pruned on the
  retention window you set. Battery/CPU telemetry only — no identifiers are recorded.

Found something? See [SECURITY.md](SECURITY.md).

## Known issues

Open bugs and the punch list live in [ISSUES.md](ISSUES.md).
