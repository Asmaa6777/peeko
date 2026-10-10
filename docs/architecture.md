# Architecture

Peeko is a SwiftPM package with three targets:

| Target | Kind | Contains |
|---|---|---|
| `PeekoCore` | library, no SwiftUI | models, verifiers, persistence, the scheduler, and platform providers |
| `PeekoApp` | executable | the `MenuBarExtra` UI, the app model, and the menu bar icon |
| `PeekoChecks` | executable (`Checks/`) | logic checks with fake providers, run by `make check` |

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
| `appTime` | Adds up active minutes while one of the given apps is in front | done |
| `remoteCheck` | Polls an outside source (`githubPush`) | done |
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

`ManualVerifier` just returns whatever the day record says. `AppTimeVerifier` turns accumulated seconds into `pending`, `inProgress(seconds / target)`, or `done`. Once done, it keeps the original completion time.

## Scheduler and app time

`Scheduler` (`@MainActor @Observable`) owns **today's `DayRecord`**. Every 10 seconds it:

1. rolls over to a new day file if the local date changed
2. asks the providers which app is in front (`FrontmostAppProvider`) and how long since the last input (`IdleTimeProvider`)
3. passes that sample to `ActivityTracker`, which credits time to matching `appTime` tasks
4. evaluates the automatic tasks' verifiers and saves if anything changed

`ActivityTracker` is a pure struct, so its rules are easy to check:

- Credit is `min(now - lastTick, 10s)`. A sleeping Mac never adds hours, and a clock that jumps backwards adds nothing.
- The first tick, and the first tick after `pause()`, only start the clock.
- A tick counts only if the front app is one of the task's `bundleIDs` and the time since your last input is within `idleCutoffSeconds` (120s by default).

`willSleep` and `screensDidSleep` call `pause()`. The providers are `@MainActor` protocols. The real ones use `NSWorkspace.frontmostApplication` and `CGEventSource.secondsSinceLastEventType`; neither needs a permission. `Checks/` swaps in fakes.

Manual tasks are never written by evaluation. Their status comes only from taps (`setManual`), so a tick that's partway through an `await` can't undo one.

## GitHub push check

`GitHubPushVerifier` passes when the user's public events include a `PushEvent` whose `created_at` (UTC) falls on **today in the user's calendar**. The done time is the day's first push. The username comes from the task's own `params.username`, or from `AppSettings.githubUsername` if that's empty.

`GitHubEventsClient` is an actor shared through `VerifierEnvironment`. The verifier is evaluated every 10s tick, but the client:

- fetches at most once per user every 10 minutes and serves the cache in between
- sends the last `ETag` as `If-None-Match`; a 304 keeps the cache
- keeps the cache on errors and retries at the next poll
- validates usernames before building a URL
- uses `URLSessionHTTPClient` with no `URLCache`, so the 304s reach us

Unauthenticated 304s **do** count against GitHub's 60/hour limit; polling every 10 minutes uses 6. GitHub's events API can lag by hours, so the UI says "checking…" rather than "not done".

`makeVerifier(for:environment:)` builds verifiers. `VerifierEnvironment` carries the GitHub client, the username, and the calendar. `isVerifiable(_:)` answers "is there a verifier for this?" without building one.

## Persistence

`Store` reads and writes JSON in `~/Library/Application Support/Peeko/`:

- `tasks.json` (`TaskList`): the enabled tasks, in display order
- `settings.json` (`AppSettings`): GitHub username, evening reminder, onboarding done. Every field decodes with a default, so older files keep loading.
- `days/yyyy-MM-dd.json` (`DayRecord`): `statuses` and `accumulatedSeconds`, keyed by task id

Rules:

- Every file has a `schemaVersion`. Schema changes are **additive only**, so older files keep decoding.
- Writes are atomic. A file that fails to decode is moved aside, never overwritten.
- One file per day makes history, streaks and export simple later on.

## Days and time zones

The day key comes from `DayKey.string(for:)`, which uses `Calendar.current`: local time, never UTC. At 01:00 in UTC+3 it's already the new day, even though UTC says yesterday.

The scheduler moves to a new day file on every tick, on `NSCalendarDayChanged`, and whenever the popover opens. Yesterday's file is already saved, so a rollover just loads (or creates) today's. A tick that crosses midnight credits its few seconds to the new day.

## App layer

- **`AppModel`** (`@MainActor @Observable`) holds the presets, the task list (`tasks.json`) and the settings (`settings.json`), and owns the `Scheduler`. Whenever the task list or GitHub username changes, it updates the scheduler and asks it to re-evaluate. It also owns notification permission and launch at login (`SMAppService.mainApp`).
- **Notifications** (`Notifier`, wrapping `UNUserNotificationCenter`):
  - `Scheduler.onAutoComplete` fires once when an *automatic* task flips to done, and the app posts "Peeko saw that!". Manual taps never notify.
  - `Scheduler.onTick` drives the evening reminder. `EveningNudge.isDue` decides, and the last reminder's day is kept in `UserDefaults` so it fires at most once a day.
  - Break reminders also run from `onTick`. `BreakTracker`, a pure struct in the core, adds up active screen time in any app: input within 2 minutes counts, using the same capped 10-second ticks. Five or more minutes idle, or a 5+ minute gap between ticks (asleep, app not running), counts as the break and resets the count. Each reminder also restarts it. The messages rotate (`BreakMessages`). The interval and on/off live in `AppSettings`.
  - Banners show even while the popover is open.
- **Onboarding** replaces the tabs on first launch, until `hasCompletedOnboarding`: welcome, then the notification permission (with a reason), then an optional GitHub username. The tab bar stays in the layout, invisible, so the panel height doesn't change.
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
