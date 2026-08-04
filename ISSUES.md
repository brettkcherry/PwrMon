# Known issues & punch list

Open work on PwrMon, roughly in the order it bothers us. Items here are things we've
confirmed on real hardware, not speculative polish.

## Bugs

- **Chart series checkboxes don't persist across minimize-to-tray and restore.** None of
  CPU, iGPU, Battery % or Load % come back in the state they were left in.
- **Mini-graph is effectively non-functional.** It renders nothing useful and responds to
  nothing. Needs a full pass, not a patch.
- **Paper theme isn't a real light theme.** Still carries many dark elements; reads as a
  half-converted dark theme rather than a light one.

## Improvements

- **Mini-graph should be resizable**, with a smaller minimum size — especially horizontally.
- **Tray icon legibility.** Beyond the colour coding, the number itself isn't intuitive at a
  glance. Worth reconsidering what it shows and how.
- **Revert settings before close.** No way to back out changes made in the Settings window
  once they've been previewed live.
- **Curate the font lists down hard.** Far fewer options, chosen to match the build's design
  instinct rather than exposing every installed family.

## Design

- **More themes, and better mid-range ones.** Three additional light themes and three
  mid-range. Mid-range themes are almost never done well; there's an opportunity there.
- Theme colour choices, waveform colours and text colours are working well overall — the
  palette direction is right, it's the coverage that's uneven.

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
- **No battery temperature** where WMI `BatteryTemperature` has no instances, which is the
  case on the reference machine. Worth re-checking on other hardware via `SensorProbe`.
- **Temperature series share the chart's right-hand axis with the percentage series.** Both
  occupy 0–105, so the scale works, but the axis carries two units. The checkbox labels are
  what disambiguate.

## Hardware coverage

- Verified on a Zenbook UX3404VA (i7-13700H, Iris Xe). **No AMD or second-machine testing
  yet** — `tools/SensorProbe` is the first move on any new hardware.