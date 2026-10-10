import Foundation

public struct VerifierContext: Sendable {
    public var now: Date
    public var record: DayRecord

    public init(now: Date, record: DayRecord) {
        self.now = now
        self.record = record
    }
}

public protocol Verifier: Sendable {
    var definition: TaskDefinition { get }
    func evaluate(context: VerifierContext) async -> TaskStatus
}

public struct ManualVerifier: Verifier {
    public let definition: TaskDefinition

    public init(definition: TaskDefinition) {
        self.definition = definition
    }

    public func evaluate(context: VerifierContext) async -> TaskStatus {
        context.record.statuses[definition.id] ?? .pending
    }
}

/// Returns nil for types that have no verifier yet.
public func makeVerifier(for definition: TaskDefinition) -> (any Verifier)? {
    switch definition.type {
    case .manual: ManualVerifier(definition: definition)
    case .appTime, .remoteCheck, .appLimit: nil
    }
}
