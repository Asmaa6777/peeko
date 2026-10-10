import PeekoCore
import SwiftUI

/// First launch only: who Peeko is, why it wants notifications, and an optional GitHub username.
struct OnboardingView: View {
    @Environment(AppModel.self) private var model
    @ViewState private var step = 0
    @ViewState private var username = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Spacer(minLength: 0)
            switch step {
            case 0: welcome
            case 1: notifications
            default: github
            }
            Spacer(minLength: 0)
            HStack(spacing: 6) {
                ForEach(0..<3) { i in
                    Capsule()
                        .fill(i == step ? Theme.green : Theme.muted.opacity(0.35))
                        .frame(width: i == step ? 16 : 6, height: 6)
                }
            }
        }
        .padding(.horizontal, 20)
        .padding(.vertical, 18)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        .animation(.easeOut(duration: 0.15), value: step)
    }

    private var welcome: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Hi, I'm Peeko.")
                .font(Theme.display(22))
            Text("I hang out up here and keep an eye on your daily list. When I see you've done something, like time in Preview or a push to GitHub, I tick it off for you. The rest you tap yourself.")
                .font(.system(size: 13))
                .foregroundStyle(Theme.chalk.opacity(0.85))
                .fixedSize(horizontal: false, vertical: true)
            Text("Everything stays on this Mac. No account, no tracking.")
                .font(Theme.mono())
                .foregroundStyle(Theme.muted)
                .fixedSize(horizontal: false, vertical: true)
            PillButton("Sounds good") { step = 1 }
                .padding(.top, 4)
        }
    }

    private var notifications: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Can I ping you?")
                .font(Theme.display(22))
            Text("I'll send one note when I tick something off for you, and, if you like, one in the evening when there's still something left. That's it.")
                .font(.system(size: 13))
                .foregroundStyle(Theme.chalk.opacity(0.85))
                .fixedSize(horizontal: false, vertical: true)
            HStack(spacing: 14) {
                PillButton("Allow notifications") {
                    Task {
                        await model.requestNotifications()
                        step = 2
                    }
                }
                skip("Not now") { step = 2 }
            }
            .padding(.top, 4)
        }
    }

    private var github: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("On GitHub?")
                .font(Theme.display(22))
            Text("Give me your username and I'll check for a public push each day. You can skip this and add it later in Settings.")
                .font(.system(size: 13))
                .foregroundStyle(Theme.chalk.opacity(0.85))
                .fixedSize(horizontal: false, vertical: true)
            HStack(spacing: 6) {
                Text("@").foregroundStyle(Theme.muted)
                TextField("username", text: $username)
                    .textFieldStyle(.plain)
                    .onSubmit(finish)
            }
            .font(.system(size: 13))
            .padding(.horizontal, 12)
            .padding(.vertical, 9)
            .background(RoundedRectangle(cornerRadius: 10).strokeBorder(Color.white.opacity(0.12)))
            HStack(spacing: 14) {
                PillButton("Done", action: finish)
                skip("Skip") {
                    username = ""
                    finish()
                }
            }
            .padding(.top, 4)
        }
    }

    private func skip(_ title: String, action: @escaping () -> Void) -> some View {
        Button(title, action: action)
            .buttonStyle(.plain)
            .font(Theme.mono(12))
            .foregroundStyle(Theme.muted)
    }

    private func finish() {
        let name = username.trimmingCharacters(in: .whitespaces).trimmingCharacters(in: CharacterSet(charactersIn: "@"))
        model.updateSettings {
            if GitHubEventsClient.isValidUsername(name) { $0.githubUsername = name }
            $0.hasCompletedOnboarding = true
        }
    }
}
