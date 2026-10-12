import Foundation

/// "Stay under N minutes." Shows usage as progress toward the limit, fails once it's passed.
/// The scheduler marks it done at the end of the day if it never went over.
public struct AppLimitVerifier: Verifier {
    public let definition: TaskDefinition

    public init(definition: TaskDefinition) {
        self.definition = definition
    }

    public func evaluate(context: VerifierContext) async -> TaskStatus {
        status(record: context.record)
    }

    public func status(record: DayRecord) -> TaskStatus {
        if let existing = record.statuses[definition.id] {
            switch existing {
            case .done, .failed: return existing
            default: break
            }
        }
        guard let minutes = definition.params.minutes, minutes >= 0 else { return .pending }
        let used = record.accumulatedSeconds[definition.id] ?? 0
        let limit = Double(minutes) * 60
        if used > limit { return .failed }
        // A zero limit ("none before noon") has no meaningful ratio; show an empty ring.
        return .inProgress(progress: limit == 0 ? 0 : used / limit)
    }
}
