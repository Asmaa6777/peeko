import Foundation

public enum TaskStatus: Codable, Equatable, Sendable {
    case pending
    case inProgress(progress: Double)
    case done(at: Date)
    case failed

    public var isDone: Bool {
        if case .done = self { return true }
        return false
    }
}
