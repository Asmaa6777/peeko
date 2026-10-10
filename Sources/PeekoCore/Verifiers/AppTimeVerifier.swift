import Foundation

public struct AppTimeVerifier: Verifier {
    public let definition: TaskDefinition

    public init(definition: TaskDefinition) {
        self.definition = definition
    }

    public func evaluate(context: VerifierContext) async -> TaskStatus {
        // Keep the original completion time once done.
        if let existing = context.record.statuses[definition.id], existing.isDone {
            return existing
        }
        guard let minutes = definition.params.minutes, minutes > 0 else { return .pending }
        let target = Double(minutes) * 60
        let seconds = context.record.accumulatedSeconds[definition.id] ?? 0
        if seconds >= target { return .done(at: context.now) }
        return seconds > 0 ? .inProgress(progress: seconds / target) : .pending
    }
}
