import AppKit
import SwiftUI
import UniformTypeIdentifiers

/// "+ custom app-time task": pick an app, set minutes, add.
struct AppTimeForm: View {
    @Environment(AppModel.self) private var model
    @ViewState private var app: PickedApp?
    @ViewState private var minutes = 30

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

            Stepper(value: $minutes, in: 5...480, step: 5) {
                Text("\(minutes) min")
                    .font(Theme.mono())
                    .monospacedDigit()
            }
            .controlSize(.mini)

            Button("Add") {
                guard let app else { return }
                model.addCustomAppTimeTask(bundleID: app.bundleID, appName: app.name, minutes: minutes)
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
    static func choose() -> URL? {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.prompt = "Watch this folder"
        panel.message = "Pick your notes folder or Obsidian vault. Peeko only reads .md and .txt files in it."
        NSApp.activate()
        return panel.runModal() == .OK ? panel.url : nil
    }
}
