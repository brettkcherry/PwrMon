# PwrMon — Panel Meter Study

**Started 2026-07-21.** Independent research into analog panel meters — moving-coil needles,
VU meters, edgewise movements — and what PwrMon should inherit from instruments designed to be
*glanced at* rather than read.

Written without reference to other design work in this repo. Third in the series after
`SCOPE-STUDY.md` and `MULTIMETER-STUDY.md`.

---

## 0. The thesis

A scope is watched. A multimeter is read. **A panel meter is glanced at.**

Panel meters are built *into* something else — a rack, a dashboard, an amplifier, a substation
cabinet. They are always on, single-purpose, and usually in your peripheral vision. Nobody
sits and studies a panel meter; you look up, absorb a state in under a second, and look away.

That is precisely PwrMon's tray icon, its mini-graph, and arguably its hero readout. **They
are panel meters.** And it turns out panel meters solved the exact problem those components
have: how do you make a fast-moving quantity legible to a glance?

The answer is the most valuable finding in any of these three studies.

---

## 1. Ballistics — the headline

A needle does not show you the signal. It shows you the signal **through a defined
mechanical response**, and that response is *standardised*, because the raw signal is
unreadable.

**VU meter** (average-responding):
- **300 ms rise time**, and a symmetrical 300 ms fall.
- Should show only minor overshoot.
- Deliberately slow. It reads *loudness*, not peaks — a musical, integrated impression.

**PPM** — Peak Programme Meter (peak-responding):
- **5 ms integration** (Type I) or 10 ms (Type II) — near-instant attack.
- **Deliberately slow fallback**: ~1.7 s to drop 20 dB (Type I), ~2.8 s to 24 dB (Type II).
- Wildly **asymmetric on purpose**: catch the peak instantly, then release it slowly enough
  that a human eye can actually see the value it caught.

That asymmetry is the whole trick. A transient too fast to perceive gets captured at full
height and then *held long enough to read*. You cannot miss a peak on a PPM, no matter how
brief — which is why broadcast engineers use them and VU meters for different jobs entirely.

**This is the answer to PwrMon's jittery readout.** Not "smooth it more" (which loses the
spike) and not "show raw" (which is unreadable). The instrument answer is **asymmetric
ballistics: fast attack, slow decay.** A 90 W spike lasting 200 ms would jump immediately to
full height and then ease down over a second or two — visible, readable, and honest about the
peak, without the number dancing.

### Measured, not assumed

Both models were simulated against an identical 200 ms, 90 W spike over a 14 W idle
(5 ms steps; VU = critically damped 2nd-order ω=22, PPM = 20 ms attack / 620 ms release):

| t | raw | VU | PPM |
|---|---|---|---|
| 0.29 s (pre-spike) | 14 | 14.0 | 14.0 |
| 0.35 s (50 ms in) | 90 | **42.6** | **85.1** |
| 0.50 s (spike ends) | 14 | 84.2 | 89.4 |
| 0.70 s | 14 | 18.7 | 68.6 |
| 1.20 s | 14 | 14.0 | 38.4 |
| 2.50 s | 14 | 14.0 | 17.0 |

**Peak actually reached — VU: 84.7 W · PPM: 90.0 W (true peak 90 W).**

Two things fall out. The PPM reaches the *true* peak while the VU undershoots by 5 W and only
gets near it just as the event ends — 50 ms in, the VU is still reading 42.6 W, less than half
the real value. And after the event, the PPM is still showing 68.6 W a full 200 ms later and
38.4 W at 1.2 s, so a human eye has seconds to read a spike that lasted a fifth of one. The VU
is back to idle almost immediately, having effectively hidden the event.

For a power monitor whose founding purpose is catching transients you weren't watching, PPM
ballistics are not a stylistic preference — they are the correct engineering choice.

## 2. Damping — there is a correct amount

The moving-coil (D'Arsonval) movement is a mechanical resonator: a coil on a spring. Left
alone it would oscillate around its target and take forever to settle.

Real meters add **eddy-current damping** — the aluminium coil former moving through the
permanent magnet's field generates opposing currents that bleed off energy — "so that the
pointer settles quickly to its position without oscillation."

Three regimes, and only one is right:
- **Underdamped** — overshoots and rings. Jittery, exhausting, imprecise.
- **Overdamped** — never overshoots but crawls. Feels laggy and unresponsive.
- **Critically damped** — fastest possible approach with no oscillation. **This is the target.**

Any animated readout in PwrMon (needle, bar, or number) should be a critically damped
second-order response, not a linear tween and not a naive EMA. It is barely more code and it
is the difference between "animated" and "instrument".

## 3. The scale is the instrument

As with the scope's graticule: you read a panel meter **against printed marks**, and the marks
are designed, not generated.

- **Colored zones.** Green / amber / red bands let you read *state* before you read *value*.
  A glance answers "is this OK?" without processing a number at all. This is the single
  highest-bandwidth thing a panel meter does.
- **Non-linear scales.** The ohms scale on a VOM is reversed and compressed because the
  physics is reciprocal. Scales get shaped to the measurement rather than forced uniform —
  resolution is spent where it matters.
- **Mirror strip.** A mirrored band under the needle; align needle with reflection to
  eliminate parallax. The instrument ships a correction for its own reading error.
- **Front zero-adjust screw.** A visible mechanical admission that the instrument drifts, plus
  the means to fix it.

There is a consistent ethic here worth naming: **good instruments are honest about their own
limitations, visibly, on the face.**

## 4. Form factors and standards

- **Round**, **square**, and **edgewise** (a straight, narrow scale — designed for dense racks).
- **DIN standard cutouts**, e.g. 1/8 DIN = 96 × 48 mm, so instruments are interchangeable
  across panels. Standardisation as a design value.
- **Peak-hold pointers** — some meters carry a second, lighter "ghost" needle dragged along
  and left at the maximum, so the peak stays visible after the main needle falls back.

The ghost needle is a lovely, directly stealable idea: a faint marker on PwrMon's readout
showing session peak draw, sitting quietly behind the live value.

---

## 5. What this means for PwrMon

### 5.1 Ballistics on every live readout — the big one

Replace naive smoothing with proper instrument ballistics:

- **Hero watts / tray icon / mini-graph**: PPM-style — fast attack (~50–150 ms to full),
  slow decay (~1.5–3 s). Spikes become visible *and* readable. Idle stays rock steady.
- **Where an average is wanted** (session average, baseline): VU-style symmetric ~300 ms.
- **Any needle or bar animation**: critically damped second-order, never a linear tween.

This is a small, self-contained change with an outsized effect on how the app *feels*, and it
directly fixes the "readout too jittery to read" problem that motivated the tray fix earlier.

### 5.2 Zones before numbers

Idle / normal / heavy draw as colour bands on any gauge — and arguably on the tray icon,
which already colour-codes but could carry explicit thresholds. The goal: **answer "is this
OK?" in peripheral vision, without reading digits.** For a tray icon at 16 px, this is the
only channel that actually works.

### 5.3 Peak-hold ghost marker

A faint secondary marker at session peak on the hero readout or mini-graph. Costs one
variable; gives the display a memory.

### 5.4 A needle is legitimately on the table

Every other study said "don't render fake knobs" — knobs are *inputs*, and a mouse is a bad
rotary switch. **A needle is an output.** There is no interaction to betray; it is pure
display, and it is genuinely superb at showing rate-of-change and zone-at-a-glance.

A small, well-damped needle gauge for live watts — with proper ballistics and coloured
zones — would be authentic rather than costume. This is the one skeuomorphic element the
research actively endorses.

### 5.5 Avoid

- Fake screws, bezels, brushed metal, glass reflections, drop shadows.
- Needle animation without real damping — worse than no needle.
- Zones so busy they become decoration; three bands maximum.
- A needle *instead of* the number. It is a complement: needle for state, digits for value.

---

## 6. Convergence across the three studies

Independently researched, the three instrument families agree:

| Idea | Scope | Multimeter | Panel meter |
|---|---|---|---|
| Catch what you didn't watch | Trigger | Peak MIN/MAX | Peak-hold pointer |
| Show deviation, not absolute | AC coupling | REL / Δ | Suppressed-zero scale |
| Pick the scale for me | AUTOSET | Autorange | (fixed by design) |
| Read state before value | Graticule divisions | Bargraph | **Coloured zones** |
| Don't overstate precision | Dwell/intensity honesty | ±(% + counts) | Mirror strip |
| Tame the raw signal | Persistence | Averaging / True RMS | **Ballistics** |

Six ideas, three independent traditions, total agreement. That is about as strong a signal as
design research ever gives you: these are not aesthetic choices, they are the correct answers
to problems every measuring instrument has.

**If PwrMon adopts only one thing from all three studies, it should be ballistics.**

---

## 7. Open questions

1. Exact attack/decay constants for power draw — audio ballistics are tuned to audio. Needs
   empirical tuning against real traces (we have 14 days of CSV to test against).
2. Should the tray icon get ballistics too, or does its ~1 s update cadence make it moot?
3. Needle gauge: a card on the dashboard, a mini-graph mode, or an alternative tray rendering?
4. Are colour zones user-configurable, or derived from the learned baseline (idle = baseline
   ±20%, heavy = 3× baseline)? Derived is less config and adapts per machine.

---

## Sources

- [Sound on Sound — What's the difference between PPM and VU meters?](https://www.soundonsound.com/sound-advice/q-whats-difference-between-ppm-and-vu-meters)
- [Elliott Sound Products — VU and PPM audio metering](https://sound-au.com/project55.htm)
- [AV-Info — Audio level meters](https://av-info.eu/audio/meters.html)
- [Nuts & Volts — The care and feeding of analog meters](https://www.nutsvolts.com/magazine/article/the-care-and-feeding-of-analog-meters)
- [Engineers Edge — D'Arsonval movement meter review](https://www.engineersedge.com/instrumentation/electrical_meters_measurement/darsonval_movement.htm)
- [Study Electrical — Permanent magnet moving coil (PMMC) instruments](https://studyelectrical.com/2019/11/permanent-magnet-moving-coil-pmmc-instrument.html)
- [IDC — D'Arsonval movement (PDF)](https://www.idc-online.com/technical_references/pdfs/instrumentation/Darsonval_Movement.pdf)
- [Weschler — Choosing panel meters](https://www.weschler.com/blog/choosing-panel-meters/)
- [Hoyt Meter — DIN series analog panel meters](https://hoytmeter.com/analog-panel-meters/din-series.html)
- [Simpson Electric — 260 Series 8 VOM manual (PDF)](https://simpsonelectric.com/wp-content/uploads/File/260-8man.pdf)
