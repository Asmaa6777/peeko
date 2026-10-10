# peeko

A small menu bar app for macOS that keeps your daily list and ticks things off by itself.

Peeko hangs from the top of your screen and peeks at what you're doing. Keep a PDF open in Preview for half an hour and "Read for 30 minutes" ticks itself. Push to GitHub and that one goes green as well. For the things it can't see, like the gym or drinking water, you tap them yourself.

It isn't there to police you. It's more like a little green creature that notices when you got something done and looks pleased about it.

## Where it's at

Early days. What works right now:

- the menu bar icon and popover, with a **Today** list and a **Catalog** of presets
- manual tasks, plus your own custom ones
- everything saved locally, with a fresh list each morning

Not built yet:

- tasks that count time in an app (reading, deep work). This is next.
- the GitHub push check, notifications, and settings
- a signed `.dmg` you can just download

There's no download yet. If you want to try it, build it from source.

## Privacy

There are no accounts, servers, or analytics. Your tasks and history are plain JSON files in `~/Library/Application Support/Peeko/`, so you can read them, back them up, or delete them whenever you like.

Once the GitHub task exists, it will call GitHub's public API with the username you give it. That's the only network request Peeko makes, and only if you turn that task on.

## How it decides you did something

Peeko only looks at a couple of simple signals:

- **Time in an app:** which app is in front, and whether you've touched the keyboard or mouse in the last couple of minutes. If both hold, the minute counts.
- **GitHub:** whether your public activity shows a push made today, in your own time zone.

These are proxies, not proof. Leave a PDF open and nudge the mouse now and then, and it will count as reading. That's fine. The point is a nudge, not a lie detector.

## Build it yourself

You need macOS 14 or later on an Apple silicon Mac, and Apple's Command Line Tools (`xcode-select --install`). You don't need Xcode.

```sh
git clone <this repo>
cd peeko
make run
```

That builds `build/Peeko.app` and opens it. Look for the eyes in your menu bar. If your menu bar is crowded, macOS might hide the icon behind the `«` overflow; ⌘-drag it somewhere you can see it.

## Docs

- [Development setup](docs/development.md): toolchain, commands, and quirks
- [Architecture](docs/architecture.md): how the pieces fit together
- [Blueprint](docs/blueprint.md): the original plan and roadmap
- [Brand](docs/brand/): the Peeko character, colors, and faces
