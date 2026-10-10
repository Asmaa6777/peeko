# Development

## Environment

| | |
|---|---|
| OS | macOS 14 Sonoma or later (minimum deployment target) |
| CPU | Apple silicon only (`arm64`) |
| Toolchain | Command Line Tools, no Xcode. Built and tested with Swift 6.4 and the macOS 27 SDK |
| Language mode | Swift 6 (strict concurrency) |

Install the tools with `xcode-select --install`, and check them with `swift --version`.

## Commands

| Command | What it does |
|---|---|
| `make run` | Builds, bundles and signs the app, quits any running copy, and opens `build/Peeko.app` |
| `make app` | Builds, bundles and signs without launching |
| `make clean` | Deletes `.build/` and `build/` |
| `swift build` | Quick debug compile, useful to catch errors |

`make app` is three scripts in a row:

1. `scripts/build.sh`: `swift build -c release --arch arm64`
2. `scripts/bundle.sh`: assembles `build/Peeko.app` from the binary, `Resources/Info.plist` and `Resources/catalog.json`
3. `scripts/sign.sh`: ad-hoc signing by default. Set `SIGN_IDENTITY="Developer ID Application: …"` to sign with a real certificate.

Always test through the `.app`, not `swift run`. A bare binary has no bundle ID, so `LSUIElement` (no Dock icon), `Bundle.main` resources and notifications won't behave as they do in the real app.

## Your data while developing

The app reads and writes the same folder whether it's a dev build or a release:

```
~/Library/Application Support/Peeko/
├── tasks.json            # enabled tasks and their params
└── days/2026-10-10.json  # one file per day: statuses and accumulated seconds
```

To start fresh, quit Peeko and run `rm -rf ~/Library/Application\ Support/Peeko`.

If a file won't decode, the app renames it to `*.corrupt-<timestamp>` and starts empty rather than overwriting it.

## Toolchain quirks

- **Use `@ViewState`, not `@State`.** In the macOS 27 SDK, `@State` is a macro whose plugin ships only with Xcode, so it fails to compile with Command Line Tools. `Sources/PeekoApp/ViewState.swift` aliases the plain property wrapper. `@Observable` is fine because its plugin ships with the toolchain.
- **No `swift test` yet.** Both XCTest and Swift Testing need macro plugins or frameworks from Xcode. Logic in `PeekoCore` is checked through a small executable instead. GitHub Actions runners do have Xcode, so the release workflow can run real tests.
- **`ld: warning: search path ... not found`** shows up during linking. It's harmless.

## Seeing the app

Peeko has no Dock icon and no window; it's only the eyes in the menu bar. On a crowded menu bar, macOS can tuck new icons behind the `«` overflow, so check there before assuming the app didn't start. `pgrep -lx Peeko` tells you whether it's running.
