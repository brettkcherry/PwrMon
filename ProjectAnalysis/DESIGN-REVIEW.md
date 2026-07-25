# PwrMon Design Review — "Bench instrument, not dashboard"

*2026-07-15 · reviewed against v1.3.1: MainWindow, SettingsWindow, MiniGraphWindow, Dark.xaml, ThemeService, TrayService*
*Visual mockups: see [DESIGN-REVIEW.html](DESIGN-REVIEW.html) (open in a browser).*

The bones are excellent — honest data, real density, tabular numerals, six live-switchable
themes. What's missing is **hierarchy** and **identity**: every pixel currently has the same
visual weight, and the app dresses like a generic dark dashboard when it is actually a
measuring instrument. This review proposes leaning all the way into the instrument identity —
a direction the settled name **PwrMon** wears naturally: terse, vowel-dropped, NirSoft-lineage,
like a model number stamped on a panel meter.

---

## 01 · What's already working (keep these)

- ✅ **Typography discipline.** Bahnschrift tabular numerals with `NumeralAlignment="Tabular"`
  so live values don't jitter — a detail most commercial apps miss. The app's strongest
  existing design asset.
- ✅ **The theme engine.** Live-mutating brushes + a `Changed` event pushing chart colors is
  real infrastructure. Six palettes with distinct personalities (Phosphor and Paper
  especially). Everything proposed below rides on it.
- ✅ **Density without clutter.** Eight cards × four stats is a lot of information that never
  feels crowded. Card padding, 11px small-caps titles, dim-label / bright-value rhythm — all correct.
- ✅ **The tray ticker.** Rendering the live wattage *as* the icon is the killer
  differentiator. No other tool does this.
- ✅ **Honesty as an aesthetic.** Tooltips that say "Estimate," the "(30s smoothed)"
  qualifier, the tier banner that explains *why* a sensor is locked. Keep this voice
  everywhere — it's the brand.

---

## 02 · Findings

Numbered for reference. Severity is about experience impact, not code quality.

### F1 — No visual hierarchy: everything shouts equally · **HIGH**

Eight identical cards, each a uniform 4-row grid. Battery chemistry (which never changes)
gets exactly the same visual weight as live system draw (which changes twice a second). The
eye has no path: nothing tells a first-time user "look here first." The hero helps, but below
it the layout is flat.

**Fix:** split content into three tiers — *Now* (live, animated, sparklined), *Trends* (the
chart), *Reference* (battery specs and health demoted to a slim strip). See Section 04.

### F2 — Stock Windows chrome breaks the spell · **HIGH**

A light-gray system title bar sits on top of a `#0F1115` app. It's the first pixel a user
sees and it says "unfinished." The Settings and Mini windows have the same problem.

**Fix:** minimum viable — `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)`, ~10 lines,
dark title bar on every window. Full version — `WindowChrome` with the title bar merged into
the hero row: app name + tier badge left, caption buttons right. The mini-graph window
already proves you can do borderless well.

### F3 — The hero is a number without an instrument · **HIGH**

The single most important readout is plain text. It has no direction (is energy flowing in or
out?), no context (is 18 W a lot? what's the session baseline?), and no memory (what did the
last minute look like?). All that context exists in the data model already — it just isn't drawn.

**Fix:** the instrument hero (Section 04): state-colored readout, wall→system→battery flow
diagram with animated direction, battery gauge with ETA, and a 60-second sparkline with the
session peak marked.

### F4 — Hard-coded card widths fight the window · **MEDIUM**

Cards are fixed at 195–235 px each (`MainWindow.xaml`), so the WrapPanel breaks rows at
arbitrary points as the window resizes — sometimes 5+3, sometimes 6+2, with ragged right
edges. The drag-to-rearrange feature just polished deserves a real grid to land in.

**Fix:** uniform card width via a shared resource (or a custom panel that divides available
width into equal columns). Cards should feel like modules in a rack, not sticky notes.

### F5 — Color roles are overloaded · **MEDIUM**

Amber is simultaneously the brand accent, the Net series, and System draw. Green is charging,
the Battery % series, and the LIVE indicator. When one hue means three things, none of them
reads instantly.

**Fix:** a small semantic token table (Section 05) added to `ThemePalette` — separate *brand
accent* from *data series* from *state*. Mostly renaming + a few new palette slots; the theme
engine already supports it.

### F6 — The tier banner ignores the theme · **MEDIUM**

Hard-coded `#2A2416` / `#5C4A18` / `#E8D9A0` — a dark amber wash that looks wrong on Paper
and clashes with Glacier/Synth. Same class of issue: the drag-ghost chrome, the mini-graph
border `#333B49`, and FlatButton hover states `#2A313D` are all theme-blind.

**Fix:** derive them — banner = accent at ~12% over card color; button hover = border color
at 50%. Kill every literal hex in XAML that isn't in a palette.

### F7 — Stock ComboBox, the last unthemed control · **MEDIUM**

`Foreground=Black` as a workaround (Dark.xaml:138) because the default WPF template ignores
dark backgrounds — so the status bar shows light-gray Windows-95-lineage dropdowns inside an
otherwise coherent dark app. It's the most visible seam in daily use.

**Fix:** one proper dark `ControlTemplate` for ComboBox (toggle + popup + item container),
styled like FlatButton. Tedious but one-time; reused by Settings too.

### F8 — Chart toolbar crowds eleven controls into one row · **POLISH**

Six range pills + five series checkboxes + LIVE + export compete in a single strip. The
series toggles are stock checkboxes whose color-coding (text color = series color) is easy to miss.

**Fix:** ranges stay left; series toggles become dot-chips (colored dot + label, dimmed when
off); LIVE becomes a pill that visibly changes state when you pan away.

### F9 — Live data doesn't feel alive · **POLISH**

Values snap to new numbers every sample with zero transition, while the drag-and-drop got
FLIP animation. The *core content* is the least animated thing in the app.

**Fix:** tween the hero wattage toward its target (~250 ms, dispatcher timer — cheap); a
subtle 300 ms foreground flash on stat values when they change by >5%. No layout animation,
just number life. Respect reduced-motion via `SystemParameters.ClientAreaAnimation`.

### F10 — Status bar mixes settings with navigation · **POLISH**

Sampling interval and unit pickers (settings that change monthly) sit in the permanently
visible status bar next to Mini graph / Settings buttons (navigation). Prime real estate
spent on rarely-touched controls.

**Fix:** move interval/units into Settings; status bar becomes a true status bar — tier
badge, sample age, history size — plus the two buttons. Cleaner and one less row of
ComboBoxes to theme.

---

## 03 · Direction: a bench instrument

PwrMon's ethos — live watts first, no telemetry, portable exe — is the ethos of a
*tool that measures*, not an app that summarizes. The design language should come from the
bench: multimeters, oscilloscopes, panel meters. Concretely:

- **Readouts have direction and scale**, not just magnitude
- **Reference data is a label on the chassis**, not a competing display
- **The frame is part of the device** (custom chrome, not borrowed Windows furniture)
- **Motion means signal** — things animate because energy is flowing, never for decoration

The settled name — **PwrMon** — fits this direction exactly: it's the name of a device, not
an app, and it came full circle from the original rant transcript (`PwrMon.md`). The direction
also gives the icon revisit a brief — a panel-meter readout / meter needle rather than a
generic bolt.

---

## 04 · Proposal

### The instrument hero

*(Rendered mockup in the HTML version — current vs. proposed, in Volt theme.)*

```
▼ DISCHARGING                                                        ┌─────────────┐
18.4 W                 [WALL] ──── [SYSTEM 18.4W] ◀━━ [BATTERY −18.4W]│▓▓▓▓▓▓▓▓ 64% │
CPU 6.2 · iGPU 0.8              (animated flow dashes)               └─────────────┘
· rest 11.4                                                    3 h 05 m to empty · 46.1 Wh
─────────────────────────────────────────────────────────────────────────────────────────
LAST 60 S   ╱╲___╱▔▔╲__╱╲____________________________________    PEAK 42.0 W 14:32
```

What changes:

- The wattage is **state-colored** (orange discharging, green charging, blue on AC idle —
  the tray icon already speaks this language; the hero finally matches it).
- The **flow diagram** shows direction: dashes physically move from battery to system while
  discharging, wall to both while charging; on AC the wall node lights up with the estimated
  input from the Power Budget learner.
- The **battery gauge** replaces the bare percentage.
- The **60-second sparkline** answers "what just happened" without touching the big chart,
  and carries the session peak as a marked point — instant scale context.

All of it is data the Sampler already produces.

### Tiered cards + reference strip

Live cards get one big number, two support rows, and a 5-minute sparkline. Battery specs and
health — data that changes monthly, not per-sample — collapse into a chassis-label strip.

**"Now" tier (4 live cards, each with an embedded sparkline):**

| System draw | Processor | iGPU | Session |
|---|---|---|---|
| **18.4 W** (accent) | **6.2 W** (blue) | **0.8 W** (purple) | **9.6 Wh** |
| Wall input — | Load 11% | 3D load 2% | Avg draw 16.2 W |
| Rest of system 11.4 W | Temp 52 °C | Clock 350 MHz | On battery 2 h 41 m |
| ~~~sparkline~~~ | ~~~sparkline~~~ | ~~~sparkline~~~ | ~~~sparkline~~~ |

**Reference strip (single quiet row below):**

> BATTERY 56.2 Wh full · 11.61 V · Li-ion │ HEALTH 19.7% wear · 494 cycles · 70.03 Wh design │ ESTIMATES avg discharge 16.8 W · avg charge —

The eight-card wall becomes four live modules plus one quiet strip. Power Flow and Power
Budget merge into **System draw** (they describe the same watts from two sides — on battery
show measured draw, on AC show the estimate + wall input, exactly the logic the Budget card
already runs). Estimates fold into the reference strip and the hero ETA. Nothing is deleted —
reference details keep living in tooltips and the strip; drag-to-rearrange still applies to
the live tier.

### Chart toolbar

Ranges stay left as pills; series toggles become **dot-chips** (colored dot + label, dimmed
at ~55% opacity when off); LIVE becomes a bordered pill that visibly changes state when you
pan away from the live edge.

**Bonus, nearly free: scope mode** — double-click the chart (or press F11) to collapse the
card tier and let the chart fill the window for watching draw during a test run. One
Visibility toggle plus the FLIP animation already built.

---

## 05 · Semantic color roles

One hue, one job. Volt values shown; each theme fills the same slots. This is an extension of
`ThemePalette`, not a rewrite.

| Role | Volt value | Used for | Never used for |
|---|---|---|---|
| **Brand accent** | `#F5B62E` | Icon, title-bar identity, focus rings, selected pills | Any data series or live value |
| **Charging / gain** | `#3FB950` | Hero + tray while charging, In row, flow-to-battery | The LIVE pill (gets its own quiet treatment) |
| **Discharging / draw** | `#F0883E` | Hero + tray on battery, Out row, hero sparkline | Warnings (that's Red's job) |
| **Alert** | `#F85149` | Heavy draw threshold, wear > 30%, errors | Ordinary discharge states |
| **Series: system** | `#E8C55A` | Net/system-draw chart series + card sparkline | UI chrome — series hues live only in data ink |
| **Series: CPU / iGPU / %** | `#58A6FF` / `#BC8CFF` / `#3FB950` | Chart series, dot-chips, card sparklines | UI chrome |

Splitting series-net (`#E8C55A`, slightly desaturated and lighter) from the brand accent is
what lets amber stay special.

---

## 06 · Phased plan (soak-week compatible)

### Phase 1 — Chassis polish · SMALL · no layout changes

- Dark title bars on all three windows via DWM attribute (F2, minimum version)
- Theme the banner, button hovers, mini-graph border — derive from palette, kill stray hexes (F6)
- Dark ComboBox template (F7) — biggest daily-visibility win per hour spent
- Uniform card width so rows wrap cleanly (F4)
- Semantic color slots in `ThemePalette` — plumbing for everything later (F5)

### Phase 2 — The instrument · MEDIUM · the visible redesign

- Instrument hero: state-colored readout, flow diagram, battery gauge, 60 s sparkline + peak (F3)
- Tiered layout: 4 live cards with sparklines + reference strip; merge Flow/Budget into System draw (F1)
- Chart toolbar: dot-chips, stateful LIVE pill; move interval/units into Settings (F8, F10)
- Full custom `WindowChrome` with tier badge in the title bar (F2, full version)

### Phase 3 — Signal & motion · SMALL–MEDIUM · personality

- Number tween on the hero + change-flash on stats, reduced-motion aware (F9)
- Animated flow dashes tied to actual direction and magnitude
- Scope mode (chart fills window)
- Icon revisit to match: panel-meter readout / meter needle, amber-on-dark — pairs with the PwrMon wordmark

> **Recommendation:** do Phase 1 now — it's invisible to the soak experiment (no behavior or
> layout changes) and removes every "unfinished" tell. Live with it a few days, then decide
> Phase 2 alongside the rename-to-PwrMon and icon revisit, since hero + wordmark + icon form
> one identity move.

---

*Mockups rendered in Volt theme · all figures from the Zenbook UX3404VA*
