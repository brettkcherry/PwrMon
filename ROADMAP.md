# Roadmap

What's planned but not built. Three files divide the work between them:

- **[CHANGELOG.md](CHANGELOG.md)** — what shipped.
- **[ISSUES.md](ISSUES.md)** — what's broken or can't be done, confirmed on hardware or in
  the code.
- **ROADMAP.md** (this file) — what's intended next. Speculative by definition; nothing here
  is a promise, and items get dropped when they stop earning their place.

Grouped by target release. Within a group, roughly in the order they'll be picked up.

---

## v1.6.2 — next

Tweaks to things that are already there, not new capability — hence a patch rather than a
minor bump. (v1.6.1 — the learned heavy-draw threshold and default-theme/size fix — shipped
2026-08-14; see [CHANGELOG.md](CHANGELOG.md).)

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

**Watch for.** Re-detection and tier transitions have to clear the right dismissals. If a user
dismisses at `NeedsAdmin`, then installs PawnIO and re-detects into `DriverBlocked`, they
should see that banner. Dismissal suppresses a *specific message*, not the banner mechanism.
