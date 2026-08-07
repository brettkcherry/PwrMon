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
- **Precision-honesty staleness note.** The hero readout and Power Flow card now drop to
  whole watts and append "reading held Ns" once the raw battery rate has held its value past
  `UnitFormatter.StaleAfterSeconds` (20s) — see MULTIMETER-STUDY.md §7.1. The threshold logic
  and formatting are fully unit tested, but the actual on-gauge cadence this tunes against
  (~15–30 s, from the 2026-07-16 incident notes) was observed on one machine; worth
  confirming the note appears at a sensible moment and doesn't flicker on real hardware.

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

## Fixes

- Resizing the mini chart should a) remove the little triangle in the corner b) work from all side and corners of the widget. movement remains "drag from anywhere inside"

