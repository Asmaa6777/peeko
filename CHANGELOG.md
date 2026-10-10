# Changelog

All notable changes to Peeko are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.2.0] - 2026-10-10

Peeko notices more: sleep, early mornings, writing, and a history of how your days went.

### Added

- "Slept on time": passes when there's no keyboard or mouse activity between 00:30 and 05:00.
- "Up before 8": passes when your first stretch of activity after 04:00 starts before 08:00.
- "Write in your notes" and "Write 300 words": pick a notes folder or Obsidian vault, and Peeko ticks these when a note changes today or when you've added enough words.
- Days now record when you were active (stretches of keyboard and mouse use), stored in the day file.
- History tab: your streak (days in a row with at least one thing done), a five-week calendar of how much got done each day, and a streak plus a 30-day count for every task. The streak also shows in the header.
- Settings → Export JSON: your tasks, settings and every day in one file.

### Changed

- GitHub Actions workflows use the Node 24 versions of their actions.

## [0.1.0] - 2026-10-10

First release.

### Added

- Menu bar app with a Today list and a Catalog of presets.
- Manual tasks, including your own custom ones.
- Tasks that count active time in an app (Read in Preview, Deep work in VS Code / Terminal), plus custom ones for any app. Idle time isn't counted, and a sleeping Mac never adds time.
- Everything is saved locally in `~/Library/Application Support/Peeko/`, one file per day, and the list starts fresh each morning.
- "Push to GitHub" task: checks your public GitHub activity every 10 minutes for a push made today, in your own time zone.
- Settings: GitHub username, evening reminder time, launch at login, and a shortcut to your data folder.
- Notifications: one when Peeko ticks something off for you, and an optional evening reminder when tasks are left.
- Break reminders: after an hour of active screen time, Peeko suggests a 5-minute walk. Stepping away for 5 minutes resets the count. The interval is adjustable (30–120 minutes), or turn it off.
- First-launch onboarding.
- App icon, DMG, and a one-line installer (`install.sh`).
