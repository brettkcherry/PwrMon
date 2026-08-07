# PwrMon — Licensing & Market Brief

**Written 2026-08-07, against the tree at `db0000e`.** Offline-readable; no links need to
resolve for this to be useful. Companion to the instrument studies in this folder.

> **Not legal advice.** I'm summarising how these licenses are generally understood and how
> they interact with the dependencies actually in this binary. Before you take money for
> this, or before you relicense anything you've already published, get a lawyer to read the
> five paragraphs that matter. Everything marked **[verify]** is from memory and should be
> checked against a live source before you act on it — prices, product statuses and store
> terms move.

---

## 0. The one thing to read if you read nothing else

**The repo is still private. Nobody has ever received a copy of PwrMon under the MIT
license.** Zero stars, zero forks, no Release, no binary, no outside contributor — 34
commits, all yours, under one identity.

That means the license is currently **unexercised**, and changing it costs you *nothing*: no
consent to gather, no code to rewrite, no fork to worry about. You edit one file.

This window closes the moment you push the visibility toggle. It never re-opens on the same
terms. So the licensing decision isn't a "some day" question — **it's a this-week question,
and it's the cheapest decision you will ever make on this project.** Everything below is in
service of making it once, deliberately, before launch.

---

# PART ONE — What it would take to change the license

## 1.1 Who owns PwrMon

You do, entirely.

| Check | Status |
|---|---|
| Contributors | 1 — `BrettCherry <22756529+brettkcherry@users.noreply.github.com>`, all 34 commits |
| Outside PRs merged | none |
| CLA / DCO in place | none needed today |
| Copyright assigned anywhere | no |
| Employer IP claim | **you need to answer this one.** If any of this was written on employer hardware/time, or you have an IP-assignment clause in an employment contract, that clause outranks your LICENSE file. Worth five minutes with your contract. |

Sole ownership is the thing that makes relicensing trivial. Every additional copyright holder
you accumulate is a person whose permission you'd need — or whose code you'd have to rip out
and rewrite — to change the terms later.

**Corollary:** the day the repo goes public, the first merged PR starts eroding this. See
§1.5.

## 1.2 What MIT actually locks in (and what it doesn't)

MIT is a **per-copy, perpetual, irrevocable grant**. The mental model that matters:

- You can change the license on **future versions** at any time, unilaterally, because you own
  the copyright. There is no "you can't take it back" at the project level.
- What you *can't* do is un-grant a copy someone already has. Anyone who received v1.0 under
  MIT keeps MIT rights **to v1.0 forever** — including the right to fork it, modify it,
  rebrand it, and sell it, with no obligation to you beyond keeping your copyright line in
  the notice file.
- So the real cost of a later relicense isn't legal, it's **social and practical**: you'd be
  abandoning a community-visible commit history at a known fork point, and if the project has
  any traction someone will maintain that fork out of spite or principle. This is exactly
  what happened to a string of well-known projects that went source-available after building
  an open community.

**Cost curve for changing the license:**

| When | What it costs |
|---|---|
| Now (repo private, 0 users) | One file edit. Genuinely zero. |
| After public repo, before any PR | One file edit + a CHANGELOG line. Someone could fork the last MIT commit, but there'd be nothing to fork *to* — no users. Near-zero. |
| After ~10 contributors / real users | Every contributor must agree, or their code comes out. A viable MIT fork exists with users on it. High. |
| After a Store listing + install base | As above, plus you're changing terms on people who already installed. Reputationally expensive. |

## 1.3 The dependency constraints (the real technical gate)

This is the part most people get wrong, and the good news is **your dependency stack does not
block a commercial or closed-source PwrMon.** From `THIRD-PARTY-NOTICES.md`:

| Component | License | Does it block going proprietary? |
|---|---|---|
| LibreHardwareMonitorLib, RAMSPDToolkit-NDD, DiskInfoToolkit, BlackSharp.Core | **MPL-2.0** | **No** — with a condition. See below. |
| HidSharp | Apache-2.0 | No. Keep the NOTICE, keep attribution. Carries an express patent grant, which is a small plus. |
| ScottPlot, SkiaSharp, HarfBuzzSharp, OpenTK, GLWpfControl, .NET libs, runtime | MIT | No. |
| GLFW (`glfw3.dll`) | zlib/libpng | No. Keep the notice, don't claim you wrote it. |
| PawnIO | GPL-2.0 + IOCTL exception | **Not bundled — keep it that way.** See below. |

**The MPL-2.0 condition — the one rule you must not break.** MPL-2.0 is *file-level*
copyleft, not project-level. It reaches the MPL files themselves and nothing else. Because you
consume all four as **unmodified library dependencies** (already stated in your notices, and
true), you may ship them inside a closed-source, paid product. Your obligations are:

1. Never modify an MPL-covered source file. The moment you patch a LibreHardwareMonitor file,
   **that file** must be published under MPL-2.0 — your own code stays yours, but you've
   acquired a publication duty and a maintenance headache.
2. Keep the notices and make the MPL source available (a link to upstream satisfies this;
   you already do it).
3. Impose no further restrictions on those files — so your EULA must not, e.g., forbid all
   reverse-engineering of the entire package without carving out the MPL parts.

If you ever *do* need to patch LHM: fork it publicly, patch the fork under MPL, depend on your
fork. That's clean and cheap. What's not clean is a private patch inside a paid binary.

**PawnIO.** GPL-2.0 with an IOCTL-interface exception. The exception exists precisely so that
separate programs can talk to it over its driver interface without becoming GPL. Your current
design — user-initiated download from the official source, signature-verified, never
redistributed — is the safe design *and it's the design that keeps a paid PwrMon legal.*
**If you ever monetise, do not start bundling PawnIO into the installer for convenience.**
That single "helpful" change is the one that could pull a proprietary product into GPL
territory. Write this down somewhere the future you will see it. (It is now written here.)

**Net:** no copyleft obstacle to any license you might pick, provided MPL files stay pristine
and PawnIO stays unbundled.

## 1.4 The realistic options

Ordered roughly from most-open to most-closed.

### A. Stay MIT
- **For:** maximum adoption; zero friction; every packaging channel accepts it; qualifies for
  free OSS perks (see §2.6 — this is worth real money); reads as generous, which is on-brand
  for a project whose README brags about having no telemetry.
- **Against:** grants the right to rebrand and sell. Someone could put PwrMon on the Store for
  $4.99 before you do, and be entirely within their rights. Low probability for a niche
  utility, not zero — Store spam farms do repackage GitHub apps. Your mitigation is trademark
  (§1.6) and *getting there first*, not the license.
- **Revenue compatibility:** donations, paid convenience builds, paid Store listing, open-core.
  All still work. MIT does not stop you charging money.

### B. Apache-2.0
Everything MIT gives, plus (a) an express patent grant in both directions, (b) an explicit
statement that the license grants **no trademark rights**, and (c) a requirement to state
changes in modified versions. Still OSI-approved, still universally accepted, still qualifies
for OSS perks. Costs you nothing in adoption.

**This is the cheapest strict upgrade over MIT** and the one I'd default to if you want to
stay open. The trademark clause alone is worth the swap given you're about to attach a name
and an icon to this.

### C. GPL-3.0 (or MPL-2.0, matching your biggest dependency)
- **For:** a closed-source rebrand-and-sell becomes a license violation rather than a
  permitted use. You keep the right to dual-license (see E) because you own the copyright.
- **Against:** GPL-3.0 and the Microsoft Store have a genuine, long-documented friction — the
  Store's distribution terms and update model sit badly against GPLv3's anti-Tivoization and
  "no additional restrictions" clauses. **[verify]** Several projects have hit this. Since a
  Store listing is on your roadmap (HANDOFF §6), copyleft and your distribution plan are
  partly in conflict.
- MPL-2.0 for your own code is the interesting middle: file-level copyleft, no Store friction,
  and it matches LibreHardwareMonitor. It stops wholesale closed forks of *your files* while
  letting anyone embed the app in something bigger.

### D. Source-available / non-commercial (PolyForm Noncommercial, BSL 1.1, Elastic-style)
Code stays readable on GitHub; commercial use is forbidden or time-delayed. BSL 1.1 has a
built-in change date after which it converts to an open license — an elegant compromise if you
want a commercial runway without permanently closing.
- **Against:** these are **not open source** by OSI definition, and the audiences you most want
  (Hacker News, r/linux-adjacent Windows power users, the LibreHardwareMonitor crowd) will say
  so loudly and in the first three comments. You also lose: free OSS code signing, "open
  source" framing on winget/Store, and the goodwill that makes strangers run an unsigned exe
  from a stranger — **which is the exact thing you currently need most** (§2.3).
- Right answer for a product with paying customers. Wrong answer for a product whose immediate
  need is hardware-coverage volunteers.

### E. Dual license (open + commercial)
Ship GPL/MPL publicly, sell a proprietary license to anyone who wants to embed PwrMon without
copyleft obligations. Costs nothing to set up, generates nothing until someone asks — but the
asking is free money when it happens. Only viable if you're the sole copyright holder, which
loops back to §1.5. **If you go copyleft, take this option; it's pure upside.**

### F. Proprietary freeware / paid
Source closed or partial, binary free or paid. Maximum control, minimum community, and it
throws away the single biggest asset this project has right now, which is that the code and
docs are *unusually well written* and would be read admiringly. Hard to recommend at this
stage.

### The tension, stated plainly

> Almost every asset you need for **launch** (free code signing, winget, HN/Reddit goodwill,
> strangers testing your app on AMD hardware) requires an OSI-approved license. Almost every
> lever for **revenue** is easier without one.
>
> The resolution that costs least: **stay OSI-open, and sell convenience rather than code** —
> a signed, Store-delivered, auto-updating build, and your name. That fits the ethos in
> CONTRIBUTING.md instead of fighting it. Details in §2.7.

## 1.5 Keeping the option open after you go public

If you launch open but want to preserve the *ability* to relicense later, you need to stay the
sole copyright holder. Two mechanisms, in increasing order of friction:

- **DCO** (Developer Certificate of Origin, `Signed-off-by:` in commits) — asserts the
  contributor had the right to submit. **Does not** give you relicensing rights. Common, low
  friction, insufficient for this purpose.
- **CLA** (Contributor License Agreement) — contributors grant you a broad license (or assign
  copyright) so you can relicense the whole work later. This is what Grafana, Elastic, MongoDB
  et al. had in place *before* they changed license, which is why they could. Costs you: a
  bot, a file, and a small amount of contributor goodwill — some people won't sign one, on
  principle.

**Recommendation:** if there is any real chance you'll want to relicense or dual-license
later, add a lightweight CLA **at launch**, not after the first PR. Retrofitting one is the
same "chase everyone for consent" problem as relicensing.

If you're confident you'll stay permissive forever, skip it — MIT/Apache contributions from
others cause you no problem as long as you never want to close.

## 1.6 The trademark point (independent of license, and underrated)

**No open-source license grants rights to your name or logo.** Apache-2.0 says so explicitly;
MIT is silent but the same is generally true. This means:

- You can be fully MIT and still say: *fork the code freely, but a fork may not be called
  "PwrMon" or use the bolt icon.*
- That single sentence is most of the protection people think they need copyleft for. A
  rebranded Store clone is a licensing problem you can act on **only** through trademark.

**Cheapest version, do it at launch:** a short `TRADEMARK.md` — "PwrMon and the PwrMon icon
are unregistered trademarks of Brett Cherry. The code is [license]; the name and mark are not.
Forks must use a different name and icon." Unregistered marks carry real weight in practice
for takedown requests to GitHub and app stores. Formal registration only becomes worth the
cost if there's money flowing. **[verify]** current filing costs in your jurisdiction.

Also: HANDOFF flags **"LICENSE (MIT, 'Brett Cherry' — confirm legal name)"** as an open item.
That's still open. Whatever license you land on, the copyright line must carry the name you'd
actually enforce with. Fix this before the repo goes public — it's in every distributed copy.

## 1.7 Execution checklist — changing the license

**Now, while private (the free path):**

1. Decide the license (§1.4). Confirm your legal name on the copyright line.
2. Replace `LICENSE`. Add the SPDX id to `PwrMon.csproj` (`<PackageLicenseExpression>`), and
   to the README badge/footer if you add one.
3. Update `CONTRIBUTING.md` to state the inbound license, and add a CLA/DCO if you chose one
   (§1.5).
4. Add `TRADEMARK.md` (§1.6).
5. Confirm `THIRD-PARTY-NOTICES.md` is current — re-run `tools/list-shipped-assemblies.ps1`.
   It must be accurate on day one; it's the document a cautious sysadmin reads first.
6. If the license is anything other than MIT/Apache, add a one-paragraph plain-English
   summary at the top of the README. People bounce off legalese and assume the worst.

**Later, after publication (the expensive path) — for reference:**

1. Confirm every contributor's consent in writing, or revert/rewrite their contributions.
2. New `LICENSE`; **do not** rewrite history or edit the license text of past tagged releases.
   Those copies were granted under the old terms and pretending otherwise is the thing that
   turns a licensing change into a scandal.
3. Tag the last commit under the old license clearly (e.g. `v1.4.0-final-mit`) so honest
   forkers have an unambiguous base. This is a goodwill move that costs one tag.
4. CHANGELOG entry + a README paragraph explaining *why*, in your own voice, before anyone
   else explains it for you.
5. Per-file copyright headers if you're going commercial — currently the code has none.
6. If money is involved: an EULA and a warranty disclaimer that survives consumer-protection
   law in the jurisdictions you sell to, plus a privacy policy (the Store requires a URL for
   one regardless — HANDOFF §6 already notes this). Note that a paid product's "AS IS"
   disclaimer is weaker than a free one's in several jurisdictions. **[verify]**

---

# PART TWO — Product & market review

## 2.1 Where the product actually is

| Dimension | State |
|---|---|
| Code | ~6,600 lines C#/XAML, .NET 8 WPF, 4 direct dependencies, xUnit bench over the pure logic |
| Features | Complete and coherent for the stated scope. Nothing feels like a stub. |
| Docs | README, CHANGELOG, CONTRIBUTING, SECURITY, THIRD-PARTY-NOTICES, TESTING, ISSUES, plus four instrument studies. **Unusually strong — this is an asset, see §2.5.** |
| Hardware verification | **One machine.** Zenbook UX3404VA, i7-13700H, Iris Xe. |
| Distribution | None. No Release, no binary, no installer built (Inno not installed on this box), unsigned. |
| Users | Zero, other than you. |

The honest read: **the product is done and the project hasn't started.** Everything left is
distribution, hardware coverage, and trust — not code. That's a good position, but it means
the next month's work looks nothing like the last month's.

## 2.2 What's genuinely differentiated

Ranked by how hard it would be for a competitor to copy:

1. **Driverless CPU/iGPU watts via Windows' Energy Meter counters.** The headline. Effectively
   every tool in this category reaches RAPL through a kernel driver — usually WinRing0, which
   is on Microsoft's vulnerable-driver blocklist. You get package/cores/iGPU watts with no
   admin, no driver, no UAC prompt, unaffected by Memory Integrity. Most developers in this
   space don't appear to know this path exists. **This is the technically interesting claim
   and the thing to lead with.** Caveat: verified on Intel only.
2. **"No WinRing0" as a security posture.** Corporate-managed and HVCI-on machines can't run
   half the competition. You can. That's a segment, not just a feature.
3. **The firmware-lie story.** Capacity-trend arbitration, the "PLUGGED IN — DRAINING" hero
   state, the 83 W drain-while-reporting-charging incident. No competitor tells this story,
   and it is a *far* better hook than any feature list. See §2.5.
4. **Live-first framing.** Every competitor leads with health summaries. Your README's opening
   line is the positioning, and it's correct.
5. **Craft details that signal to the exact audience you want:** the tray icon *is* the
   wattage; the readout drops decimal places when the fuel gauge hasn't republished, rather
   than implying false precision; 13 themes ordered darkest→lightest so cycling never
   flashbangs. Measurement nerds notice precision honesty. It's a trust signal disguised as a
   UI detail.
6. Privacy: one outbound URL in the whole codebase, behind consent. Increasingly rare, easy to
   verify, easy to claim credibly *because the repo is readable.*

## 2.3 What's actually blocking

1. **The chicken-and-egg on hardware coverage.** You need users to get AMD/second-machine
   verification; you're withholding a release until you have it. That loop has to be broken
   deliberately — see §2.7 Phase 1, which is the single most actionable idea in this document.
2. **Unsigned binary → SmartScreen.** A red warning screen on first run will cost you a large
   fraction of downloads, and it lands on exactly the security-conscious audience you're
   courting. Options: build reputation slowly (free, slow, unreliable), Azure Trusted Signing
   (~$10/mo, individual validation path exists) **[verify]**, SignPath (free tier for OSS
   projects — **license-dependent**) **[verify]**, or ship via the Store and let Microsoft
   sign it ($19 one-time dev account) **[verify]**.
3. **No download exists.** Zero users today. Everything else is theoretical until this changes.
4. **Discoverability of the name.** "PwrMon" is memorable in context but hard to search — it
   collides with generic power-monitoring terminology and at least one unrelated tool
   **[verify]**. Not worth renaming over; worth knowing that SEO won't do the work and a
   written post will.
5. **Scope boundaries that cost you market:** Windows-only, laptop-only, and integrated-GPU
   focused. Gaming laptops — a large, engaged, money-spending slice of the enthusiast market —
   care most about the discrete GPU's watts. NVIDIA exposes per-GPU power draw through NVML
   with no elevation and no kernel driver **[verify]**, which is philosophically identical to
   your EMI tier. **If you want one feature that widens the market without violating the
   ethos, that's it.**

## 2.4 The competitive field

**[verify] all prices and product statuses — these are from memory.**

| Tool | Model | Where it beats you | Where you beat it |
|---|---|---|---|
| **HWiNFO** | Free personal, ~$25 Pro | Sensor breadth, everything on every chip, entrenched as the default | Needs a driver; dense engineering-tool UI; not battery/live-watts focused |
| **LibreHardwareMonitor** | Free, MPL | Breadth, community, cross-hardware | WinRing0 (blocklisted driver), general-purpose UI, not a battery tool |
| **BatteryBar / Pro** | Free / ~$8 | Taskbar presence, install base | Health-and-estimates oriented; dated; no silicon watts |
| **BatteryInfoView** (NirSoft) | Free | Trusted brand, tiny | Snapshot table, no live chart, no CPU watts. Your tonal ancestor — you are the modern version of this. |
| **Intel Power Gadget** | Free — **discontinued ~2023** | Was *the* RAPL-with-a-nice-UI tool on Windows | **It's gone.** It left a real vacuum and no obvious successor. This is the clearest market gap you're standing in. |
| **Windows battery report / Task Manager** | Free, built in | Zero install | No watts, coarse, historical not live |
| **ThrottleStop, HWMonitor, AIDA64** | Free / free / paid | Tuning and breadth | Not live-power-flow tools; mostly driver-based |
| **coconutBattery, iStat Menus** (macOS) | Freemium / ~$12 | — | Not comparable — but **proof that people pay real money for exactly this category on another platform.** Use them for price anchoring, not competition. |

**Read of the field:** the category is real, entrenched at the "everything sensor" end
(HWiNFO), stale at the "just my battery" end, and *empty* where Power Gadget used to be. Your
position — live watts, driverless, honest, beautiful, portable — is a genuine hole in the
market. The problem was never differentiation. It's distribution.

## 2.5 Your best marketing asset is already written

Three pieces of content exist in this repo, in near-publishable form, and each is stronger
than any feature list:

1. **"Windows will tell you your CPU's wattage without a kernel driver, and almost nobody
   uses it."** A technical write-up of the EMI counter path — what it is, why everyone else
   ships a blocklisted driver instead, what it does and doesn't expose. This is a
   front-page-of-Hacker-News shape of post, and it is *true and useful independent of your
   app*, which is exactly why it would travel.
2. **"My laptop drained to flat at 83 W while Windows insisted it was charging."** The
   2026-07-16 TB4 port incident, the firmware contradiction, and the capacity-trend
   arbitration that fixes it. This is the story. It's visceral, everyone with a USB-C laptop
   has half-experienced it, and it justifies the app's existence in one paragraph.
3. **The instrument studies** (MULTIMETER, PANEL-METER, SCOPE, DESIGN-REVIEW). Design-craft
   content for a different audience than the other two. Slower burn, high respect.

Publishing (1) and (2) as posts, with the repo linked at the bottom rather than pitched at the
top, is plausibly worth more than every other launch activity combined — and it works whether
or not you ever charge a cent.

## 2.6 Revenue positions, honestly sized

Set expectations first: **a niche Windows utility with no marketing budget realistically
earns from nothing to a few hundred dollars a month, and only after it has thousands of
users.** Sizes below assume the project reaches a few thousand users, which is itself an
optimistic year-one outcome. Treat revenue as validation and hardware money, not income.

| # | Position | Effort | Realistic size | Ethos cost | License needed |
|---|---|---|---|---|---|
| 1 | **GitHub Sponsors / Ko-fi** | ~1 hour | $0–50/mo | none | any |
| 2 | **Microsoft Store, paid ($3–5 one-time)** | Medium — MSIX packaging, EMI-only flavor, privacy policy | $0–300/mo | low — Store build is signed and sandboxed, which *serves* users | any; copyleft has friction **[verify]** |
| 3 | **Store free + "supporter" build** | Medium | less than #2, usually | moderate — needs some key check | any |
| 4 | **Open core** (free core, paid extras: fleet logging, long-horizon analytics, reviewer PDF reports) | High, ongoing | $0–500/mo | **high** — conflicts with "one exe, no account, zero bloat" | any; easier non-copyleft |
| 5 | **License the EMI sensor layer as a component** — extract the driverless RAPL reader as a NuGet package or a commercial SDK for other Windows devs/OEMs | Medium | Lumpy: $0 most months, then one $2–20k deal | none — it's a separate artifact | **Needs non-MIT, or dual-license, to be sellable.** Ties directly to Part One. |
| 6 | **Reputation route** — publish §2.5, let the project be the portfolio | Low | $0 direct; potentially the highest-value outcome | none | any (open helps a lot) |
| 7 | **Hardware-coverage bounties** — people fund an AMD test unit | Low | one-off, small | none | any |

**Two observations worth pausing on:**

- **#5 is the sleeper.** The driverless EMI RAPL reader is the genuinely novel engineering in
  this project, and it is *reusable by other developers* in a way the WPF app isn't. It's
  also the one revenue path whose viability depends on the licensing decision in Part One:
  MIT it and you can never sell it; MPL/GPL it and dual-licensing is free upside. If you find
  #5 at all interesting, that argues for copyleft-or-dual on at least the sensor layer, even
  if the app stays permissive. **Different licenses for different directories is entirely
  normal and worth considering.**
- **#3 and #4 fight the product.** Any paid tier needs a way to tell a payer from a
  non-payer, and every mechanism for that (accounts, key servers, activation checks) is
  something the README currently brags about not having. The least-bad version is an
  offline-verified signed key file — no network, no account, honor-system-adjacent. If that
  sounds unappealing, that's a real signal that #1/#2/#6 are your lane.

## 2.7 A sequenced plan

Cheapest and most reversible first. Each phase is independently worthwhile if you stop there.

**Phase 0 — before the repo goes public (days)**
- Decide the license (Part One). Confirm the legal name on the copyright line.
- Add `TRADEMARK.md`.
- Decide CLA / no CLA.
- The two screenshots GO-PUBLIC.md is blocked on.
- Push the local backlog.

**Phase 1 — break the hardware-coverage deadlock (the key move)**
Ship **`SensorProbe` alone as the first public artifact.** It's a small console app; it
installs nothing, elevates nothing, writes nothing, and asks for no trust — so the
SmartScreen and "unsigned exe from a stranger" objections mostly evaporate. Pair it with a
post: *"I built a Windows power monitor that reads CPU watts without a kernel driver. It's
verified on exactly one laptop. Run this 200-line probe and paste the output — especially if
you have AMD."*

This converts your biggest weakness (one machine) into the actual call-to-action, which is a
much better story than hiding it. It gets you a hardware matrix, an audience, and early
credibility *before* you have to stand behind a binary. Public repo + Pages goes live
alongside it.

**Phase 2 — signed beta**
Once a handful of non-Intel dumps have landed and the AMD path either works or is honestly
documented as unsupported: portable + standalone builds, code signing sorted (SignPath's OSS
tier if you're open-licensed — a concrete financial benefit of an OSI license **[verify]**),
release notes that keep the README's honesty about what's verified.

**Phase 3 — v1.0**
GitHub Release with all three artifacts, winget manifest, the §2.5 posts published properly.

**Phase 4 — monetisation experiment, and only now**
Store listing (EMI-only, no-elevation flavor, as HANDOFF §6 already specifies), Sponsors
button, price test at $3–5 or free-with-sponsor-link. **Gate this on a real number** — e.g.
don't spend a day on Store packaging until 1,000 downloads or 5 verified non-Intel machines.

**The strategic line to hold throughout:** stay open, sell convenience and trust rather than
code, and protect the name. That's compatible with everything in CONTRIBUTING.md, keeps every
launch-phase perk available, and leaves #5 open if you scope the license by directory.

## 2.8 Decisions this brief can't make for you

1. **Is this a product or a portfolio piece?** The whole plan changes. Portfolio ⇒ Apache-2.0,
   publish the posts, take sponsors, never think about revenue again. Product ⇒ the license
   question gets sharper, the Store matters, and you'll be doing support.
2. **Will you support other people's hardware?** A public release with users means bug reports
   from machines you can't reproduce on. That's the real recurring cost of launching, and it's
   paid in evenings.
3. **Does the EMI sensor layer have a life of its own (#5)?** Only you can judge how novel it
   really is — but the answer directly constrains Part One.
4. **How much does "someone rebrands and sells it" actually bother you?** Be honest. If the
   answer is "a lot", that's an argument for copyleft + trademark, and it should be made now
   rather than resented later.

---

## Appendix — one-page summary

- **Repo is private with zero users, so the license can be changed today for free.** That is
  the whole licensing story. Decide before you push the visibility toggle.
- **You are the sole copyright holder.** Preserve that with a CLA if you might ever want to
  relicense or dual-license.
- **No dependency blocks any license choice**, provided MPL files (LibreHardwareMonitor et al.)
  stay unmodified and PawnIO is never bundled.
- **Apache-2.0 is a free strict upgrade over MIT** — patent grant plus explicit trademark
  reservation.
- **Trademark, not license, is what stops a rebranded clone.** `TRADEMARK.md` costs ten
  minutes.
- **Confirm the legal name on the copyright line** — still flagged open in HANDOFF.
- **The product is finished; the project hasn't started.** The remaining work is distribution
  and trust.
- **Real differentiation:** driverless RAPL, no WinRing0, firmware-lie detection, live-first
  framing, visible craft. The Intel Power Gadget vacuum is a real gap you fit.
- **Real blockers:** one-machine verification, unsigned binary, no download, zero users.
- **Best single move:** release `SensorProbe` alone, with a written post, and make
  "verified on one laptop" the call to action rather than the thing you're hiding from.
- **Best revenue shape:** stay open, sell convenience (signed Store build) and keep the name.
  Consider a separate license for the sensor layer if you want option #5 alive.
- **Expected revenue: small.** Do it for validation and hardware money. The reputational
  return on the two write-ups is probably worth more than all of it.
