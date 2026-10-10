import PeekoCore
import SwiftUI

struct TodayView: View {
    @Environment(AppModel.self) private var model
    @Binding var tab: Tab

    var body: some View {
        if model.tasks.isEmpty {
            VStack(spacing: 10) {
                FaceView(mood: .dozing, width: 56)
                Text("Nothing to watch yet.")
                    .font(.system(size: 14, weight: .semibold))
                Button("Pick some tasks") { tab = .catalog }
                    .buttonStyle(.plain)
                    .font(Theme.mono(12).weight(.medium))
                    .foregroundStyle(Theme.night)
                    .padding(.horizontal, 12)
                    .padding(.vertical, 6)
                    .background(Capsule().fill(Theme.green))
            }
            .frame(maxWidth: .infinity, minHeight: Theme.contentHeight - 20)
        } else {
            VStack(spacing: 6) {
                ForEach(model.tasks) { task in
                    TaskRow(task: task)
                }
            }
        }
    }
}

private struct TaskRow: View {
    @Environment(AppModel.self) private var model
    let task: TaskDefinition
    @ViewState private var hovering = false

    var body: some View {
        let status = model.status(for: task)
        let content = HStack(spacing: 12) {
            FaceView(mood: Mood(status), width: 30)
            VStack(alignment: .leading, spacing: 2) {
                Text(task.title)
                    .font(Theme.rowTitle)
                    .foregroundStyle(status.isDone ? Theme.green : Theme.chalk)
                    .lineLimit(1)
                subtitle(status)
                    .font(Theme.mono())
                    .foregroundStyle(Theme.muted)
                    .lineLimit(1)
            }
            Spacer(minLength: 8)
            trailing(status)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .background(RoundedRectangle(cornerRadius: 10).fill(hovering && task.type == .manual ? Theme.cardHover : Theme.card))
        .contentShape(Rectangle())
        .onHover { hovering = $0 }

        if task.type == .manual {
            Button { model.toggleManual(task) } label: { content }
                .buttonStyle(.plain)
                .accessibilityLabel("\(task.title), \(status.isDone ? "done" : "not done")")
        } else {
            content
        }
    }

    @ViewBuilder
    private func subtitle(_ status: TaskStatus) -> some View {
        if case let .done(at) = status {
            Text("done · \(at.formatted(date: .omitted, time: .shortened))")
        } else {
            Text(TaskDetail.describe(task))
        }
    }

    @ViewBuilder
    private func trailing(_ status: TaskStatus) -> some View {
        if task.type == .manual {
            Image(systemName: status.isDone ? "checkmark.circle.fill" : "circle")
                .font(.system(size: 18))
                .foregroundStyle(status.isDone ? Theme.green : Theme.muted)
        } else if !model.isSupported(task) {
            Text("auto · soon")
                .font(Theme.mono(10))
                .foregroundStyle(Theme.muted)
                .padding(.horizontal, 6)
                .padding(.vertical, 3)
                .background(Capsule().strokeBorder(Theme.muted.opacity(0.4)))
        }
    }
}
