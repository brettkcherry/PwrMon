# Commercial licensing

PwrMon is dual-licensed.

**Copyright © 2026 Brett Cherry. All rights reserved.**

## The open license

The public license is **GNU GPL-3.0** — the full text is in [LICENSE](LICENSE). Under it you
may use, study, modify and redistribute PwrMon freely, including commercially, provided that
anything you distribute which incorporates it is also released under GPL-3.0, with source.

For the overwhelming majority of people — anyone running PwrMon, reading it, forking it,
packaging it, or contributing to it — **that license is the whole story and nothing here
applies to you.**

## The commercial license

If you want to incorporate PwrMon, or any part of it, into a product you distribute
**without** releasing that product's source under GPL-3.0, you need a separate license. That
is available directly from the copyright holder.

Typical reasons people need one:

- Embedding PwrMon's telemetry into a closed-source commercial application.
- Shipping it inside an OEM utility or a preinstalled system tool.
- Reusing the **driverless RAPL / Energy Meter sensor layer** — reading CPU and iGPU watts
  with no kernel driver and no elevation — inside proprietary software.

Terms are negotiated per case; there's no published price list. Open an issue titled
`[Licensing]` with a rough description of what you want to build, or use the contact route in
[SECURITY.md](SECURITY.md) if you'd rather it not be public to start with.

## What the commercial license does not cover

The **PwrMon name and icon** are trademarks and are handled separately — see
[TRADEMARK.md](TRADEMARK.md). A commercial code license does not grant the right to ship your
product under the PwrMon name.

Third-party components keep their own terms regardless of which license you take PwrMon
under; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Notably PawnIO is **not** bundled
with PwrMon and is downloaded by the user from its own source — that separation is deliberate
and a commercial licensee must preserve it.

---

> **Note:** this document describes the licensing arrangement; it is not itself the commercial
> agreement. A commercial license is a separate, written agreement negotiated per case.
