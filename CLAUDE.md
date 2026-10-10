# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Peeko is a local-first macOS menu bar app (Swift 6, SwiftUI, SwiftPM, **no Xcode**) that keeps a daily task list and ticks tasks off from signals on the Mac.

## Where things are documented

- [docs/blueprint.md](docs/blueprint.md): product spec and the phased build plan (§8). It's the source of truth. Each phase has **Do / Done when / Do not touch** lists; follow them and don't pull later phases forward.
- [docs/architecture.md](docs/architecture.md): targets, verifier model, persistence, day rollover, and the app layer.
- [docs/development.md](docs/development.md): environment, commands, data folder, and toolchain quirks.
- [docs/brand/Peeko Brand.dc.html](<docs/brand/Peeko Brand.dc.html>): character, colors, and the three status faces.
- `README.md` is public-facing. Keep it short, plain and human. Put dev details in `docs/`, not the README.

**Status:** Phases 1–2 are done. Next is Phase 3 (`appTime` verifier).

**Naming:** the blueprint's working name was "Proof". Use **Peeko** everywhere (`PeekoCore`, `Peeko.app`, `com.a7med7x7.peeko`, `~/Library/Application Support/Peeko/`, `User-Agent: Peeko-App`, `Peeko-x.y.z.dmg`).

## Commands

- `make run`: build, bundle and ad-hoc sign `build/Peeko.app`, then relaunch it. Always verify through the `.app`, never `swift run`.
- `make app` / `make clean` / `swift build` (quick compile check)
- No `swift test`: XCTest and Swift Testing both need Xcode. Check `PeekoCore` logic with a small executable (a package depending on this one by path, or a check target).

## Must-knows

- **Write `@ViewState`, never `@State`.** The macOS 27 SDK's `@State` macro plugin isn't in Command Line Tools ([ViewState.swift](Sources/PeekoApp/ViewState.swift)). `@Observable` works fine.
- Tasks are data: a preset is a JSON edit in `Resources/catalog.json`; a new type is one `Verifier` struct plus a case in `makeVerifier(for:)`. Never hardcode a specific app or task.
- Files on disk carry `schemaVersion`; schema changes are additive only.
- Day keys come from `Calendar.current` (local time), never date strings or UTC.
- `appTime`: tick every 10s and add `min(now - lastTick, 10s)` so sleep never inflates totals; skip ticks when idle > `idleCutoffSeconds`; pause on `willSleep` / `screensDidSleep`. Keep system calls behind `FrontmostAppProvider` / `IdleTimeProvider` protocols so fakes can be injected.
- GitHub: ETag + 10-min poll, compare `created_at` to the local day, show "checking…" until end of day. Any token goes in Keychain.
- Notify once per task on flip to done. Notifications only work from the bundled `.app`.
- The popover content has a fixed height (`Theme.contentHeight`). A resizing `MenuBarExtra` window drifts off the menu bar.
- A new menu bar icon may hide in macOS's `«` overflow on this machine; that's not a bug.

## Brand and copy

- Tone: curious, cheering, never policing. Write "Peeko saw that! Read for 30 minutes ✓", never "Verified".
- Faces: pending/failed → dozing, inProgress → looking, done → beaming. The menu bar icon is an eyes-only template image.
- Colors (in `Theme`): Peeko green `oklch(0.82 0.15 145)` ≈ `#81DD86`, Night `#151515`, Chalk `#F2F2EF`, Blush `oklch(0.82 0.15 330)`. The brand fonts (Bricolage Grotesque, JetBrains Mono) aren't bundled yet; the UI uses SF Rounded and SF Mono for now.
