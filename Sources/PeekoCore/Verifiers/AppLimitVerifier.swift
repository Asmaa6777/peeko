import Foundation

/// "Stay under N minutes." Shows usage as progress toward the limit, fails once it's passed.
/// The scheduler marks it done at the end of the day if it never went over.
public struct AppLimitVerifier: Verifier {
    public let definition: TaskDefinition

    public init(definition: TaskDefinition) {
        self.definition = definition
    }

    public func evaluate(context: VerifierContext) async -> TaskStatus {
        if let existing = context.record.statuses[definition.id] {
            switch existing {
            case .done, .failed: return existing
            default: break
            }
        }
        guard let minutes = definition.params.minutes, minutes > 0 else { return .pending }
        let used = context.record.accumulatedSeconds[definition.id] ?? 0
        let limit = Double(minutes) * 60
        return used > limit ? .failed : .inProgress(progress: used / limit)
    }
}
