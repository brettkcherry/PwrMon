# PwrMon — Oscilloscope Study

**Started 2026-07-21.** An independent research study into how real oscilloscopes work, look,
and behave — and which of those hundred-year-old conventions PwrMon should adopt.

This document is deliberately written without reference to any other design work in this repo.
It is a first-principles study of the instrument itself.

---

## 0. The thesis

**PwrMon is already an oscilloscope.** It plots a single scalar against time, sweeping
continuously, with the newest sample at the right edge. That is not "like" a scope — it is
literally what a scope in *roll mode* does.

So this study is not about costuming a chart in fake brushed aluminum. It is about
recognising what the instrument already is, and inheriting the conventions that a century of
instrument design has already solved. The valuable part of scope design is not the furniture.
It is the **information design**: how to make a moving quantity legible at a glance, how to
encode time spent, how to catch the event you weren't watching for.

Three separable layers, and we should be deliberate about which we take:

| Layer | Example | Take it? |
|---|---|---|
| **Convention** — measurable, functional | 1-2-5 steps, 8×10 graticule | Yes, wholesale |
| **Behavior** — what the instrument *does* | trigger, persistence, roll | Yes, selectively |
| **Look** — surface appearance | phosphor glow, panel texture | Yes, but disciplined |

---

## 1. How a scope actually works

The look of a scope is a *consequence* of its mechanism. Understanding the mechanism is what
separates an authentic homage from a costume.

An analog CRT scope has an electron gun firing a beam at a phosphor-coated screen. Two pairs
of deflection plates steer it: the **vertical** plates are driven by your (amplified) input
signal, the **horizontal** plates by an internally generated **sweep** — a sawtooth ramp that
drags the beam left to right at a constant, calibrated rate, then blanks the beam and snaps it
back to the left to start again.

Three consequences that define the entire aesthetic:

1. **The beam draws the curve continuously.** It is not a raster of pixels sampled onto a
   grid — it is a single moving point tracing a path. Lines are *drawn*, not *plotted*.
2. **Brightness encodes dwell time.** The phosphor's glow depends on how long the beam
   excites it. Where the trace moves slowly (flat regions), the beam lingers and the line is
   *bright*. Where it moves fast (steep edges), the same energy is smeared over more
   distance and the line goes *dim*. **A real scope trace is not a uniform-width stroke — its
   brightness varies inversely with slope.** This is the single most important visual detail,
   and almost every "retro scope" UI gets it wrong.
3. **The trigger exists because of the snap-back.** Without synchronisation, each sweep would
   start at a random point in the signal and the traces would not overlay — you'd see mush.
   The trigger holds the sweep until the signal crosses a set level going a set direction, so
   successive sweeps land on top of each other and the waveform appears to stand still.

---

## 2. The display: phosphor

The phosphor is chosen, not incidental, and is designated by a **P-number**. The choice is a
trade between colour, brightness, and persistence (how long it glows after the beam leaves).

| Type | Colour | Persistence | Notes |
|---|---|---|---|
| **P31** | Green, slightly blue-shifted | Short (<1 ms) | **The** scope phosphor. Most analog scopes from the '70s onward. Chosen because it sits near peak human eye response and resists burn-in |
| **P2** | Blue-green ("stoplight green") | Long (30 s+) | Inverse-power-law decay |
| **P7** | **Two layers**: blue flash → yellow-green trail | Very long (~1 min) | Cascade phosphor. Radar screens |
| **P11** | Blue | Short | Used for photographing traces |
| **P39** | Very saturated green | Long | The IBM 5151 monochrome-monitor look |
| **P1** | Green | Medium, exponential decay | The original |

**P31 is the canonical answer** if we want "this reads instantly as an oscilloscope."

**P7 is the interesting answer.** It is a *cascade* phosphor: an outer layer that flashes
bright blue where the beam currently is, over an inner layer that glows yellow-green and
decays over roughly a minute. The physical result is that **recency is encoded in colour** —
the live spot is blue-white, the recent past is a fading amber-green trail.

That maps onto a live power chart almost too perfectly. The right edge (now) would burn
blue-white; the last few minutes would trail off in amber. We would get an intuitive,
physically-grounded recency gradient *for free*, with no legend required.

### Persistence is a control, not a fixed property

Digital scopes made persistence adjustable, and this is where it becomes genuinely useful
rather than decorative:

- **Variable persistence** — traces fade over a set time (0.5 s, 1 s, 5 s…).
- **Infinite persistence** — nothing ever fades; the display accumulates every sweep, so the
  full envelope of everything the signal has ever done builds up on screen.
- **Intensity grading** (Tektronix's "Digital Phosphor" / DPO) — the scope histograms how
  often the trace passes through each pixel, and maps *frequency of occurrence* to brightness
  or colour. Common behaviour glows bright; rare anomalies show up faint but visible.

Intensity grading is the killer feature to steal. Over a 48-hour window, an intensity-graded
power chart would show the **envelope of normal** as a bright band, with genuine anomalies
(that 90 W adapter-assist event) as thin, unmistakable outliers. That is a real analytical
gain, not a skin.

---

## 3. The graticule

The measurement grid, and highly standardised:

- **8 divisions vertical × 10 horizontal**, traditionally 1 cm each (hence "8 cm × 10 cm" on
  the Tek 465 spec).
- **Minor tick marks subdivide each major division into 5** (i.e. every 2 mm) — but *only
  along the centre horizontal and centre vertical axes*, not across the whole field. This is
  why a scope graticule reads as calm rather than busy: the fine detail lives on the two
  centre crosshair lines only.
- **0% / 10% / 90% / 100% markings** on many graticules, for rise-time measurement.
- **All labelled settings refer to major divisions.** "VOLTS/DIV" means volts per *major*
  division. The whole instrument is calibrated in divisions, and the grid is the ruler.

The mental model is important: on a scope you don't read a number off an axis, **you count
divisions and multiply**. The grid is not decoration behind the data; the grid *is* the
measuring instrument.

---

## 4. The controls

Front-panel controls cluster into four groups, and the vocabulary is near-universal across
manufacturers.

**Vertical (amplitude)**
- `VOLTS/DIV` — stepped attenuator; sets the vertical scale
- `POSITION` — moves the trace up/down
- `COUPLING` — `AC` (blocks DC, shows only the wiggle) / `DC` (shows everything) / `GND`
  (disconnects input, shows the zero reference line)

**Horizontal (time)**
- `TIME/DIV` (a.k.a. `SEC/DIV`) — stepped; sets the sweep rate
- `POSITION` — moves the trace left/right
- `X-Y MODE` — replaces the time base with a second channel, plotting A against B

**Trigger**
- `LEVEL` — the threshold the signal must cross
- `SLOPE` — trigger on the rising or falling edge
- `MODE` — `AUTO` (sweeps anyway if no trigger arrives, so you always see *something*) /
  `NORMAL` (only sweeps on a real trigger) / `SINGLE` (arm once, capture one event, freeze)
- `HOLDOFF` — a dead time after each trigger during which re-triggering is inhibited
- `SOURCE` — which channel arms the trigger

**Display**
- `INTENSITY` — beam brightness
- `FOCUS` — spot sharpness
- `BEAM FIND` — compresses everything back inside the graticule so you can find a trace
  you've driven off-screen. **A "you are lost, take me back" button.**
- Graticule illumination

### The 1-2-5 sequence

`VOLTS/DIV` and `TIME/DIV` do not vary continuously. They step in a **1-2-5 sequence**:
1, 2, 5, 10, 20, 50, 100… Each step is a detent — a physical click.

This matters more than it looks. It means the scale is always a *round number*, so counting
divisions and multiplying stays mental arithmetic. It also means the display is **stable**:
the scale doesn't drift as the signal changes; it holds until you deliberately click it.

### Physical control vocabulary

Worth cataloguing for the *visual* direction: skirted knobs with a pointer line and a printed
dial ring; concentric dual knobs (coarse outer, fine inner); detented rotary switches with a
positive click; small toggle and slide switches for binary modes; push-buttons for latching
functions; BNC connectors; silkscreened white/black legends grouped inside printed boxes that
visually fence each functional group; a matte, low-glare panel finish so the CRT stays the
brightest thing on the instrument.

That last point is a genuine design rule: **on a real scope, the screen is the only bright
thing.** Everything else is deliberately matte and recessive.

---

## 5. What this means for PwrMon

Splitting honestly into what genuinely improves the app versus what would be cargo cult.

### 5.1 Genuine wins

**A. `WATTS/DIV` with 1-2-5 stepping — replaces auto-fit Y.**
Currently `FitY()` recomputes the Y limits from the visible data on every sample. That means
the axis labels can shift continuously, and the *same* wattage sits at a different screen
height from one second to the next. A scope would never do this. Snapping to a 1-2-5 W/div
ladder (1, 2, 5, 10, 20, 50 W/div) and only stepping when the trace approaches the edge would
make the display **stable**, make heights comparable over time, and turn the grid into a real
ruler. This is a straight functional upgrade that happens to also be authentic.

**B. Intensity/dwell-weighted trace rendering.**
Render the trace with opacity (or width) inversely proportional to slope, exactly as beam
physics dictates. Steady 15 W idle draws as a bright, solid line; a fast spike to 90 W draws
as a dim streak. It looks unmistakably like a scope *and* it encodes real information: how
much time was actually spent at each wattage. A 90 W spike lasting 200 ms *should* look
fainter than an hour at 15 W — that's honest.

**C. Persistence / intensity grading on long windows.**
On the 24h/48h views, accumulate a histogram of watts-vs-time-of-day and grade it by
frequency. The "envelope of normal" emerges as a bright band; anomalies stay visible as
outliers. Genuinely analytical.

**D. Trigger.**
The most under-appreciated steal. `TRIGGER LEVEL = 60 W`, `SLOPE = rising`, `MODE = SINGLE`
→ "freeze the chart the next time draw exceeds 60 W and show me what happened around it."
For a tool whose entire founding story is *catching an anomaly you weren't watching for*,
a trigger is not decoration — it's the feature the incident of 2026-07-16 was begging for.
`AUTO` / `NORMAL` / `SINGLE` map cleanly onto the existing LIVE/PAUSED concept.

**E. Naming what already exists.**
The live chart *is* roll mode. The LIVE button *is* `BEAM FIND` / "return to now". The range
pills *are* a `TIME/DIV` switch. Adopting the vocabulary costs nothing and makes the app feel
like an instrument rather than an app with a graph in it.

**F. Graticule discipline.**
Adopt the real convention: majors across the field, minor 5-subdivision ticks *only* on the
centre axes, 0 line emphasised. Calmer and more legible than a uniform grid.

**G. Cursors.**
Two draggable vertical cursors reading Δt and ΔW between them, plus energy (Wh) integrated
over the span. Standard scope feature, directly useful here.

### 5.2 Interesting but needs thought

- **`AC`/`DC` coupling → absolute watts vs. delta-from-baseline.** "AC coupling" the power
  trace (subtracting the learned idle baseline) would show *only the activity*, which is
  arguably what you care about when hunting what's eating power.
- **X-Y mode → CPU watts vs. total system watts.** Would reveal how much of total draw the
  CPU actually explains. Genuinely interesting, but a niche second view.
- **Holdoff** — probably meaningless at our sample rates.

### 5.3 Cargo cult — avoid

- **Draggable skeuomorphic knobs.** A mouse cannot do rotary. Real knobs are great *because*
  they're physical; rendering one on screen keeps the metaphor and loses everything that made
  it good. A detented ladder can be a click-through control or a scroll target instead.
- **Fake bezels, screws, brushed-metal textures, patina, wear.** Costume, not instrument.
- **Aggressive CRT simulation** — barrel distortion, scanlines, heavy bloom — anything that
  reduces legibility of the number you came to read. A *hint* of glow on the trace, yes.
- **Skeuomorphic BNC connectors, carry handles, rack ears.** No.

The guardrail, consistent with the project's stated ethos: **take the information design, not
the furniture.**

---

## 6. Theme directions (phosphor palettes)

Four candidate themes derived directly from real phosphors, to sit alongside the existing six.

**`P31`** — the canonical scope. Near-black screen, slightly blue-shifted green trace
(~#4AE05C territory), dim green graticule, everything else recessive. The "obviously an
oscilloscope" option.

**`P7 Radar`** — the standout idea. Cascade phosphor emulation: the live edge burns
blue-white, the trace decays through yellow-green over the visible window. Recency encoded as
colour, straight from the physics. No other power tool looks like this.

**`P11`** — blue, short persistence, photographic. Cooler and more clinical than P31.

**`Storage`** — bistable storage-tube look: a dimmer, flatter, slightly amber field where
everything written stays written. Pairs naturally with infinite-persistence mode.

Each would carry the panel discipline too: matte, recessive chrome so the trace is the
brightest thing on screen.

---

## 7. Open questions

1. Does `WATTS/DIV` replace auto-fit entirely, or is auto-fit a mode (like a scope's
   `AUTOSET`) that snaps to the nearest 1-2-5 rung?
2. Does the trigger freeze the display, or mark-and-continue (a flagged event you can jump
   to)? The latter fits an always-on monitor better.
3. Is dwell-weighted rendering feasible in ScottPlot without a custom renderer? May need a
   per-segment alpha, which could mean drawing segments individually — needs a perf check at
   100k+ points.
4. Should phosphor themes also change *behavior* (P7 implying persistence on), or stay purely
   visual with persistence as an independent control? Coupling them is more authentic;
   separating them is more predictable.

---

## Sources

- [Tektronix — Oscilloscope Systems and Controls](https://www.tek.com/en/documents/primer/oscilloscope-systems-and-controls)
- [Tektronix — Oscilloscope Types (DPO / digital phosphor)](https://www.tek.com/en/documents/primer/oscilloscope-types)
- [TekWiki — Tektronix 465](https://w140.com/tekwiki/wiki/465)
- [Papay — DSO intensity-gradient displays](https://www.hit.bme.hu/~papay/edu/DSOdisp/gradient.htm)
- [Inst Tools — CRT fluorescent screen / phosphor types](https://instrumentationtools.com/crt-fluorescent-screen/)
- [Instrumentation and Control Engineering — Fluorescent screen of CRT](https://instrumentationandcontrollers.blogspot.com/2011/04/fluorescent-screen-of-crt.html)
- [LabGuy's World — CRT phosphors of interest to the experimenter (PDF)](http://www.labguysworld.com/crt_phosphor_research.pdf)
- [Teledyne LeCroy — Using the display graticule](https://blog.teledynelecroy.com/2014/09/back-to-basics-using-display-graticule.html)
- [Liquisearch — Oscilloscope front panel controls: graticule](https://www.liquisearch.com/oscilloscope/features_and_uses/front_panel_controls/graticule)
- [Elliott Sound Products — The design of meter (and oscilloscope) attenuators](https://sound-au.com/articles/meter-atten.htm)
- [TubeTime — CRT phosphor video](https://tubetime.us/index.php/2015/10/31/crt-phosphor-video/)
