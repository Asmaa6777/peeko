import Foundation

public enum VerifierType: String, Codable, Hashable, Sendable {
    case manual
    case appTime
    case remoteCheck
    case appLimit
    case activity
    case folderActivity
}

public struct TaskParams: Codable, Hashable, Sendable {
    public var bundleIDs: [String]?
    public var minutes: Int?
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

    public init(
        bundleIDs: [String]? = nil,
        minutes: Int? = nil,
        idleCutoffSeconds: Int? = nil,
        source: String? = nil,
        username: String? = nil,
        rule: String? = nil,
        from: String? = nil,
        until: String? = nil,
        path: String? = nil,
        extensions: [String]? = nil,
        minWords: Int? = nil
    ) {
        self.bundleIDs = bundleIDs
        self.minutes = minutes
        self.idleCutoffSeconds = idleCutoffSeconds
        self.source = source
        self.username = username
        self.rule = rule
        self.from = from
        self.until = until
        self.path = path
        self.extensions = extensions
        self.minWords = minWords
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

    public var isCustom: Bool { id.hasPrefix(Self.customIDPrefix) }
}

public struct Catalog: Codable, Sendable {
    public var schemaVersion: Int
    public var presets: [TaskDefinition]

    public static func decode(_ data: Data) throws -> Catalog {
        try JSONDecoder().decode(Catalog.self, from: data)
    }
}
