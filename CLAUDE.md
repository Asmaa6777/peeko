# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Peeko is a local-first macOS menu bar app (Swift 6, SwiftUI, SwiftPM, **no Xcode**) that keeps a daily task list and ticks tasks off from signals on the Mac.

## Where things are documented

- [docs/blueprint.md](docs/blueprint.md): product spec and the phased build plan (§8). It's the source of truth. Each phase has **Do / Done when / Do not touch** lists; follow them and don't pull later phases forward.
- [docs/architecture.md](docs/architecture.md): targets, verifier model, persistence, day rollover, and the app layer.
- [docs/development.md](docs/development.md): environment, commands, data folder, and toolchain quirks.
- [docs/releasing.md](docs/releasing.md): icon, DMG, versioning, the tag-driven release workflow, and signing.
- [docs/brand/Peeko Brand.dc.html](<docs/brand/Peeko Brand.dc.html>): character, colors, and the three status faces.
- `site/` is the website at https://peeko.stack42.tech (GitHub Pages via `.github/workflows/pages.yml`, which copies in `install.sh` and `docs/assets/` images). Preview with `make site` (port 8642). It mirrors the app's look; keep its copy and the README in sync.
- `README.md` is public-facing. Keep it short, plain and human. Put dev details in `docs/`, not the README.

**Status:** v0.2.0 (Phases 1–5, break reminders, sleep/wake/writing tasks, history and streaks, export). Next on the roadmap: Sparkle auto-updates, appLimit; see the blueprint (§9).

**Naming:** the blueprint's working name was "Proof". Use **Peeko** everywhere (`PeekoCore`, `Peeko.app`, `com.a7med7x7.peeko`, `~/Library/Application Support/Peeko/`, `User-Agent: Peeko-App`, `Peeko-x.y.z.dmg`).

## Commands

- `make run`: build, bundle and ad-hoc sign `build/Peeko.app`, then relaunch it. Always verify through the `.app`, never `swift run`.
- `make check`: runs `Checks/main.swift` (`PeekoChecks` target), the stand-in for unit tests, since XCTest and Swift Testing both need Xcode. Add a `check(...)` case there for new core logic; it exits non-zero on failure.
- `make dmg`: `build/Peeko-<version>.dmg` + `.sha256`. `make icon` regenerates `Resources/AppIcon.icns` from `scripts/make-icon.swift`.
- `make install`: build and copy into `/Applications`. `install.sh` is the public one-liner that installs the latest GitHub release.
- `make app` / `make clean` / `swift build` (quick compile check)
- Release = bump `CFBundleShortVersionString` in `Resources/Info.plist`, add the matching `CHANGELOG.md` section, push tag `vX.Y.Z`. CI refuses mismatches.

## Must-knows

- **Write `@ViewState`, never `@State`.** The macOS 27 SDK's `@State` macro plugin isn't in Command Line Tools ([ViewState.swift](Sources/PeekoApp/ViewState.swift)). `@Observable` works fine.
- Tasks are data: a preset is a JSON edit in `Resources/catalog.json`; a new type is one `Verifier` struct plus a case in `makeVerifier(for:)`. Never hardcode a specific app or task.
- Files on disk carry `schemaVersion`; schema changes are additive only.
- Day keys come from `Calendar.current` (local time), never date strings or UTC.
- `appTime`: tick every 10s and add `min(now - lastTick, 10s)` so sleep never inflates totals; skip ticks when idle > `idleCutoffSeconds`; pause on `willSleep` / `screensDidSleep`. Keep system calls behind `FrontmostAppProvider` / `IdleTimeProvider` protocols so fakes can be injected.
- GitHub: ETag + 10-min poll (unauthenticated 304s still count against the 60/hour limit), compare `created_at` to the local day, show "checking…" until end of day. Any token goes in Keychain.
- `makeVerifier(for:environment:)` needs a `VerifierEnvironment` (GitHub client, username, calendar). Use `isVerifiable(_:)` for "is it supported?".
- This machine runs the LuLu firewall: a new binary's network calls time out until it's allowed. If `curl` works but URLSession doesn't, that's the cause.
- Notify once per task when an *automatic* task flips to done (`Scheduler.onAutoComplete`); never for manual taps. Notifications only work from the bundled `.app`.
- The popover content has a fixed height (`Theme.contentHeight`). A resizing `MenuBarExtra` window drifts off the menu bar.
- A new menu bar icon may hide in macOS's `«` overflow on this machine; that's not a bug.
- Use `/usr/bin/log` (zsh shadows `log`). Missing banners are usually a Focus: look for `muted by DND suppression` in the NotificationCenter logs before debugging code.

## Brand and copy

- Tone: curious, cheering, never policing. Write "Peeko saw that! Read for 30 minutes ✓", never "Verified".
- Faces: pending/failed → dozing, inProgress → looking, done → beaming. The menu bar icon is an eyes-only template image.
- The tab users see is **Explore**; in code the presets are still `catalog.json` / `CatalogView`. Each `TaskCategory` (how Peeko checks a task) has a color on icons and chips only; status (rings, ticks, faces) stays green.
- Colors (in `Theme`): Peeko green `oklch(0.82 0.15 145)` ≈ `#81DD86`, Night `#151515`, Chalk `#F2F2EF`, Blush `oklch(0.82 0.15 330)`. The brand fonts (Bricolage Grotesque, JetBrains Mono) aren't bundled yet; the UI uses SF Rounded and SF Mono for now.
