# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project state

Phase 1 (skeleton + bundle pipeline) is done; next is Phase 2. Reference material:

- [BLUEPRINT.md](BLUEPRINT.md) — the full product spec, architecture and phased build plan. It is the source of truth; read the relevant section before each phase.
- [Brand asset variations for tool/Peeko Brand.dc.html](<Brand asset variations for tool/Peeko Brand.dc.html>) — brand direction. (`support.js` is the design-canvas runtime for that file; `uploads/BLUEPRINT.md` is a copy, not a separate spec.)

**Naming:** BLUEPRINT.md uses the working name "Proof". The product is **Peeko**. Wherever the blueprint says `Proof`, use `Peeko`: `PeekoCore`, `PeekoApp`, `Peeko.app`, `~/Library/Application Support/Peeko/`, bundle ID `com.a7med7x7.peeko`, `User-Agent: Peeko-App`, `.peeko.json`, `Peeko-x.y.z.dmg`.

## What it is

A macOS menu bar app (no Dock icon) that keeps a daily task list and ticks tasks off automatically from signals on the Mac (frontmost-app time, public GitHub pushes), plus manual tasks. Local-first: no accounts, server, or telemetry. Swift + SwiftUI, built with SwiftPM and shell scripts. **No Xcode project**: the `.app` bundle is assembled by `scripts/bundle.sh`.

Targets: macOS 14+, arm64 only.

## Commands

- `make app` — release build (`scripts/build.sh`) → assemble `build/Peeko.app` (`scripts/bundle.sh`) → ad-hoc sign (`scripts/sign.sh`; set `SIGN_IDENTITY` for a real cert)
- `make run` — `make app`, kill any running Peeko, open the `.app`
- `make clean` — remove `.build/` and `build/`
- `swift build` — debug compile only
- `swift test` — no test target yet (added in Phase 3 as `Tests/PeekoCoreTests`); single test: `swift test --filter PeekoCoreTests.<TestClass>/<testMethod>`

Toolchain is Command Line Tools only (no Xcode). The `ld: warning: search path ... not found` lines during linking are harmless.

Notifications (`UNUserNotificationCenter`) only work from the bundled `.app`, never from `swift run`. Test them through `make run`.

The dev machine's menu bar is crowded: a new status item may land in macOS's `«` overflow, so "icon not visible" is not necessarily a bug.

## Architecture

Two SwiftPM targets so logic is testable without UI:

- **PeekoCore** (library, no SwiftUI): Models, Verifiers, Platform, Store, Scheduler.
- **PeekoApp** (executable): `MenuBarExtra` popover with Today / Catalog / Settings / Onboarding views, and a notifications wrapper.

Core abstraction: every task is **one verifier type + params**, defined as data. Presets ship in `Resources/catalog.json`; user tasks use the same shape. A new preset = JSON edit; a new type = one struct conforming to `Verifier` (`evaluate(context:) async -> TaskStatus`). Types: `appTime`, `remoteCheck` (`githubPush`), `manual`, and `appLimit` (v0.2). Never hardcode a specific app or task in Swift.

Invariants that are easy to get wrong:

- `appTime` ticks every 10s and adds `min(now - lastTick, 10s)`, so sleep never inflates totals. Skip ticks when idle > `idleCutoffSeconds`; pause on `willSleep` / `screensDidSleep`.
- `FrontmostAppProvider` and `IdleTimeProvider` are protocols so tests inject fakes. Keep all system calls behind them.
- GitHub: poll every 10 min with `If-None-Match` ETag (304s are free). Compare `created_at` against *today in the local time zone* via `Calendar.current`, never by string. Show "checking…", not "not done", before end of day, since the events API can lag hours.
- Persistence: `tasks.json` plus one `days/yyyy-MM-dd.json` per day; every file carries `"schemaVersion"`. Schema changes are additive only. Day key comes from `Calendar.current`; roll over on tick when the key changes and on `NSCalendarDayChanged`.
- Notify once per task on flip to done.
- Any future GitHub token goes in Keychain, never JSON.

## Working in phases

BLUEPRINT.md §8 defines Phases 1–5, each with **Do**, **Done when**, and **Do not touch** lists. Work one phase at a time, respect its "Do not touch" list, and don't pull in later-phase items early (e.g. no Sparkle, notifications or verifiers in Phase 1).

## Brand and copy

- Peeko is a small green creature hanging from the menu bar; only its eyes show in the icon. Tagline: "peeks in, ticks off".
- Tone: curious, playful, cheering, never policing. Say "Peeko saw that! Read for 30 minutes ✓" or "Looks like you read today", never "Verified".
- Three status faces, which are also the menu bar icon states: **pending → dozing** (closed eyes), **checking → looking** (open eyes), **done → beaming** (smiling eyes + mouth). This replaces the SF Symbol idea in BLUEPRINT.md §5.
- Colors: Peeko green `oklch(0.82 0.15 145)`, Night `#151515`, Blush `oklch(0.82 0.15 330)`, Chalk `#F2F2EF`.
- Type: Bricolage Grotesque (wordmark/headings, lowercase "peeko", weight 800); JetBrains Mono (labels, metadata).
