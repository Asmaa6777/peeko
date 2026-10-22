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
| `make install` | Builds, then replaces `/Applications/Peeko.app` and opens it |
| `make check` | Runs the logic checks in `Checks/` (stand-in for unit tests, see below) |
| `make downloads` | Prints download counts for each release file (Mac and Windows), from GitHub. Set `GITHUB_TOKEN` if you hit the rate limit |
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
├── settings.json         # GitHub username, evening reminder, onboarding done
├── days/2026-10-10.json  # one file per day: statuses, accumulated seconds, activity stretches
└── words/2026-10-10.json # starting word counts for notes-folder tasks
```

To start fresh, quit Peeko and run `rm -rf ~/Library/Application\ Support/Peeko`.

If a file won't decode, the app renames it to `*.corrupt-<timestamp>` and starts empty rather than overwriting it.

## CI

`.github/workflows/ci.yml` runs on every push to `main` and every pull request, on GitHub's `macos-latest` runner: `swift build`, then `make check`, then `make app`. GitHub's runner uses its own Xcode and SDK, which can differ from the Command Line Tools here, so a red CI run after a green local build usually means a toolchain difference, not a logic bug. Releases (`release.yml`) and the website (`pages.yml`) are separate workflows.

## Toolchain quirks

- **Use `@ViewState`, not `@State`.** In the macOS 27 SDK, `@State` is a macro whose plugin ships only with Xcode, so it fails to compile with Command Line Tools. `Sources/PeekoApp/ViewState.swift` aliases the plain property wrapper. `@Observable` is fine because its plugin ships with the toolchain.
- **No `swift test`.** Both XCTest and Swift Testing need macro plugins or frameworks from Xcode. Instead, `Checks/main.swift` is a small executable target (`PeekoChecks`) that drives `PeekoCore` with fake providers and prints ✓/✗. `make check` exits non-zero if anything fails. Add new cases there. If Xcode becomes available, these port directly to a test target.
- **`ld: warning: search path ... not found`** shows up during linking. It's harmless.

## Network and firewalls

The GitHub check is the only network call. If you run an outbound firewall like LuLu, it blocks new binaries until you allow them, including a freshly built `Peeko.app` or a scratch test executable. Symptom: URLSession times out while `curl` to the same URL works. Allow Peeko in the firewall.

To see what GitHub returns for a user:

```sh
curl -sI -H 'User-Agent: Peeko-App' https://api.github.com/users/<name>/events/public
```

## Logs and notifications

Peeko logs under the subsystem `com.a7med7x7.peeko` (category `notifications` for permission and posting). Use the full path, because in zsh `log` is a built-in shell command:

```sh
/usr/bin/log show --last 10m --info --style compact --predicate 'subsystem == "com.a7med7x7.peeko"'
```

If a notification "didn't show up", check whether macOS held it back:

```sh
/usr/bin/log show --last 30m --style compact --predicate 'process == "NotificationCenter"' | grep a7med7x7
```

`muted by DND suppression` means a Focus was on. The notification is in Notification Center, and Peeko can be added to that Focus's allowed apps.

## Seeing the app

Peeko has no Dock icon and no window; it's only the eyes in the menu bar. On a crowded menu bar, macOS can tuck new icons behind the `«` overflow, so check there before assuming the app didn't start. `pgrep -lx Peeko` tells you whether it's running.
