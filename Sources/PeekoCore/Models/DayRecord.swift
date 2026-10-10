import Foundation

public struct DayRecord: Codable, Equatable, Sendable {
    public static let currentSchemaVersion = 1

    public var schemaVersion: Int
    public var day: String
    public var statuses: [String: TaskStatus]
    public var accumulatedSeconds: [String: Double]

    public init(day: String) {
        self.schemaVersion = Self.currentSchemaVersion
        self.day = day
        self.statuses = [:]
        self.accumulatedSeconds = [:]
    }
}

public struct TaskList: Codable, Equatable, Sendable {
    public static let currentSchemaVersion = 1

    public var schemaVersion: Int
    public var tasks: [TaskDefinition]

    public init(tasks: [TaskDefinition] = []) {
        self.schemaVersion = Self.currentSchemaVersion
        self.tasks = tasks
    }
}

public enum DayKey {
    /// `yyyy-MM-dd` in the calendar's time zone, never UTC.
    public static func string(for date: Date, calendar: Calendar = .current) -> String {
        let c = calendar.dateComponents([.year, .month, .day], from: date)
        return String(format: "%04d-%02d-%02d", c.year ?? 0, c.month ?? 0, c.day ?? 0)
    }
}
