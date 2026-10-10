import PeekoCore
import SwiftUI

struct CatalogView: View {
    @Environment(AppModel.self) private var model
    @ViewState private var draft = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            section("Presets") {
                if model.presets.isEmpty {
                    Text("No presets found (catalog.json missing from the app bundle).")
                        .font(Theme.mono())
                        .foregroundStyle(Theme.muted)
                }
                ForEach(model.presets) { preset in
                    row(preset) {
                        Toggle("", isOn: Binding(
                            get: { model.isEnabled(preset) },
                            set: { model.setEnabled(preset, $0) }
                        ))
                        .toggleStyle(.switch)
                        .tint(Theme.green)
                        .controlSize(.mini)
                        .labelsHidden()
                        .accessibilityLabel(preset.title)
                    }
                }
            }

            section("Your tasks") {
                ForEach(model.customTasks) { task in
                    row(task) {
                        Button { model.remove(task) } label: {
                            Image(systemName: "trash")
                                .foregroundStyle(Theme.muted)
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel("Delete \(task.title)")
                    }
                }
                HStack(spacing: 8) {
                    TextField("Custom manual task", text: $draft)
                        .textFieldStyle(.plain)
                        .font(.system(size: 13))
                        .onSubmit(add)
                    Button("Add", action: add)
                        .buttonStyle(.plain)
                        .font(Theme.mono().weight(.medium))
                        .foregroundStyle(draftIsEmpty ? Theme.muted : Theme.green)
                        .disabled(draftIsEmpty)
                }
                .padding(.horizontal, 12)
                .padding(.vertical, 9)
                .background(RoundedRectangle(cornerRadius: 10).strokeBorder(Color.white.opacity(0.12)))
                AppTimeForm()
            }
        }
    }

    private var draftIsEmpty: Bool {
        draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    private func add() {
        model.addCustomManualTask(title: draft)
        draft = ""
    }

    private func section<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        PanelSection(title: title, content: content)
    }

    private func row<Trailing: View>(_ task: TaskDefinition, @ViewBuilder trailing: () -> Trailing) -> some View {
        HStack(spacing: 12) {
            Image(systemName: task.icon)
                .font(.system(size: 14))
                .frame(width: 22)
                .foregroundStyle(Theme.green)
            VStack(alignment: .leading, spacing: 2) {
                Text(task.title)
                    .font(Theme.rowTitle)
                    .lineLimit(1)
                Text(TaskDetail.describe(task))
                    .font(Theme.mono())
                    .foregroundStyle(Theme.muted)
                    .lineLimit(1)
            }
            Spacer(minLength: 8)
            trailing()
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .background(RoundedRectangle(cornerRadius: 10).fill(Theme.card))
    }
}
