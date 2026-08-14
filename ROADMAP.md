# Roadmap

What's planned but not built. Three files divide the work between them:

- **[CHANGELOG.md](CHANGELOG.md)** — what shipped.
- **[ISSUES.md](ISSUES.md)** — what's broken or can't be done, confirmed on hardware or in
  the code.
- **ROADMAP.md** (this file) — what's intended next. Speculative by definition; nothing here
  is a promise, and items get dropped when they stop earning their place.

Grouped by target release. Within a group, roughly in the order they'll be picked up.

---

## v1.7.0 — next

### Dismissible sensor-tier banner

**What's there now.** When PwrMon lands in a tier below Full, a banner sits across the top of
the dashboard offering the upgrade — "Restart as admin", "Get PawnIO", "Re-detect" depending
on which tier ([MainWindow.xaml.cs:640–682](src/PwrMon/Views/MainWindow.xaml.cs#L640)). There
is no way to close it. Someone who has read it, decided they're happy without admin, and just
wants their dashboard back has to keep looking at it forever.

**What to build.**

1. A dismiss control on the banner — an `×` at the right end rather than a third button, so it
   reads as "close this" instead of a fourth option to weigh.
2. Dismissal persists in `settings.json`, keyed by tier. Dismissing the `NeedsAdmin` banner
   should not also suppress a future `DriverBlocked` banner — those say different things and
   the second one is news.
3. The status bar keeps carrying the tier, which it already does via `StatusTier`
   ([MainWindow.xaml:323](src/PwrMon/Views/MainWindow.xaml#L323)) — currently
   "🔒 CPU/iGPU watts need admin". After a dismissal that line becomes the only signal, so it
   has to be enough on its own.
4. Make the tier text end in an underlined, clickable word that brings the banner back with
   its button. Something like *🔒 CPU/iGPU watts need admin — **fix***. Cursor to a hand on
   hover so it reads as interactive rather than decorative.

**Why it's worth doing.** The banner is correct to appear and wrong to be permanent. A user
who has made an informed choice to stay unelevated is being nagged, and the app currently
treats "you haven't done the thing" as indistinguishable from "you don't know about the
thing". Dismissal with a persistent, clickable status line respects the first case without
hiding the information — the state is still on screen, just no longer taking a whole row.

**Watch for.** Re-detection and tier transitions have to clear the right dismissals. If a user
dismisses at `NeedsAdmin`, then installs PawnIO and re-detects into `DriverBlocked`, they
should see that banner. Dismissal suppresses a *specific message*, not the banner mechanism.

### Heavy-draw threshold that isn't one machine's number

**What's there now.** The tray icon turns from orange to red above 60 W of discharge
([TrayService.cs:111](src/PwrMon/Services/TrayService.cs#L111)). One constant, no setting, no
comment saying where it came from.

**Why it's wrong.** It fails in both directions on hardware that isn't the reference machine:

| Machine | Idle | Heavy | What 60 W means there |
|---|---|---|---|
| Tablet-class, ~28 Wh | 3–4 W | 12–15 W | Never reached. Red never fires; orange forever. |
| Zenbook UX3404VA, 70 Wh | 6–8 W | 45–55 W | About right. |
| Gaming 17", ~99 Wh | 25–35 W | 100–150 W | Permanently red. The colour stops meaning anything. |

Not "too low" — simultaneously unreachable on small machines and constantly tripped on large
ones. No single constant survives that spread.

**What the number actually was.** 60 W against this machine's 70.03 Wh design capacity is
0.86C — a draw that empties the pack in about 70 minutes. The constant was never arbitrary; it
was *"roughly an hour of runtime left"*, worked out by hand and then hard-coded as watts. The
fix is to have the app do that arithmetic instead of carrying its result.

**The change is to what red means.** Today: "a big number." Proposed: "you are about to run
out." Capacity doesn't predict what a machine *can* draw — nothing does without watching it —
but it converts any draw into the thing the user actually wants, which is time:

```
hours remaining  =  capacity (Wh)  ÷  draw (W)
red threshold W  =  fullChargeCapacityWh ÷ hoursFloor      // hoursFloor ≈ 1.2
```

**Use full-charge capacity, not design.** Runtime is governed by the pack you actually have,
so the threshold should tighten as the battery ages — the same wattage genuinely does buy less
time on a worn cell. Note this *lowers* the reference machine's threshold from ~58 W to ~47 W
(70.03 Wh design, ~20% wear ⇒ ~56 Wh actual), and that drop is the feature working, not a
regression.

**Known consequence, and it's acceptable.** On a machine efficient enough that it can never
empty its battery in 1.2 h at full tilt, red never fires. Under this definition that's
correct — that machine never enters urgency — but it does mean the tray colour is a runtime
warning, not a load gauge. If a load gauge is wanted too, that's a different signal built from
observed history, and a separate item.

**Watch for.**

- **Hysteresis.** Trip at the 1.2 h equivalent, release at ~1.4 h, or it flickers on the
  boundary.
- Compute the threshold from the static capacity figure, not from the live 30 s-smoothed
  time-to-empty, so it stays stable while the draw moves.
- Clamp to a sane band (~15–150 W) and fall back to the current 60 W when capacity is missing
  or implausible — bad firmware and UPS-backed desktops both report nonsense here.
- Machines with no battery already render white and are untouched by this.

---

## Unscheduled

Wanted, not yet assigned to a release.

- **Mini-graph "Net" series colour.** Every other series wears its history-chart colour on the
  mini graph; Net is deliberately different, coloured by charge/discharge state instead
  ([MiniGraphWindow.xaml.cs:113–130](src/PwrMon/Views/MiniGraphWindow.xaml.cs#L113)). That's
  the right call — for Net, direction is the whole signal — but the README currently claims
  all eight series keep their chart colour, which isn't true of Net. Either reword the README
  or make it an option. Reword is probably right.
- **Chart-interaction edits.** TradingView-style pan/zoom shipped; there are outstanding
  changes to how it should behave that were never written down precisely enough to build from.
  Needs specifying before it needs coding.
- **Window open/close behaviour**, from the tray and app-wide — what opens where, and what
  closing actually does. Currently inconsistent enough to be worth one deliberate pass.
- **A load gauge, as distinct from the runtime warning above.** "This draw is unusual *for
  this machine*" is a different signal from "you're about to run out", and it can't come from
  capacity — it needs the machine's own observed distribution. PwrMon already has the raw
  material (daily history CSVs, session peak draw). Open question whether it's worth having
  both, and how two signals would share one tray icon.
