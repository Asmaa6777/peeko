import Foundation

public struct AppSettings: Codable, Equatable, Sendable {
    public static let currentSchemaVersion = 1

    public var schemaVersion = Self.currentSchemaVersion
    public var githubUsername = ""
    public var eveningNudgeEnabled = true
    /// Minutes after local midnight; 21:00 by default.
    public var eveningNudgeMinutes = 21 * 60
    public var hasCompletedOnboarding = false
    public var breakRemindersEnabled = true
    public var breakIntervalMinutes = 60

    public init() {}

    // Every field is optional on decode so files from older versions keep loading.
    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = AppSettings()
        schemaVersion = try c.decodeIfPresent(Int.self, forKey: .schemaVersion) ?? d.schemaVersion
        githubUsername = try c.decodeIfPresent(String.self, forKey: .githubUsername) ?? d.githubUsername
        eveningNudgeEnabled = try c.decodeIfPresent(Bool.self, forKey: .eveningNudgeEnabled) ?? d.eveningNudgeEnabled
        eveningNudgeMinutes = try c.decodeIfPresent(Int.self, forKey: .eveningNudgeMinutes) ?? d.eveningNudgeMinutes
        hasCompletedOnboarding = try c.decodeIfPresent(Bool.self, forKey: .hasCompletedOnboarding) ?? d.hasCompletedOnboarding
        breakRemindersEnabled = try c.decodeIfPresent(Bool.self, forKey: .breakRemindersEnabled) ?? d.breakRemindersEnabled
        breakIntervalMinutes = try c.decodeIfPresent(Int.self, forKey: .breakIntervalMinutes) ?? d.breakIntervalMinutes
    }
}

public enum EveningNudge {
    public static func isDue(
        now: Date,
        settings: AppSettings,
        lastNudgeDay: String?,
        remaining: Int,
        calendar: Calendar = .current
    ) -> Bool {
        guard settings.eveningNudgeEnabled, remaining > 0 else { return false }
        let c = calendar.dateComponents([.hour, .minute], from: now)
        guard (c.hour ?? 0) * 60 + (c.minute ?? 0) >= settings.eveningNudgeMinutes else { return false }
        return lastNudgeDay != DayKey.string(for: now, calendar: calendar)
    }
}
