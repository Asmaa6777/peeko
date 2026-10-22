# Proof — Blueprint

> Working name: **Proof** ("proof you did it"). Check the name is free on GitHub / the web before release.
> A macOS menu bar app that keeps a daily list of tasks and **checks them off automatically** by reading signals from your Mac (GitHub activity, time spent focused in an app, etc.).
> Local-first, no login, no telemetry, open source.

---

## 1. Product principles

1. **Local-first.** All data stays in `~/Library/Application Support/Proof/`. No accounts, no server, no analytics. This is a feature — say so in the README.
2. **Evidence, not policing.** Copy says "Looks like you read today ✓", not "Verified". Automatic signals are proxies (see *Goodhart's Law*: once a measure becomes a target it stops being a good measure).
3. **Generic verifiers, many presets.** Don't code "Preview reading". Code "*N minutes active in app X*" and ship "Read PDFs" as a preset of it.
4. **Out of the way.** Menu bar icon + popover. No Dock icon.
5. **Packaged like a real app from v0.1.** Version numbers, icon, installer, update channel, changelog.

---

## 2. Verifier types (the core abstraction)

Every task is an instance of one verifier type plus parameters.

| Type | Meaning | v1 presets |
|---|---|---|
| `remoteCheck` | Poll an external source: did X happen today? | GitHub push today |
| `appTime` (accumulator) | Sum active minutes while a condition holds | Read PDFs (Preview, 30 min), Deep work (VS Code / Terminal, 90 min) |
| `appLimit` (constraint) | Pass while total stays under a limit; fails if exceeded | *(v1.1)* |
| `manual` | User ticks it | Gym, Water, anything custom |

### Task definition (data, not code)

Built-in presets ship as a bundled `catalog.json`. User tasks are saved the same way.

```json
{
  "id": "read-pdfs",
  "title": "Read for 30 minutes",
  "type": "appTime",
  "params": { "bundleIDs": ["com.apple.Preview"], "minutes": 30, "idleCutoffSeconds": 120 },
  "icon": "book"
}
```

```json
{
  "id": "github-push",
  "title": "Push to GitHub",
  "type": "remoteCheck",
  "params": { "source": "githubPush", "username": "" },
  "icon": "github"
}
```

Adding a new *preset* = editing JSON. Adding a new *type* = writing one Swift struct that conforms to the `Verifier` protocol.

---

## 3. How the two hard verifiers work (precise)

### 3a. `appTime` — "N active minutes in app X"

A tick runs every **10 seconds**. On each tick:

1. `NSWorkspace.shared.frontmostApplication?.bundleIdentifier` — is the target app in front? (No permission needed.)
2. Idle time: `CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: CGEventType(rawValue: ~0)!)` — seconds since last keyboard/mouse input. If greater than `idleCutoffSeconds`, don't count.
3. If both pass, add `min(now - lastTick, 10s)` to today's total.
   - **The `min(...)` cap matters:** if the Mac sleeps for 2 hours, the next tick must not add 2 hours.
4. Also listen to `NSWorkspace.willSleepNotification` / `screensDidSleepNotification` and pause the accumulator.

Known limitation (document it): "PDF open + occasional mouse movement" counts as reading. Acceptable.

### 3b. `remoteCheck: githubPush`

- `GET https://api.github.com/users/{username}/events/public`
  - Headers: `User-Agent: Proof-App`, `Accept: application/vnd.github+json`.
  - Send `If-None-Match: <etag>` from the last response. A `304 Not Modified` reply **does not count** against the rate limit.
- Pass if any event has `type == "PushEvent"` and `created_at` (UTC) falls inside **today in the user's local time zone**.
  - Example: a user in UTC+3 who pushes at 01:00 local has `created_at` = 22:00 UTC *yesterday*. Always convert with `Calendar.current`; never compare date strings.
- Poll every **10 minutes**. Unauthenticated limit is 60 requests/hour, which is fine.
- **Latency:** GitHub's docs say the events API is not real-time and events can take from seconds up to several hours to appear. The UI should say "checking…", not "not done", until end of day.
- Limitation: public repos only. *(Later: optional token stored in **Keychain**, never in JSON, to include private repos.)*

---

## 4. Architecture

Swift Package with **two targets**, so the logic is testable without the UI:

```
Proof/
├── Package.swift
├── Sources/
│   ├── ProofCore/                 # pure logic, no SwiftUI
│   │   ├── Models/                # TaskDefinition, DayRecord, TaskStatus
│   │   ├── Verifiers/             # Verifier protocol + AppTimeVerifier, GitHubPushVerifier, ManualVerifier
│   │   ├── Platform/              # FrontmostAppProvider, IdleTimeProvider (protocols + real impls)
│   │   ├── Store/                 # JSON persistence, day rollover
│   │   └── Scheduler/             # tick loop, polling intervals
│   └── ProofApp/                  # SwiftUI menu bar app
│       ├── ProofApp.swift         # @main, MenuBarExtra
│       ├── Views/                 # TodayView, CatalogView, SettingsView, OnboardingView
│       └── Notifications/         # UNUserNotificationCenter wrapper
├── Resources/
│   ├── catalog.json
│   ├── Info.plist
│   └── AppIcon.icns
├── scripts/
│   ├── build.sh                   # swift build -c release
│   ├── bundle.sh                  # assemble Proof.app
│   ├── sign.sh                    # ad-hoc or Developer ID
│   └── dmg.sh                     # create Proof-x.y.z.dmg
├── Tests/ProofCoreTests/
├── .github/workflows/release.yml
├── README.md
├── CHANGELOG.md
└── LICENSE                        # MIT
```

**Why the Platform protocols?** `FrontmostAppProvider` and `IdleTimeProvider` are protocols, so tests can inject fake values ("Preview is frontmost, idle 5s") and check the accumulator math without touching the real system. This is called *dependency injection*.

### Key types

```swift
protocol Verifier {
    var definition: TaskDefinition { get }
    func evaluate(context: VerifierContext) async -> TaskStatus
}

enum TaskStatus: Codable, Equatable {
    case pending
    case inProgress(progress: Double)   // 0.0–1.0, for appTime
    case done(at: Date)
    case failed                         // for appLimit
}
```

### Persistence

- `~/Library/Application Support/Proof/tasks.json`: which tasks are enabled, plus their params.
- `~/Library/Application Support/Proof/days/2026-10-10.json`: one file per day with status and accumulated seconds. This makes history and streaks trivial and the data easy to export.
- Include `"schemaVersion": 1` in every file, so future versions can migrate old data.

### Day rollover

- Store "today" as a `yyyy-MM-dd` key built from `Calendar.current`.
- On every tick, if the key changed, close yesterday's file and start a new one.
- Also observe `NSNotification.Name.NSCalendarDayChanged`.

### Notifications

- Notify **once** per task when it flips to `done`: "✅ Push to GitHub — 2 of 4 done today".
- Optional evening nudge (default 9 PM, configurable): "2 tasks left today".
- `UNUserNotificationCenter` **only works from a real `.app` bundle with a `CFBundleIdentifier`**. Running the raw `swift run` binary will crash or silently fail, so test notifications only through `bundle.sh`.

---

## 5. UI (menu bar popover)

- **Today**: list of enabled tasks, each with an icon, title, and status (progress ring for `appTime`, "checking…" for GitHub, checkbox for manual). Header shows "2 / 4" and the streak 🔥.
- **Catalog**: every preset with a toggle, plus "+ Custom manual task" and "+ Custom app-time task" (pick an app from `/Applications`, set minutes).
- **Settings**: GitHub username, notification times, launch at login, data folder ("Reveal in Finder"), export, version number, "Check for updates".
- **Onboarding** (first launch only): one screen per permission explaining *why* (notifications, and later optional Accessibility), plus GitHub username entry. People grant permissions when the reason is clear.

Menu bar icon: an SF Symbol that fills as the day completes (e.g. `circle` → `circle.inset.filled` → `checkmark.circle.fill`).

---

## 6. Packaging & distribution (the "real app" part)

### Platform targets
- **Minimum macOS 14 (Sonoma).** `MenuBarExtra` needs 13+; 14 gives a modern SwiftUI baseline and still covers most active Macs.
- **arm64 only (Apple Silicon).** Apple announced macOS 26 Tahoe as the last release for Intel Macs. Also, building a universal binary with SwiftPM generally needs full Xcode, which you don't have. Revisit only if Intel users ask.

### The `.app` bundle (built by `scripts/bundle.sh`, no Xcode)
```
Proof.app/Contents/
├── Info.plist        # CFBundleIdentifier=com.a7med7x7.proof, CFBundleShortVersionString=0.1.0,
│                     # CFBundleVersion=1, LSUIElement=true (no Dock icon), LSMinimumSystemVersion=14.0
├── MacOS/Proof       # the compiled binary
└── Resources/        # AppIcon.icns, catalog.json
```
Icon: design a 1024×1024 PNG, generate the sizes with `sips`, and convert with `iconutil -c icns`. Both tools ship with macOS.

### Code signing — two stages

| Stage | Cost | What users see |
|---|---|---|
| **Ad-hoc signed** (`codesign --force --deep --sign - Proof.app`) | Free | On first open, macOS blocks the app. Since macOS 15 Sequoia, the old right-click → Open bypass is gone: users must go to **System Settings → Privacy & Security → "Open Anyway"**. The README needs a short GIF of this. |
| **Developer ID + notarization** (`notarytool`) | Apple Developer Program, $99/year | Opens normally with no warning. Required if you want non-technical users. |

**My bias:** launch v0.x ad-hoc signed to friends and developers, who can handle the "Open Anyway" step. Pay the $99 once real strangers start downloading it, since an unsigned app loses most non-developer users at the first warning. Also offer **build from source** (`git clone && make install`): an app built locally isn't quarantined, so it skips Gatekeeper entirely.

### Installer
- `scripts/dmg.sh` uses `hdiutil create` to make `Proof-0.1.0.dmg` containing `Proof.app` plus a shortcut to `/Applications`.
- Publish the `.dmg` on **GitHub Releases** along with SHA-256 checksums.

### Auto-updates — Sparkle
- **Sparkle** (sparkle-project.org) is the standard update framework for Mac apps distributed outside the App Store, and it's available as a Swift Package.
- It reads an `appcast.xml` feed (host it on GitHub Pages) and verifies updates with an **EdDSA signature**, so nobody can push a fake update.
- Add it in Phase 5, not earlier. The bundle structure above already supports it.

### Launch at login
- `SMAppService.mainApp.register()` (macOS 13+). It works best when the app lives in `/Applications`.
- On by default: the first launch registers it once (a `UserDefaults` flag records that), with no prompt. Turning it off in Settings sticks.

### Versioning
- **SemVer** (`MAJOR.MINOR.PATCH`). Bump `CFBundleShortVersionString` every release, and increment `CFBundleVersion` every build.
- Keep `CHANGELOG.md` in the "Keep a Changelog" format.
- Tag `v0.1.0` and let CI build the release.

### CI — `.github/workflows/release.yml`
- Trigger: tag push `v*`.
- Runner: `macos-latest`. GitHub's macOS runners include Xcode, which also covers anything Command Line Tools can't do, such as running XCTest reliably.
- Steps: `swift test` → `build.sh` → `bundle.sh` → `sign.sh` → `dmg.sh` → upload the DMG and checksum to the GitHub Release.
- Later: add notarization using secrets (the Developer ID cert as base64, plus an app-specific password).

---

## 7. Why not the Mac App Store?
App Store apps must run in the **App Sandbox** (a security container). Reading another app's window titles needs Accessibility access, which the sandbox prevents, and future shell-command verifiers would be blocked outright. Direct distribution keeps the whole verifier roadmap possible. Frontmost-app and idle detection might pass review, but don't design around that.

---

## 8. Build phases (one Claude Code session each)

Each phase ends with something you can run. Paste one phase at a time.

### Phase 1 — Skeleton + bundle pipeline
**Do:**
- `Package.swift` with targets `ProofCore` (library) and `ProofApp` (executable), platform `.macOS(.v14)`.
- A `MenuBarExtra` showing a static "Hello, Proof".
- `Resources/Info.plist` as specified in §6, with `LSUIElement=true`.
- `scripts/build.sh`, `scripts/bundle.sh`, and `scripts/sign.sh` (ad-hoc), plus a `Makefile` with `make app` and `make run`.

**Done when:** `make run` opens `Proof.app`, a menu bar icon appears, and there's no Dock icon.
**Do not touch:** nothing exists yet. Do NOT add Sparkle, notifications, or any verifier.

### Phase 2 — Core models, store, manual tasks
**Do:**
- `TaskDefinition`, `TaskStatus`, and `DayRecord` (Codable, with `schemaVersion`).
- A JSON store in Application Support, with day rollover (§4).
- `catalog.json` with the presets from §2. `ManualVerifier`.
- TodayView (list + tick) and CatalogView (toggles).

**Done when:** you can enable "Gym", tick it, quit, relaunch, and it's still ticked. Tomorrow it resets.
**Do not touch:** `scripts/`, `Info.plist`, `Package.swift` platforms. Additive changes only.

### Phase 3 — `appTime` verifier + tests
**Do:**
- `FrontmostAppProvider` and `IdleTimeProvider` protocols plus real implementations (§3a).
- A Scheduler that ticks every 10s, with the `min(delta, 10s)` cap and pause on sleep.
- `AppTimeVerifier` and a progress ring in the UI.
- Unit tests with fake providers covering: counts when frontmost and active; doesn't count when idle; sleep gap is capped; rollover at midnight.

**Done when:** keeping Preview in front for the configured minutes flips the task to done.
**Do not touch:** the store file format (only add fields), `scripts/`, ManualVerifier.

### Phase 4 — GitHub verifier + notifications + settings
**Do:**
- `GitHubPushVerifier` (§3b) with ETag caching, local-timezone day boundaries, and a 10-min poll.
- SettingsView (username, evening nudge time, launch at login via SMAppService).
- A notifications wrapper: a one-time "done" alert per task, plus the evening nudge.
- OnboardingView on first launch.

**Done when:** pushing to a public repo turns the task green within one poll after GitHub shows the event, and a notification appears (tested via the `.app`, not `swift run`).
**Do not touch:** AppTimeVerifier logic and tests, store schema (additive only), `scripts/`.

### Phase 5 — Ship v0.1.0
**Do:**
- App icon (`.icns`), `scripts/dmg.sh`, and the GitHub Actions release workflow (§6).
- README with screenshots, a privacy statement, the "Open Anyway" GIF, and build-from-source steps.
- `CHANGELOG.md`, MIT `LICENSE`.
- Sparkle integration with an appcast on GitHub Pages (or postpone to v0.2).

**Done when:** pushing tag `v0.1.0` produces a downloadable DMG on GitHub Releases that installs and runs on a second Mac.
**Do not touch:** any verifier logic or the store format.

---

## 9. Roadmap after v0.1
- **v0.2:** `appLimit` constraint verifier (e.g. "< 45 min in Safari on YouTube" needs the window title, so the optional Accessibility permission); history and streak view; JSON export.
- **v0.3:** more `remoteCheck` sources (LeetCode, Anki via AnkiConnect, Time Machine via `tmutil latestbackup`); optional GitHub token in Keychain for private repos.
- **v0.4:** importable task files (`.proof.json`), which is the seed of a community catalog; Arabic localization via String Catalogs.
- **Later:** Developer ID + notarization; Homebrew distribution; an iPhone companion app (that's where HealthKit / HRV tasks would live).
