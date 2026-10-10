# Architecture

Peeko is a SwiftPM package with two targets:

| Target | Kind | Contains |
|---|---|---|
| `PeekoCore` | library, no SwiftUI | models, verifiers, persistence, and later the scheduler and platform providers |
| `PeekoApp` | executable | the `MenuBarExtra` UI, the app model, and the menu bar icon |

Keeping the logic in `PeekoCore` means it can be tested without the UI or a real Mac session.

## Tasks are data

Every task is **one verifier type plus parameters**:

```json
{
  "id": "read-pdfs",
  "title": "Read for 30 minutes",
  "type": "appTime",
  "params": { "bundleIDs": ["com.apple.Preview"], "minutes": 30, "idleCutoffSeconds": 120 },
  "icon": "book"
}
```

| Type | Meaning | Status |
|---|---|---|
| `manual` | You tick it | done |
| `appTime` | Adds up active minutes while one of the given apps is in front | Phase 3 |
| `remoteCheck` | Polls an outside source (`githubPush`) | Phase 4 |
| `appLimit` | Passes while time stays under a limit | v0.2 |

Built-in presets live in `Resources/catalog.json`. Custom tasks use the same shape, with ids that start with `custom-`.

- **Adding a preset** means editing JSON. No Swift needed.
- **Adding a type** means writing one struct that conforms to `Verifier` and registering it in `makeVerifier(for:)`.

Don't hardcode a particular app or task in Swift. Make it a preset of a generic type.

### Verifiers

```swift
protocol Verifier: Sendable {
    var definition: TaskDefinition { get }
    func evaluate(context: VerifierContext) async -> TaskStatus
}

enum TaskStatus { case pending, inProgress(progress: Double), done(at: Date), failed }
```

`ManualVerifier` just returns whatever the day record says. Automatic verifiers will compute a status and write it into the record.

## Persistence

`Store` reads and writes JSON in `~/Library/Application Support/Peeko/`:

- `tasks.json` (`TaskList`): the enabled tasks, in display order
- `days/yyyy-MM-dd.json` (`DayRecord`): `statuses` and `accumulatedSeconds`, keyed by task id

Rules:

- Every file has a `schemaVersion`. Schema changes are **additive only**, so older files keep decoding.
- Writes are atomic. A file that fails to decode is moved aside, never overwritten.
- One file per day makes history, streaks and export simple later on.

## Days and time zones

The day key comes from `DayKey.string(for:)`, which uses `Calendar.current`: local time, never UTC. At 01:00 in UTC+3 it's already the new day, even though UTC says yesterday.

The app moves to a new day file when it gets `NSCalendarDayChanged` and whenever the popover opens. From Phase 3, the scheduler also checks on every tick. Yesterday's file is already saved, so a rollover just loads (or creates) today's.

## App layer

- **`AppModel`** (`@MainActor @Observable`) holds the presets, the task list and today's record. All changes go through it, and it saves after each one.
- **`RootView`** draws the header (face, wordmark, `done / total`), the Today and Catalog tabs, and the footer. The content area has a **fixed height**: a `MenuBarExtra` window that resizes when you switch tabs drifts away from the menu bar.
- **`FaceView`** draws Peeko from the brand file's 72×60 geometry. Status maps to a face: pending or failed → *dozing*, in progress → *looking*, done → *beaming*.
- **`MenuBarIcon`** draws an eyes-only **template** `NSImage`, so macOS tints it for light and dark menu bars. It shows the day's overall mood: nothing done → dozing, some progress → looking, everything done → beaming.

## The bundle

`scripts/bundle.sh` builds the `.app` without Xcode:

```
Peeko.app/Contents/
├── Info.plist      # com.a7med7x7.peeko, LSUIElement=true, LSMinimumSystemVersion=14.0
├── MacOS/Peeko
└── Resources/catalog.json
```

Later phases add `AppIcon.icns`, a DMG, a Developer ID signature and notarization, and Sparkle updates. See [blueprint.md](blueprint.md) §6.
