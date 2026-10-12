import Foundation

public struct DayRecord: Codable, Equatable, Sendable {
    public static let currentSchemaVersion = 1

    public var schemaVersion: Int
    public var day: String
    public var statuses: [String: TaskStatus]
    public var accumulatedSeconds: [String: Double]
    /// Stretches of keyboard/mouse activity today, merged across gaps under 5 minutes.
    public var activity: [ActivitySpan]?

    public init(day: String) {
        self.schemaVersion = Self.currentSchemaVersion
        self.day = day
        self.statuses = [:]
        self.accumulatedSeconds = [:]
    }
}

public struct ActivitySpan: Codable, Equatable, Sendable {
    public var start: Date
    public var end: Date

    public init(start: Date, end: Date) {
        self.start = start
        self.end = end
    }
}

public enum ActivityLog {
    public static let mergeGap: TimeInterval = 5 * 60

    /// Adds an input moment to the day's spans. Returns true when it starts a new span.
    @discardableResult
    public static func note(_ instant: Date, in spans: inout [ActivitySpan], notBefore dayStart: Date) -> Bool {
        let t = max(instant, dayStart)
        if let last = spans.last, t.timeIntervalSince(last.end) <= mergeGap {
            if t > last.end { spans[spans.count - 1].end = t }
            return false
        }
        spans.append(ActivitySpan(start: t, end: t))
        return true
    }
}

/// Per-file word counts at the start of the day, saved once a file changes.
public struct WordBaselines: Codable, Equatable, Sendable {
    public static let currentSchemaVersion = 1

    public var schemaVersion = Self.currentSchemaVersion
    public var day: String
    /// task id → relative file path → word count
    public var tasks: [String: [String: Int]] = [:]

    public init(day: String) {
        self.day = day
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

    /// The last second of a `yyyy-MM-dd` day in the calendar's time zone.
    public static func endOfDay(_ key: String, calendar: Calendar = .current) -> Date? {
        let parts = key.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3,
              let start = calendar.date(from: DateComponents(year: parts[0], month: parts[1], day: parts[2])),
              let next = calendar.date(byAdding: .day, value: 1, to: start)
        else { return nil }
        return next.addingTimeInterval(-1)
    }
}
