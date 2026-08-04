# Known issues & punch list

Open work on PwrMon, roughly in the order it bothers us. Items here are things we've
confirmed on real hardware or in the code, not speculative polish. Anything already shipped
lives in [CHANGELOG.md](CHANGELOG.md) — this file is only what's still outstanding.

## Bugs

- **WMI result collections are never disposed.** `ManagementObjectSearcher.Get()` returns an
  `IDisposable` collection that `BatteryReader` iterates but never disposes (`Read` and
  `RefreshFullChargeIfStale`, plus all three queries in `ReadStatic`). The individual
  `ManagementBaseObject`s are disposed correctly; the collection wrapping them isn't, so
  unmanaged COM handles are left to finalization — twice a second, for as long as the app
  runs.
- **One history row lands in the wrong daily file at midnight.** `HistoryStore.Append`
  appends the line to the buffer *before* testing whether the day changed, so the first
  sample after midnight is flushed into the previous day's CSV. Exactly one row per
  rollover. Display self-corrects (`LoadRecent` filters on the timestamp, not the filename),
  but the file on disk is wrong and filename-based retention can prune it a day early.
- **The sensor tier can't fall back once it reaches Full.** `HardwareReader.Tier` gates on
  `_maxPowerSeen`, a high-water mark only ever reset by `Reinit()`. One good RAPL read pins
  the tier at `Full` for the rest of the session, so a machine whose driver stops mid-session
  keeps claiming full telemetry and the banner never reappears to offer a fix.

## Improvements

- **Mini-graph should be resizable**, with a smaller minimum size — especially horizontally.
  Currently `ResizeMode="NoResize"` at a fixed 300×150.
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
