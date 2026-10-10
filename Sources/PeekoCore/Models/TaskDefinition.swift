import Foundation

public enum VerifierType: String, Codable, Hashable, Sendable {
    case manual
    case appTime
    case remoteCheck
    case appLimit
}

public struct TaskParams: Codable, Hashable, Sendable {
    public var bundleIDs: [String]?
    public var minutes: Int?
    public var idleCutoffSeconds: Int?
    public var source: String?
    public var username: String?

    public init(
        bundleIDs: [String]? = nil,
        minutes: Int? = nil,
        idleCutoffSeconds: Int? = nil,
        source: String? = nil,
        username: String? = nil
    ) {
        self.bundleIDs = bundleIDs
        self.minutes = minutes
        self.idleCutoffSeconds = idleCutoffSeconds
        self.source = source
        self.username = username
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
