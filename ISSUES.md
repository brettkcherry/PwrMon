# Known issues & punch list

Open work on PwrMon, roughly in the order it bothers us. Items here are things we've
confirmed on real hardware or in the code, not speculative polish. Anything already shipped
lives in [CHANGELOG.md](CHANGELOG.md) — this file is only what's still outstanding.

## Improvements

- **Displayed precision should follow the source's resolution.** The hero readout prints two
  decimals below 10 W off a fuel gauge that is quantized and publishes every ~15–30 s — more
  significant figures than the measurement carries. Wants a staleness indicator too, so a
  number that hasn't been refreshed doesn't read as live. The argument is already written up
  in [ProjectAnalysis/MULTIMETER-STUDY.md](ProjectAnalysis/MULTIMETER-STUDY.md) §7.1.

## Queued discussions

- **Chart interaction.** TradingView-style pan/zoom shipped (time-only axis, clamped to data
  extent, Y auto-fit, LIVE = go-to-realtime), but the interaction model still needs to
  diverge from TradingView's in places.
- **Window open/close behaviour**, from the tray and app-wide: what opens where, and what
  closing actually does.

## Needs confirming on hardware

- **Chart series checkboxes across minimize-to-tray and restore.** The clobber path — each
  checkbox assignment during setup firing the handler and writing the others'
  not-yet-applied state back to settings — is guarded now. Worth one pass on real hardware
  to confirm the symptom is actually gone.
- **Mini-graph resize.** Was fixed `ResizeMode="NoResize"` at 300×150 with no way to shrink
  it. Now has a corner grip (`Thumb`-based, since the window is borderless + transparent and
  has no chrome for the OS to hit-test) down to a 150×90 floor, with size persisted the same
  way as position. Builds clean; drag interaction itself needs a pass on real hardware — no
  way to click-and-drag a native window from here to confirm the grip feels right.

## Known limitations

- **No iGPU temperature on Intel integrated graphics.** Four routes tested and ruled out
  (LibreHardwareMonitor, Level Zero Sysman, Intel IGCL, D3DKMT) — see "Temperature coverage"
  in the README. Reading it requires a kernel driver PwrMon deliberately doesn't ship.
- **No battery temperature** where WMI `BatteryTemperature` has no instances, which is the
  case on the reference machine. Worth re-checking on other hardware via `SensorProbe`.
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
