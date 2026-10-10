import Foundation

/// Past days, loaded from the day files, plus helpers for streaks.
public struct History: Sendable {
    public private(set) var days: [String: DayRecord]
    let calendar: Calendar

    public init(days: [String: DayRecord], calendar: Calendar = .current) {
        self.days = days
        self.calendar = calendar
    }

    /// The `count` days ending at `date`, oldest first. Missing days are empty records.
    public static func load(from store: Store, days count: Int, endingAt date: Date, calendar: Calendar = .current) -> History {
        var days: [String: DayRecord] = [:]
        for offset in 0..<count {
            let key = DayKey.string(for: calendar.date(byAdding: .day, value: -offset, to: date)!, calendar: calendar)
            days[key] = store.loadDay(key)
        }
        return History(days: days, calendar: calendar)
    }

    /// Swaps in the live record for today, which may be newer than its file.
    public mutating func replace(_ record: DayRecord) {
        days[record.day] = record
    }

    public func doneCount(on date: Date) -> Int {
        days[key(date)]?.statuses.values.filter(\.isDone).count ?? 0
    }

    public func isDone(_ taskID: String, on date: Date) -> Bool {
        days[key(date)]?.statuses[taskID]?.isDone == true
    }

    /// Days in a row with at least one task done.
    public func overallStreak(today: Date) -> Int {
        streak(today: today) { doneCount(on: $0) > 0 }
    }

    /// Days in a row this task was done.
    public func streak(for taskID: String, today: Date) -> Int {
        streak(today: today) { isDone(taskID, on: $0) }
    }

    /// How many of the last `days` days (including today) this task was done.
    public func timesDone(_ taskID: String, inLast days: Int, today: Date) -> Int {
        (0..<days).filter { isDone(taskID, on: calendar.date(byAdding: .day, value: -$0, to: today)!) }.count
    }

    /// Today only extends a streak; it never breaks one, because the day isn't over yet.
    private func streak(today: Date, _ isGoodDay: (Date) -> Bool) -> Int {
        var date = isGoodDay(today) ? today : calendar.date(byAdding: .day, value: -1, to: today)!
        var count = 0
        while days[key(date)] != nil, isGoodDay(date) {
            count += 1
            date = calendar.date(byAdding: .day, value: -1, to: date)!
        }
        return count
    }

    private func key(_ date: Date) -> String {
        DayKey.string(for: date, calendar: calendar)
    }
}

/// Everything Peeko knows, in one JSON file.
public struct PeekoExport: Codable, Sendable {
    public var exportedAt: Date
    public var tasks: TaskList
    public var settings: AppSettings
    public var days: [DayRecord]

    public static func make(from store: Store, now: Date = .now) -> PeekoExport {
        PeekoExport(
            exportedAt: now,
            tasks: store.loadTaskList(),
            settings: store.loadSettings(),
            days: store.allDayKeys().map(store.loadDay)
        )
    }

    public func encoded() throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .iso8601
        return try encoder.encode(self)
    }
}
