# Known issues & punch list

Open work on PwrMon, roughly in the order it bothers us. Items here are things we've
confirmed on real hardware or in the code, not speculative polish. Anything already shipped
lives in [CHANGELOG.md](CHANGELOG.md) — this file is only what's still outstanding.

## Queued discussions

- **Chart interaction.** TradingView-style pan/zoom shipped (time-only axis, clamped to data
  extent, Y auto-fit, LIVE = go-to-realtime), but the interaction model still needs to
  diverge from TradingView's in places.
- **Window open/close behaviour**, from the tray and app-wide: what opens where, and what
  closing actually does.

## Known limitations

- **No iGPU temperature on Intel integrated graphics.** Four routes tested and ruled out
  (LibreHardwareMonitor, Level Zero Sysman, Intel IGCL, D3DKMT) — see "Temperature coverage"
  in the README. Reading it requires a kernel driver PwrMon deliberately doesn't ship.
- **No battery temperature.** WMI `BatteryTemperature` has no instances on the reference
  machine (Zenbook UX3404VA), and HWiNFO doesn't expose it there either — confirmed
  2026-08-07, so this isn't a PwrMon gap, the controller likely just doesn't publish it over
  ACPI. Worth re-checking on other hardware via `SensorProbe` in case a different embedded
  controller does.
- **Temperature series share the chart's right-hand axis with the percentage series.** Both
  occupy 0–105, so the scale works, but the axis carries two units. The checkbox labels are
  what disambiguate.

## Hardware coverage

- Verified on a Zenbook UX3404VA (i7-13700H, Iris Xe). **No AMD or second-machine testing
  yet** — `tools/SensorProbe` is the first move on any new hardware.
- The Energy Meter counter path is what makes the default (no-admin, no-driver) tier work,
  and it is **verified on Intel only**. Until it's tested on AMD, the README's default-tier
  claim is an Intel claim. This is the widest gap between what the docs promise and what has
  actually been observed.


