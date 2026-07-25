# PwrMon — Multimeter Study

**Started 2026-07-21.** Independent research into how multimeters — analog VOMs and modern
DMMs — present a measured value, and what PwrMon should inherit.

Written without reference to other design work in this repo. Companion to `SCOPE-STUDY.md`,
but deliberately researched on its own terms.

---

## 0. The thesis

The scope study was about **the trace**. This one is about **the number**.

A multimeter is the discipline of the single trusted value. It has no history, no sweep, no
waveform — just one quantity, displayed as well as it can honestly be displayed. Every design
decision in a good DMM is in service of one question: *how do I show a number without
implying more certainty than I have?*

PwrMon's hero readout is a DMM display. `+80.4 W` is a multimeter reading. So the question
this study asks is uncomfortable and useful: **have we earned that decimal point?**

---

## 1. Resolution: counts, not digits

DMM resolution is specified in **counts**, not digits — because "digits" is ambiguous once
you hit the half-digit convention.

- A **4000-count** meter displays `0000`–`3999`, then changes range. Often written "3¾ digits."
- 3½ digits ≈ 2000 counts; 6000, 50000-count meters exist for finer resolution.
- The leading "half digit" can only show 0 or 1 — hence 1999 or 3999 rather than 9999.

**Counts define when the instrument gives up and shifts range.** They are a statement about
where meaningful resolution ends. That is a more honest specification than "digits", which
invites you to count characters on a screen and assume they all mean something.

## 2. Accuracy is two terms, and the second one is the interesting one

DMM accuracy is quoted as **±(% of reading + N counts)**. For example ±(2% + 2): a 100.0 V
reading is somewhere in 97.8–102.2 V.

Two independent error sources:
- **% of reading** — proportional (gain) error, grows with the value.
- **+ N counts** — a fixed floor of uncertainty in the last digits, regardless of magnitude.

The `+ counts` term is why a good meter never claims its last digit is solid. It is a formal,
published admission that **the final digits are noise**, and it scales the claim to the
instrument, not the marketing.

## 3. The bargraph exists because digits are bad at trends

Fluke and others put an **analog bargraph** underneath the digital readout. It is not
nostalgia. Digits are excellent for precision and terrible for movement — a value flickering
between `80.4` and `79.1` is unreadable, and your eye cannot integrate it.

The bargraph updates faster than the digits and shows direction, rate, and instability at a
glance. It is explicitly there for "tracking changing or unstable signals" and for nulling
(adjust until the bar centres). **Digits for the value, bar for the behaviour.**

This is a direct, cheap upgrade path for PwrMon's hero: keep the big number, add a fast bar
beneath it.

## 4. The modes that turn a meter into an observer

A DMM's buttons are mostly about *not having to watch it*:

| Mode | What it does | Why it exists |
|---|---|---|
| **MIN/MAX** | Records lowest/highest seen; beeps on a new extreme | You can't stare at it for an hour |
| **Peak MIN/MAX** | Captures transients as fast as **250 µs** | The digits can't show what the ADC caught |
| **REL / Δ** | Zeroes a reference; shows deviation from it | Absolute value is often the wrong question |
| **HOLD** | Freezes a stable reading | Your hands/eyes are busy elsewhere |
| **AutoHOLD** | Freezes, beeps, re-arms on the next stable reading | Probe, look away, look back |
| **Autorange** | Picks the range automatically | Resolution without fiddling |
| **Continuity beep** | Audible pass/fail | You do not have to look at all |

The through-line: **a good instrument assumes you are not watching it.** For an always-on
background monitor, that is not a nice-to-have — it is the entire premise.

Note the convergence with the scope study, arrived at independently:
- `Autorange` ≈ the scope's `AUTOSET` ≈ proposed `WATTS/DIV` auto-stepping
- `REL / Δ` ≈ the scope's `AC coupling` ≈ "show me draw above idle baseline"
- `Peak MIN/MAX` ≈ the scope's `trigger` ≈ "catch the event I wasn't watching"

Three different instrument families independently invented the same three ideas. That is
strong evidence they are correct, not stylistic.

## 5. True RMS, or: say what you actually measured

Cheap meters rectify and average, then scale the result assuming a sine wave. On any other
waveform they are simply wrong. **True-RMS** meters compute the real heating-equivalent value.

The relevant discipline isn't the math — it's that the meter's face *tells you which one it
is*. "True RMS" is printed on the instrument because the measurement method changes what the
number means, and hiding that would be a lie of omission.

PwrMon has exactly this situation: on battery, watts are measured; on AC, watts are a learned
estimate. The app already distinguishes these (`IsSystemEstimate`) — a multimeter would say so
**on the face of the instrument**, permanently, not in a tooltip.

## 6. The analog inheritance: mirror scales and honest reading

The Simpson 260 — the archetypal VOM — has a **mirrored strip** running along its scale. You
line the needle up with its own reflection before reading, which guarantees your eye is
perpendicular and removes parallax error.

Think about what that is: the instrument **admits its own reading is error-prone**, and ships
a physical tool to correct for it. It doesn't hide the problem or assume the user is
infallible. It also carries multiple stacked arcs (DC volts, AC volts, ohms), with the ohms
scale *reversed and non-linear* because the physics demands it — the scale is shaped to the
measurement, not forced into uniformity.

## 7. What this means for PwrMon

### 7.1 The uncomfortable one: significant digits

PwrMon displays `+80.4 W` and `37.3 %`. But we established during the 2026-07-16 incident that
this machine's fuel gauge:

- reports **quantized** rate values,
- refreshes only every **~15–30 s**,
- and under fault conditions reports the *sign* wrong entirely.

A multimeter would never print a tenths digit off that data. The honest presentations are
`80 W`, or `80.4 W` with an explicit stability/staleness indicator, or a `+ counts`-style
tolerance stated somewhere permanent.

**Recommendation:** derive displayed precision from the source's actual resolution rather than
from `double` formatting. Where the underlying reading is quantized and stale, show fewer
digits — or show the digit but mark it as unsettled. This is the single most multimeter-ish
change available to us, and it costs almost nothing.

### 7.2 Bargraph under the hero readout

Big number for the value, fast horizontal bar for the behaviour, updating at full sample rate
while the digits update on a slower, readable cadence. Solves the "numbers flicker too fast to
read" problem without throwing away responsiveness.

### 7.3 Adopt the observer modes

`MIN/MAX` (already half-present as session stats — formalise it), `HOLD` (freeze the readout),
`REL` (draw above learned idle baseline). All three are small, and all three make the app
useful while you are *not* looking at it.

### 7.4 The beeper

A DMM beeps for continuity so you never look away from your probes. PwrMon's equivalent:
an optional audible alert on a threshold — the "you are on battery and pulling 80 W" case, or
the adapter-assist condition. Off by default, obviously.

### 7.5 Say which mode the number is in

Measured vs estimated should be visible **on the readout itself**, permanently — the way
"True RMS" is printed on a meter's face. Not hover text.

### 7.6 Avoid

- Skeuomorphic LCD bezels, fake glass, drop-shadowed 7-segment kitsch.
- A rotary function dial rendered on screen (same objection as scope knobs — a mouse is not a
  rotary switch).
- Displaying more digits because there is room for them. Space is not a licence.

---

## 8. Open questions

1. What *is* the real resolution of our battery rate readings? Measurable: log raw WMI values
   and find the smallest non-zero delta. That number should drive the display format.
2. Should digits and bargraph update at different rates (readable digits, responsive bar), or
   is that too clever?
3. Does `HOLD` make sense for a monitor that is already recording everything to CSV, or is
   the chart's PAUSED state already this feature?
4. Is there a defensible `± (% + counts)` figure we could publish for our own estimates?

---

## Sources

- [Electronics Notes — DMM accuracy, resolution & counts](https://www.electronics-notes.com/articles/test-methods/meters/dmm-digital-multimeter-accuracy-resolution.php)
- [Fluke — Why digital multimeter accuracy and precision matter](https://www.fluke.com/en-us/learn/blog/digital-multimeters/accuracy-precision)
- [Fluke — The dials, buttons, symbols and display of a digital multimeter](https://www.fluke.com/en-us/learn/blog/digital-multimeters/multimeter-dial-button-jacks-display)
- [Fluke — 179 True-RMS DMM with analog bargraph](https://www.fluke.com/en-us/product/electrical-testing/digital-multimeters/fluke-179)
- [Fluke — 287/289 True-RMS DMM users manual (PDF)](https://media.fluke.com/f640860c-6f58-4c4c-8096-b10800c17e13_original%20file.pdf)
- [Tektronix — Understanding handheld DMM specifications](https://www.tek.com/en/documents/whitepaper/understanding-handheld-dmm-specifications)
- [Automation Forum — What do digits & counts mean in a multimeter](https://automationforum.co/what-do-digits-counts-mean-in-multimeter/)
- [Simpson Electric — 260 Series 8 VOM instruction manual (PDF)](https://simpsonelectric.com/wp-content/uploads/File/260-8man.pdf)
- [Engineer Fix — How to use a Simpson analog multimeter](https://engineerfix.com/how-to-use-a-simpson-analog-multimeter/)
- [Library of Congress — Simpson Model 260 Volt-Ohm-Milliammeter (PDF)](https://lcweb2.loc.gov/master/mbrs/recording_preservation/manuals/Simpson%20Model%20260%20Volt-Ohm-Milliammeter.pdf)
