# ⚡ PowerMonitor: Technical Project Analysis

A thorough, mindful analysis of the **PowerMonitor** project—a portable Windows desktop utility built with **.NET 8** and **WPF** that provides real-time, high-precision power telemetry, custom background polling, dynamic tray notification rendering, and a floating sparkline overlay.

---

## 📂 Project Overview & Structure

The codebase is extremely clean, modern, and modular. It relies on a minimal set of dependencies:
*   **LibreHardwareMonitorLib**: Extracts CPU and iGPU silicon-level power data.
*   **ScottPlot.WPF**: Renders real-time, interactive panning/zooming graphs.
*   **System.Management**: Interoperates with WMI to poll battery metrics.
*   **System.Diagnostics.PerformanceCounter**: Polls Windows Energy Meter (EMI) counters as a driverless fallback.

Here is the high-level architecture of the files and folders:

```mermaid
graph TD
    App[App.xaml / App.xaml.cs] --> Sampler[Sampler.cs]
    App --> Tray[TrayService.cs]
    App --> History[HistoryStore.cs]
    Sampler --> Battery[BatteryReader.cs]
    Sampler --> Hardware[HardwareReader.cs]
    
    subgraph UI Windows (Views)
        MainWindow[MainWindow.xaml / .cs]
        MiniGraph[MiniGraphWindow.xaml / .cs]
        SettingsWindow[SettingsWindow.xaml / .cs]
    end
    
    App --> MainWindow
    App --> MiniGraph
    App --> SettingsWindow
    
    subgraph Core Models
        PowerSample[PowerSample.cs]
        AppSettings[AppSettings.cs]
    end
    
    Sampler -.-> PowerSample
    MainWindow -.-> AppSettings
```

---

## ⚙️ Core Component Breakdown

### 1. Telemetry Capture (`BatteryReader.cs` & `HardwareReader.cs`)
The application splits telemetry collection between battery metrics and silicon-level package draw.

*   **ACPI Battery Telemetry (`BatteryReader.cs`)**:
    *   Queries `root\wmi` using `ManagementObjectSearcher` for battery charging/discharging states, capacities, and voltages.
    *   Accesses properties of `BatteryStatus`, `BatteryFullChargedCapacity`, `BatteryStaticData`, `BatteryCycleCount`, and the `root\cimv2` class `Win32_Battery`.
    *   Implements a cache/throttler for static data (like designed capacity and chemistry) and slow-drifting metrics (re-polling full-charge capacity only once per minute to avoid WMI overhead).
    *   Handles little-endian packed ASCII codes for battery chemistry (e.g. converting the byte representation of `"LiP"` to `"Li-polymer"`).
*   **Silicon Power Telemetry (`HardwareReader.cs`)**:
    *   Wraps LibreHardwareMonitor (`LHM`) to obtain CPU Package, CPU Cores, CPU Platform, iGPU, and discrete GPU power consumption.
    *   **Sensors Tiers**: Implements a tier detection system (`SensorTier`) to handle Windows 11 Memory Integrity (HVCI) blocks on the legacy WinRing0 MSR driver:
        1.  `Full`: Full RAPL telemetry via administrative access and signed driver.
        2.  `EmiOnly`: Fallback to Windows Energy Meter (EMI) counters via `PerformanceCounterCategory("Energy Meter")` (CPU/iGPU package draw in milliwatts, working without admin or drivers).
        3.  `DriverBlocked` / `NeedsAdmin`: Flags actionable user statuses, offering one-click shortcuts to elevate or download the HVCI-compatible **PawnIO** driver.

### 2. The Heartbeat Polling Loop (`Sampler.cs`)
*   Uses a modern `PeriodicTimer` (introduced in .NET 6) on a background thread (`LoopAsync`) instead of older thread sleeps or UI dispatch timers.
*   **Smoothing & Estimates**: Applies Exponential Moving Average (`EMA`) smoothing over a $\tau = 30$ seconds window to compute stable, non-fluctuating estimations of `TimeToEmpty` and `TimeToFull`.
*   **AC Power Budget Learning**:
    *   On battery, discharge rate is an exact measurement from the fuel gauge. On AC, charging rates vary and total system power draw cannot be directly read from the battery.
    *   `Sampler` learns the "rest of system" baseline (screen, RAM, SSD, fans, board) by measuring `DischargeRate - CpuPackageW` over time while on battery ($\tau = 180\text{ s}$).
    *   On AC, it estimates total system draw as `CpuPackageW + LearnedBaseline`, allowing the app to approximate wall/adapter input assuming $\approx 90\%$ power efficiency.

### 3. System Tray Integration (`TrayService.cs`)
*   Hosts a `NotifyIcon` (from Windows Forms, though isolated from WPF namespace pollution).
*   **Dynamic Ticker**: Renders the current wattage or battery percentage *directly as* the tray icon itself.
*   Creates a `32x32` bitmap, paints the formatted text and state-appropriate colors (green charging, orange discharging, red heavy draw) using GDI+ (`Graphics.DrawString`), and sets it as the icon handle.
*   **Memory Leak Prevention**: Correctly calls `DestroyIcon` via P/Invoke on `user32.dll` to clean up the GDI icon handles, preventing systemic leaks during fast sampling.

### 4. History Storage (`HistoryStore.cs`)
*   Records telemetry and system events (AC plugged/unplugged, resume from sleep) to CSV files inside `%LocalAppData%\PowerMonitor\history\`.
*   **Buffered Writing**: Writes are stored in a `StringBuilder` buffer and flushed every 15 seconds (or upon day transition / shutdown) to minimize disk IO overhead.
*   Loads up to 48 hours of recent historical data on startup, so chart visuals survive application restarts.
*   Implements an automated directory cleaner based on user-configured retention days.

### 5. UI Views and Styling (`Views/`)
*   **`MainWindow.xaml`**: Implements a clean dashboard consisting of key stat cards, real-time hero readouts, and a `ScottPlot.WPF` plot. The graph automatically scales Y limits based on active series and includes vertical dotted lines mapping sleep/AC transitions.
*   **`MiniGraphWindow.xaml`**: A borderless, translucent, always-on-top overlay.
    *   Implements drag-to-move repositioning (saving coordinates to settings).
    *   Uses native Win32 `WS_EX_TRANSPARENT` via `SetWindowLong` to support a **click-through mode** where user clicks fall through to underlying applications.
*   **`ThemeService.cs`**: Implements dynamic runtime styling. All theme colors reside as `SolidColorBrush` definitions in app resources. Changing themes modifies the color values in-place so all elements refresh immediately. Custom numeral fonts (like "Bahnschrift" or "Cascadia Mono") can be loaded to guarantee stable digit widths (avoiding numeral jitter during fast updates).

---

## 💎 Design Highlights & Best Practices

1.  **Strict Lifecycle Isolation**: The dashboard view can be run in **"Slim Mode"** (`AppSettings.SlimMode`). When closed, the window is fully destroyed and garbage collected. The app continues polling in the tray with a resident memory footprint of under $\approx 15\text{ MB}$. Reopening the window lazily regenerates the UI and fills the history from disk.
2.  **Clean Single-Instance Control**: Uses a named `Mutex` combined with `EventWaitHandle` signals (`ShowSignal`, `ExitSignal`). Poking the executable a second time alerts the primary instance to bring itself to the foreground, while a `--replace` argument signals the background instance to gracefully exit and surrender the hardware handles (crucial for seamless administrative elevation).
3.  **No-Bloat Philosophy**: Extremely lightweight. No network connections are made unless the user explicitly requests a PawnIO driver download, keeping telemetry strictly local.

---

## 📈 Potential Optimization Notes (Observation Only)

*   **WMI Exceptions**: If a WMI provider or class is corrupted or missing (common on some stripped Windows installations), queries like `DesignedCapacity` inside `BatteryReader.ReadStatic` could throw. Currently, these are wrapped in broad try-catch blocks and log failures, which is excellent, though some default configurations (like design capacity falling back to full-charge capacity) could benefit from more fallback safety defaults.
*   **Chart Point Capping**: ScottPlot's `DataLogger` is highly optimized, and the trim limit is capped at 260,000 points. This provides excellent memory ceiling controls for long-running sessions.
