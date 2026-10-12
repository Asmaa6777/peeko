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

/// What verifiers may need beyond the task itself.
public struct VerifierEnvironment: Sendable {
    public var githubUsername: String
    public var github: GitHubEventsClient
    public var folders: FolderScanner
    public var calendar: Calendar
    public var fetcher: CachedFetcher
    public var git: GitScanner
    public var reminders: RemindersReader

    public init(
        githubUsername: String,
        github: GitHubEventsClient,
        folders: FolderScanner,
        calendar: Calendar,
        fetcher: CachedFetcher = CachedFetcher(http: URLSessionHTTPClient()),
        git: GitScanner = GitScanner(),
        reminders: RemindersReader = RemindersReader()
    ) {
        self.githubUsername = githubUsername
        self.github = github
        self.folders = folders
        self.calendar = calendar
        self.fetcher = fetcher
        self.git = git
        self.reminders = reminders
    }
}

/// Whether a verifier exists for this task, without building one.
public func isVerifiable(_ definition: TaskDefinition) -> Bool {
    switch definition.type {
    case .manual, .appTime: true
    case .remoteCheck: ["githubPush", "feed", "duolingo"].contains(definition.params.source ?? "")
    case .activity: ActivityWindowVerifier.rules.contains(definition.params.rule ?? "")
    case .folderActivity, .gitCommits, .backup, .reminders, .folderTidy: true
    case .appLimit: definition.params.minutes != nil
    }
}

/// Returns nil for types that have no verifier yet.
public func makeVerifier(for definition: TaskDefinition, environment: VerifierEnvironment) -> (any Verifier)? {
    guard isVerifiable(definition) else { return nil }
    switch definition.type {
    case .manual:
        return ManualVerifier(definition: definition)
    case .appTime:
        return AppTimeVerifier(definition: definition)
    case .remoteCheck where definition.params.source == "feed":
        return FeedVerifier(definition: definition, fetcher: environment.fetcher, calendar: environment.calendar)
    case .remoteCheck where definition.params.source == "duolingo":
        return DuolingoVerifier(definition: definition, fetcher: environment.fetcher, calendar: environment.calendar)
    case .remoteCheck:
        let own = definition.params.username ?? ""
        return GitHubPushVerifier(
            definition: definition,
            username: own.isEmpty ? environment.githubUsername : own,
            client: environment.github,
            calendar: environment.calendar
        )
    case .activity:
        return ActivityWindowVerifier(definition: definition, calendar: environment.calendar)
    case .folderActivity:
        return FolderActivityVerifier(definition: definition, scanner: environment.folders, calendar: environment.calendar)
    case .appLimit:
        return AppLimitVerifier(definition: definition)
    case .gitCommits:
        return GitCommitsVerifier(definition: definition, scanner: environment.git, calendar: environment.calendar)
    case .backup:
        return BackupVerifier(definition: definition, calendar: environment.calendar)
    case .reminders:
        return RemindersVerifier(definition: definition, reader: environment.reminders, calendar: environment.calendar)
    case .folderTidy:
        return FolderTidyVerifier(definition: definition, calendar: environment.calendar)
    }
}
