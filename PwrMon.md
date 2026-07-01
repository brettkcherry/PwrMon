are there really not other better ( way better ) options than Pure Battery Analytics? UUUUUUUUUuuuuuuuGGGGGHHHHHHHH

I feel that deep, soul-crushing sigh. The Microsoft Store is packed with clunky, ad-heavy, or abandonware battery tools that look like they were built for Windows 8 and haven't been updated since. Pure Battery Analytics has a lot of features, but its UI and setup can be incredibly frustrating.

You absolutely do not have to settle for it. Depending on whether you want deep analytics, a lightweight tray indicator, or hardware-level power control, there are significantly better, cleaner options.

1. The Built-In Powerhouses (Zero Bloat)
Before downloading anything else, Windows has two incredibly accurate native options that skip the store apps entirely.

The Native Battery Report: If you just want raw data on how fast your battery is degrading, nothing beats the system's own log. Open PowerShell or Command Prompt as Admin and run:

PowerShell
powercfg /batteryreport
This spits out a beautifully clean HTML file (saved to your user directory) tracking your exact design capacity vs. current full charge capacity, history logs, and cycle counts.

BatteryInfoView (by NirSoft): If you want that exact raw data but in a live UI without generating a file every time, this utility is iconic. It's 100% free, entirely portable (just an .exe, no installation required), and has zero ads. It tracks charge cycles, voltage, and accurate real-time wear down to the milliwatt.

2. The Clean Modern Alternatives
If you want something that actually looks like it belongs on a modern desktop and gives you visual stats or custom alerts:

Battery Health Checker: A fantastic, 100% free and open-source tool hosted on GitHub. It pulls directly from Windows' native API to give you a clean, gorgeous dashboard tracking your cycle count, wear percentage, and hardware details with zero data collection or premium paywalls. It also handles custom low/high battery notifications beautifully.  
Battery Health Checker

Battery Flyout: If you miss the clean, concise pop-up menu from Windows 10 instead of the massive Windows 11 quick settings pane, this app replaces your tray icon. It gives you a lightweight visual chart of your last week of usage, cycles, and voltage right from the taskbar.

3. The Power User Move: G-Helper
If you happen to be running an ASUS laptop (like a ROG, Strix, TUF, or ProArt), G-Helper is a masterclass in software utility. It replaces heavy manufacturer bloatware completely. Aside from being a tiny, lightweight, single executable file, it lets you set strict physical battery charge limits (e.g., capping your charge at 60% or 80% to permanently stop degradation if you are often plugged in) and lets you customize your fan curves and power limits on the fly.  
G-Helper

Ditch Pure Battery Analytics, grab either a lightweight open-source tool or the portable NirSoft utility, and give your taskbar some room to breathe.

Which direction fits your current setup best?

Show me how to automate or interpret the native Windows Battery Report

Tell me more about setting up charge limits to extend battery life

I want power stats, current active power statistics . I use g helper, it doesn't do anything that I need

Got it—you don't care about health summaries or changing charge limits; you need live, active, real-time telemetry of what your system is pulling right now in watts. G-Helper only throws up a static snapshot of the current discharge rate inside the app, which doesn't help you map out spikes or see what's actually chewing through your power.

Since you want hard, live power stats and active wattage tracking, forget standard "battery apps" and look at these three precision utilities instead:

1. The Lightweight Choice: BatteryMaster
This is a modern open-source tool built exactly for this frustration. The developer created it specifically because existing utilities failed at showing live power stats gracefully.

The Power Stat: It hooks into the system tray and displays your exact active discharging or charging power (in watts/mW) live in the taskbar, color-coded by state.  
BatteryMaster

Active Tracking: It includes an active history tracker so you can leave it running and look back to see exactly when and where your power draw spiked.

2. The Advanced Per-App Tracker: WattSeal
If you want to know where those active watts are going, this is a phenomenal tool. It's written in Rust, meaning it runs with near-zero system overhead.  
WattSeal

The Power Stat: It pulls live hardware telemetry directly from the CPU (via RAPL) and GPU to give you a real-time rolling graph of your total system wattage.

Per-App Breakdown: Unlike standard battery trackers, it actually attributes power consumption to individual threads and processes. If a rogue background task or a specific browser tab suddenly starts pulling 30W, you’ll see it instantly on the active dashboard.  
WattSeal

3. The Pure Hardware Standard: HWiNFO64
  
WattSeal
If you don't want a stylized dashboard and just want raw, unadulterated hardware sensor logging, HWiNFO64 is the gold standard for PC enthusiasts.

The Power Stat: Fire it up in "Sensors-only" mode and scroll down to your Battery and CPU/GPU sections. It tracks:

Charge/Discharge Rate (Real-time active wattage draw from the battery, updated at sub-second intervals).

CPU Package Power and GPU Power (Exactly how many watts the core silicon is pulling at that exact millisecond).

Logging: You can configure it to graph any of these metrics live or log them to a file while you run a specific workload to see your exact active power curves.

If you want a quick visual indicator of your current active drain right in your taskbar, grab BatteryMaster. If you want a deep, live breakdown of which applications are actively pulling those watts, WattSeal is exactly what you are looking for.

okay, I tried them.... any other options ?

If you have already tried HWiNFO64, BatteryMaster, and WattSeal and none of them fit the bill, you are likely looking for a specific layout, presentation, or way to stream that live telemetry.

Since you use G-Helper, you have a gaming/performance machine with plenty of hardware sensors that expose exact real-time power metrics. If you want pure active power stats without the junk, try these completely different approaches:

1. MSI Afterburner + RTSS (The Live On-Screen Overlay)
If you want to track your active power statistics while actually using apps or gaming without switching windows to look at a desktop UI, this is the definitive enthusiast setup.

The Power Stat: You can configure RivaTuner Statistics Server (RTSS) to display a live, transparent HUD in the corner of your screen.

What it tracks: It can pull real-time CPU Package Power (W), GPU Power Draw (W), and individual frame times. You can set it to show either a live numeric watt ticker or a rolling line graph directly over your workspace or games.

2. LibreHardwareMonitor (The Persistent Graphing Tool)
If HWiNFO64 was too much of a dense, tabular wall of text, LibreHardwareMonitor is a completely open-source fork of Open Hardware Monitor that does graphing far better.

The Power Stat: It lets you select exact power sensors (like Battery Discharge Rate, CPU Package, or GPU Power) and send them directly to a customizable, floating plot graph.

The Setup: You can minimize the main window and keep just a small, translucent, resizable wattage history chart floating on your desktop or secondary monitor.

3. Battery Mode (The Direct Windows Icon Replacement)
If you want something seamless that doesn't feel like a heavy telemetry app but still feeds you the exact number, Battery Mode is a classic open-source replacement for the native Windows power flyout.

The Power Stat: It calculates the active milliwatt (mW) or watt (W) drain directly from your battery's fuel gauge and can display the exact discharge rate directly in your system tray as an active icon.

Bonus: It provides an instant, light toggle for your active power profiles (Balanced, Performance, Eco) right from the tray icon, playing very nicely alongside G-Helper.

The Advanced/DIY Route: If you want absolute, raw control over your data stream to build your own dashboard, you can open PowerShell and query the active charge/discharge telemetry directly from the hardware layer via WMI:  
GitHub

PowerShell
Get-CimInstance -Namespace root\wmi -ClassName BatteryStatus | Select-String -Pattern "DischargeRate"
If any of these layouts feel closer to what you need, let me know. What exactly feels missing from the ones you've tested so far—are you looking for a cleaner desktop graph, a permanent taskbar ticker, or an on-screen overlay?