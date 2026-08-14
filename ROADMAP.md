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
- **Configurable heavy-draw threshold.** The tray icon turns red above 60 W discharge
  ([TrayService.cs:111](src/PwrMon/Services/TrayService.cs#L111)). That number is right for a
  35 W-class ultrabook and wrong for a gaming laptop that idles near it. Either make it a
  setting or derive it from the battery's design capacity.
