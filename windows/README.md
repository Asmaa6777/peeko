# Peeko for Windows (skeleton)

Sits beside the macOS app and shares its data contract: the same `tasks.json` / `days/*.json`
shapes, `schemaVersion` rules and verifier semantics. Nothing here touches `Sources/` or the
macOS build.

| Path | What |
|---|---|
| `src/Peeko.Core` | Portable logic: models, tracker, verifiers, store, scheduler. No UI, no Win32. |
| `src/Peeko.Windows` | Tray app (WinForms `NotifyIcon`) and the Win32 providers (foreground window, idle time). |
| `checks/Peeko.Checks` | Console checks, the equivalent of `make check`. Runs on any OS. |
| `Resources/catalog.windows.json` | Windows presets. Same shape as the Mac catalog, with `processNames` instead of `bundleIDs`. |

## Try it

```
dotnet run --project checks/Peeko.Checks        # any OS
dotnet run --project src/Peeko.Windows          # Windows only
```

Data lives in `%APPDATA%\Peeko`. Launching `Peeko.exe` again brings the window forward; closing the
window hides it to the tray. Use **Quit** in the footer or the tray menu to exit.

A self-contained exe: `dotnet publish src/Peeko.Windows -c Release -r win-x64 --self-contained true
-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
-o dist/<name>` (about 72 MB).

## What's here

Shaped like the macOS app, and drawn by hand to match it:

- **Tray popover:** left-click the tray face. Header with the face, done / total and streak;
  Today, Explore, History and Settings tabs; footer with *Open window* and *Quit*. Fixed size, opens
  next to the taskbar, hides when you click elsewhere (or press Esc).
- **Main window:** resizable, with a sidebar (Ctrl+1–4) and a wide column. Explore is a card grid
  with search (Ctrl+F) and category chips; History shows 20 weeks. Closing it keeps Peeko in the tray.
- **Right-click the tray face** for a quick menu.

- **Explore:** 23 presets grouped by how Peeko checks them, each group with its own color. Switching one on asks for what it needs (a
  folder, a username, a feed link). Your own manual tasks go at the bottom.
- **Settings:** GitHub username, balloons, evening reminder (time), break reminders (interval),
  launch at login, open the data folder, export everything as JSON.

## Scope

Runs `manual`, `appTime`, `appLimit` (with window-title filters), `activity` (quiet / start / gap
windows), `remoteCheck` (GitHub, feeds, Duolingo, LeetCode, Codeforces), `folderActivity`,
`gitCommits` and `folderTidy`.

`backup` (Time Machine) and `reminders` (EventKit) are macOS-only: they stay in `tasks.json`
untouched and show "Mac only".

Not done yet: a popover anchored to the tray, an installer and signing, auto-updates, onboarding,
and CI for Windows.

## Layout of the core

| File | What |
|---|---|
| `Scheduler.cs` | The 10-second tick: roll the day, credit time, log activity, evaluate, save. |
| `ActivityTracker.cs` | `appTime` / `appLimit` credit, idle cutoff, title filter, time window. |
| `Verifiers.cs` | Pure verifiers (manual, appTime, appLimit, activity) and the async ones. |
| `ActivityWindow.cs` | The activity log and the quiet / start / gap rules. |
| `OnlineChecks.cs`, `Http.cs` | `CachedFetcher`, `GitHubEventsClient`, parsers, the `IHttp` seam. |
| `LocalChecks.cs` | `FolderScanner` (word counts), `FolderTidy`, `GitScanner`. |
| `Reminders.cs` | `BreakTracker`, evening nudge, `ReminderEngine`. |

## Rules

- The JSON files are the contract with the macOS app (same shapes, `schemaVersion`, additive only).
- Don't change anything outside `windows/` for Windows work.
- The UI can't be run from macOS or Linux; `dotnet build` and the checks prove it compiles, not that it renders.
