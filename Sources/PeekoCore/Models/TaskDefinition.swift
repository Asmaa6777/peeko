import Foundation

public enum VerifierType: String, Codable, Hashable, Sendable {
    case manual
    case appTime
    case remoteCheck
    case appLimit
    case activity
    case folderActivity
    case gitCommits
    case backup
    case reminders
    case folderTidy
}

public struct TaskParams: Codable, Hashable, Sendable {
    public var bundleIDs: [String]?
    public var minutes: Int?
    /// `appTime` / `appLimit`: only count while the front window's title contains one of these
    /// (case-insensitive). Needs Accessibility permission.
    public var titleContains: [String]?
    public var idleCutoffSeconds: Int?
    public var source: String?
    public var username: String?
    /// `activity`: "quietBetween" or "startBetween", with `from` / `until` as "HH:mm".
    public var rule: String?
    public var from: String?
    public var until: String?
    /// `folderActivity`: absolute folder path, file extensions, and words to add (0 = any edit).
    public var path: String?
    public var extensions: [String]?
    public var minWords: Int?
    /// `folderActivity`: pass when a file named with today's date (yyyy-MM-dd) exists.
    public var dailyNote: Bool?
    /// `appTime`: count the longest unbroken run instead of the total.
    public var continuous: Bool?
    /// `activity` "gapBetween": breaks of at least `gapMinutes`, `count` times.
    public var gapMinutes: Int?
    public var count: Int?
    /// `folderTidy`: at most this many items.
    public var maxItems: Int?
    /// `remoteCheck` "feed": an RSS or Atom URL.
    public var url: String?
    /// `gitCommits`: only commits by this email (empty = anyone).
    public var author: String?
    /// Status lines in the task's own words; "{time}" becomes the time.
    public var doneNote: String?
    public var missedNote: String?

    public init(
        bundleIDs: [String]? = nil,
        minutes: Int? = nil,
        titleContains: [String]? = nil,
        idleCutoffSeconds: Int? = nil,
        source: String? = nil,
        username: String? = nil,
        rule: String? = nil,
        from: String? = nil,
        until: String? = nil,
        path: String? = nil,
        extensions: [String]? = nil,
        minWords: Int? = nil,
        dailyNote: Bool? = nil,
        continuous: Bool? = nil,
        gapMinutes: Int? = nil,
        count: Int? = nil,
        maxItems: Int? = nil,
        url: String? = nil,
        author: String? = nil,
        doneNote: String? = nil,
        missedNote: String? = nil
    ) {
        self.bundleIDs = bundleIDs
        self.minutes = minutes
        self.titleContains = titleContains
        self.idleCutoffSeconds = idleCutoffSeconds
        self.source = source
        self.username = username
        self.rule = rule
        self.from = from
        self.until = until
        self.path = path
        self.extensions = extensions
        self.minWords = minWords
        self.dailyNote = dailyNote
        self.continuous = continuous
        self.gapMinutes = gapMinutes
        self.count = count
        self.maxItems = maxItems
        self.url = url
        self.author = author
        self.doneNote = doneNote
        self.missedNote = missedNote
    }
}

public struct TaskDefinition: Codable, Hashable, Identifiable, Sendable {
    public static let customIDPrefix = "custom-"

    public var id: String
    public var title: String
    public var type: VerifierType
    public var params: TaskParams
    public var icon: String

    public init(id: String, title: String, type: VerifierType, params: TaskParams = TaskParams(), icon: String) {
        self.id = id
        self.title = title
        self.type = type
        self.params = params
        self.icon = icon
    }

    public static func customManual(title: String) -> TaskDefinition {
        TaskDefinition(
            id: customIDPrefix + UUID().uuidString.lowercased(),
            title: title,
            type: .manual,
            icon: "checkmark.circle"
        )
    }

    public static func customAppTime(bundleID: String, appName: String, minutes: Int) -> TaskDefinition {
        TaskDefinition(
            id: customIDPrefix + UUID().uuidString.lowercased(),
            title: "\(minutes) min in \(appName)",
            type: .appTime,
            params: TaskParams(bundleIDs: [bundleID], minutes: minutes, idleCutoffSeconds: 120),
            icon: "timer"
        )
    }

    public static func customAppLimit(bundleID: String, appName: String, minutes: Int) -> TaskDefinition {
        TaskDefinition(
            id: customIDPrefix + UUID().uuidString.lowercased(),
            title: "Under \(minutes) min in \(appName)",
            type: .appLimit,
            params: TaskParams(bundleIDs: [bundleID], minutes: minutes, idleCutoffSeconds: 120),
            icon: "hourglass"
        )
    }

    public var isCustom: Bool { id.hasPrefix(Self.customIDPrefix) }

    /// True when counting needs the front window's title (and so Accessibility permission).
    public var needsWindowTitle: Bool { !(params.titleContains ?? []).isEmpty }
}

public struct Catalog: Codable, Sendable {
    public var schemaVersion: Int
    public var presets: [TaskDefinition]

    public static func decode(_ data: Data) throws -> Catalog {
        try JSONDecoder().decode(Catalog.self, from: data)
    }
}
