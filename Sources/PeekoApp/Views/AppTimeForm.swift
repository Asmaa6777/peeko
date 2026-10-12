import AppKit
import SwiftUI
import UniformTypeIdentifiers

/// "+ custom app-time task": pick an app, at least / at most, minutes, add.
struct AppTimeForm: View {
    @Environment(AppModel.self) private var model
    @ViewState private var app: PickedApp?
    @ViewState private var minutes = 30
    @ViewState private var isLimit = false

    var body: some View {
        HStack(spacing: 8) {
            Menu {
                ForEach(PickedApp.running()) { candidate in
                    Button(candidate.name) { app = candidate }
                }
                Divider()
                Button("Choose from Applications…") {
                    if let picked = PickedApp.chooseFromApplications() { app = picked }
                }
            } label: {
                Text(app?.name ?? "Pick an app")
                    .font(.system(size: 13))
                    .foregroundStyle(app == nil ? Theme.muted : Theme.chalk)
                    .lineLimit(1)
            }
            .menuStyle(.borderlessButton)
            .menuIndicator(.visible)
            .fixedSize()

            Spacer(minLength: 4)

            Button(isLimit ? "at most" : "at least") { isLimit.toggle() }
                .buttonStyle(.plain)
                .font(Theme.mono())
                .foregroundStyle(isLimit ? Theme.blush : Theme.green)
                .help(isLimit ? "Stay under this many minutes" : "Spend at least this many minutes")

            Stepper(value: $minutes, in: 5...480, step: 5) {
                Text("\(minutes) min")
                    .font(Theme.mono())
                    .monospacedDigit()
            }
            .controlSize(.mini)

            Button("Add") {
                guard let app else { return }
                model.addCustomAppTimeTask(bundleID: app.bundleID, appName: app.name, minutes: minutes, limit: isLimit)
                self.app = nil
            }
            .buttonStyle(.plain)
            .font(Theme.mono().weight(.medium))
            .foregroundStyle(app == nil ? Theme.muted : Theme.green)
            .disabled(app == nil)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .background(RoundedRectangle(cornerRadius: 10).strokeBorder(Color.white.opacity(0.12)))
    }
}

struct PickedApp: Identifiable, Hashable {
    let bundleID: String
    let name: String
    var id: String { bundleID }

    @MainActor
    static func running() -> [PickedApp] {
        let me = Bundle.main.bundleIdentifier
        let apps = NSWorkspace.shared.runningApplications.compactMap { app -> PickedApp? in
            guard app.activationPolicy == .regular,
                  let id = app.bundleIdentifier, id != me,
                  let name = app.localizedName
            else { return nil }
            return PickedApp(bundleID: id, name: name)
        }
        return Array(Set(apps)).sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
    }

    @MainActor
    static func chooseFromApplications() -> PickedApp? {
        let panel = NSOpenPanel()
        panel.directoryURL = URL(fileURLWithPath: "/Applications")
        panel.allowedContentTypes = [.application]
        panel.allowsMultipleSelection = false
        panel.prompt = "Choose"
        NSApp.activate()
        guard panel.runModal() == .OK,
              let url = panel.url,
              let id = Bundle(url: url)?.bundleIdentifier
        else { return nil }
        return PickedApp(bundleID: id, name: TaskDetail.appName(id))
    }
}

enum PickedFolder {
    @MainActor
    static func choose(message: String) -> URL? {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.prompt = "Watch this folder"
        panel.message = message
        NSApp.activate()
        return panel.runModal() == .OK ? panel.url : nil
    }
}

/// A small text prompt (a username, a feed link) shown when a preset needs one.
enum AskText {
    @MainActor
    static func ask(title: String, message: String, placeholder: String) -> String? {
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = message
        alert.addButton(withTitle: "Add")
        alert.addButton(withTitle: "Cancel")
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 300, height: 24))
        field.placeholderString = placeholder
        alert.accessoryView = field
        alert.window.initialFirstResponder = field
        NSApp.activate()
        guard alert.runModal() == .alertFirstButtonReturn else { return nil }
        let text = field.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        return text.isEmpty ? nil : text
    }
}
