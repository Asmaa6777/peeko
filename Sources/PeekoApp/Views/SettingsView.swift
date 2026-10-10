import PeekoCore
import ServiceManagement
import SwiftUI
import UserNotifications

struct SettingsView: View {
    @Environment(AppModel.self) private var model
    @ViewState private var username = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            PanelSection(title: "GitHub") {
                VStack(alignment: .leading, spacing: 6) {
                    HStack(spacing: 6) {
                        Text("@").foregroundStyle(Theme.muted)
                        TextField("username", text: $username)
                            .textFieldStyle(.plain)
                            .onSubmit(commitUsername)
                        if username != model.settings.githubUsername {
                            Button("Save", action: commitUsername)
                                .buttonStyle(.plain)
                                .font(Theme.mono().weight(.medium))
                                .foregroundStyle(usernameIsValid ? Theme.green : Theme.muted)
                                .disabled(!usernameIsValid)
                        }
                    }
                    .font(.system(size: 13))
                    Text("Public pushes only. Checked every 10 minutes.")
                        .font(Theme.mono(10))
                        .foregroundStyle(Theme.muted)
                }
                .card()
            }

            PanelSection(title: "Notifications") {
                VStack(alignment: .leading, spacing: 10) {
                    notificationPermission
                    Divider().overlay(Color.white.opacity(0.08))
                    Toggle(isOn: Binding(
                        get: { model.settings.eveningNudgeEnabled },
                        set: { on in model.updateSettings { $0.eveningNudgeEnabled = on } }
                    )) {
                        Text("Evening reminder").font(.system(size: 13))
                    }
                    .toggleStyle(.switch)
                    .controlSize(.mini)
                    .tint(Theme.green)
                    if model.settings.eveningNudgeEnabled {
                        HStack {
                            Text("if anything's left at")
                                .font(Theme.mono())
                                .foregroundStyle(Theme.muted)
                            Spacer()
                            DatePicker("", selection: nudgeTime, displayedComponents: .hourAndMinute)
                                .labelsHidden()
                                .datePickerStyle(.field)
                                .fixedSize()
                        }
                    }
                }
                .card()
            }

            PanelSection(title: "General") {
                VStack(alignment: .leading, spacing: 10) {
                    Toggle(isOn: Binding(
                        get: { model.launchAtLoginStatus == .enabled },
                        set: { model.setLaunchAtLogin($0) }
                    )) {
                        Text("Launch at login").font(.system(size: 13))
                    }
                    .toggleStyle(.switch)
                    .controlSize(.mini)
                    .tint(Theme.green)
                    if model.launchAtLoginStatus == .requiresApproval {
                        Button("Approve in System Settings → Login Items") {
                            SMAppService.openSystemSettingsLoginItems()
                        }
                        .buttonStyle(.plain)
                        .font(Theme.mono())
                        .foregroundStyle(Theme.green)
                    }
                    Divider().overlay(Color.white.opacity(0.08))
                    linkRow("Your data", action: "Reveal in Finder") { model.revealDataFolder() }
                    linkRow("Updates", action: "Releases on GitHub") {
                        NSWorkspace.shared.open(URL(string: "https://github.com/STACK-42/peeko/releases")!)
                    }
                }
                .card()
            }
        }
        .onAppear { username = model.settings.githubUsername }
    }

    @ViewBuilder
    private var notificationPermission: some View {
        switch model.notificationStatus {
        case .authorized, .provisional, .ephemeral:
            Label("Notifications are on", systemImage: "checkmark.circle.fill")
                .font(.system(size: 13))
                .foregroundStyle(Theme.green)
        case .denied:
            linkRow("Notifications are off", action: "Open Settings") {
                NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.Notifications-Settings.extension")!)
            }
        default:
            linkRow("Get a ping when Peeko sees something", action: "Allow") {
                Task { await model.requestNotifications() }
            }
        }
    }

    private func linkRow(_ label: String, action: String, perform: @escaping () -> Void) -> some View {
        HStack {
            Text(label).font(.system(size: 13))
            Spacer()
            Button(action, action: perform)
                .buttonStyle(.plain)
                .font(Theme.mono().weight(.medium))
                .foregroundStyle(Theme.green)
        }
    }

    private var trimmedUsername: String {
        username.trimmingCharacters(in: .whitespaces).trimmingCharacters(in: CharacterSet(charactersIn: "@"))
    }

    private var usernameIsValid: Bool {
        trimmedUsername.isEmpty || GitHubEventsClient.isValidUsername(trimmedUsername)
    }

    private func commitUsername() {
        guard usernameIsValid else { return }
        let name = trimmedUsername
        username = name
        model.updateSettings { $0.githubUsername = name }
    }

    private var nudgeTime: Binding<Date> {
        Binding(
            get: {
                let minutes = model.settings.eveningNudgeMinutes
                return Calendar.current.date(bySettingHour: minutes / 60, minute: minutes % 60, second: 0, of: .now) ?? .now
            },
            set: { date in
                let c = Calendar.current.dateComponents([.hour, .minute], from: date)
                model.updateSettings { $0.eveningNudgeMinutes = (c.hour ?? 21) * 60 + (c.minute ?? 0) }
            }
        )
    }
}
