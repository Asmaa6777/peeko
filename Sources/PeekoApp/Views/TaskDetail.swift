import AppKit
import PeekoCore

enum TaskDetail {
    static func describe(_ task: TaskDefinition) -> String {
        switch task.type {
        case .manual:
            return "tick it yourself"
        case .appTime:
            let minutes = task.params.minutes.map { "\($0) min" } ?? "time"
            let apps = appNames(task)
            return apps.isEmpty ? minutes : "\(minutes) in \(apps)"
        case .appTime where task.params.continuous == true:
            return "\(duration(task.params.minutes ?? 0)) straight in \(appNames(task))"
        case .appLimit where task.params.minutes == 0:
            return "none \(limitTarget(task)) before \(task.params.until ?? "noon")"
        case .appLimit:
            return "under \(duration(task.params.minutes ?? 0)) \(limitTarget(task))"
        case .remoteCheck:
            switch task.params.source {
            case "githubPush": return "a public push today"
            case "feed": return feedHost(task).map { "a new post on \($0)" } ?? "add a feed link in Catalog"
            case "duolingo": return (task.params.username ?? "").isEmpty ? "add your username in Catalog" : "a lesson today · @\(task.params.username!)"
            default: return "online check"
            }
        case .activity:
            let p = task.params
            let from = p.from ?? "", until = p.until ?? ""
            switch p.rule {
            case "startBetween": return "first on the Mac before \(until)"
            case "gapBetween" where (p.count ?? 1) > 1: return "\(p.count!) breaks of \(p.gapMinutes ?? 5)+ min, \(from)–\(until)"
            case "gapBetween": return "a \(p.gapMinutes ?? 30)-min break between \(from) and \(until)"
            default: return until == "24:00" ? "no screen time after \(from)" : "no screen time \(from)–\(until)"
            }
        case .folderActivity:
            guard let folder = folderName(task) else { return "pick a folder in Catalog" }
            if task.params.dailyNote == true { return "today's dated note in \(folder)" }
            let words = task.params.minWords ?? 0
            return words > 0 ? "\(words) words in \(folder)" : "any note in \(folder)"
        case .gitCommits:
            guard let folder = folderName(task) else { return "pick your code folder in Catalog" }
            return "a commit in \(folder) today"
        case .backup:
            return "a Time Machine backup today"
        case .reminders:
            return "everything due today, done"
        case .folderTidy:
            return "\(task.params.maxItems ?? 5) things or fewer on the Desktop after \(task.params.from ?? "17:00")"
        }
    }

    /// "45 min", "8 h", "1 h 30 min".
    static func duration(_ minutes: Int) -> String {
        if minutes < 60 { return "\(minutes) min" }
        let h = minutes / 60, m = minutes % 60
        return m == 0 ? "\(h) h" : "\(h) h \(m) min"
    }

    static func feedHost(_ task: TaskDefinition) -> String? {
        FeedVerifier.url(task.params.url)?.host?.replacingOccurrences(of: "www.", with: "")
    }

    static func done(_ task: TaskDefinition, at date: Date) -> String {
        let time = date.formatted(date: .omitted, time: .shortened)
        if let note = task.params.doneNote { return note.replacingOccurrences(of: "{time}", with: time) }
        switch task.type {
        case .manual: return "done · \(time)"
        case .activity where task.params.rule == "quietBetween": return "looks like you slept on time"
        case .activity: return "up and about at \(time)"
        case .appLimit: return "stayed under · nice"
        default: return "Peeko saw that! · \(time)"
        }
    }

    /// Gentle on purpose: these are hints, not verdicts.
    static func missed(_ task: TaskDefinition) -> String {
        if let note = task.params.missedNote { return note }
        switch task.type {
        case .activity where task.params.rule == "quietBetween": return "up late last night · no worries"
        case .activity: return "a slow morning · no worries"
        case .appLimit: return "over today's \(duration(task.params.minutes ?? 0)) · fresh start tomorrow"
        default: return "not today · no worries"
        }
    }

    /// "of YouTube" for title filters, "in Discord" for whole apps.
    static func limitTarget(_ task: TaskDefinition) -> String {
        if let titles = task.params.titleContains, !titles.isEmpty {
            return "of " + titles.map { $0.trimmingCharacters(in: CharacterSet(charactersIn: " /")) }.joined(separator: " / ")
        }
        let apps = appNames(task)
        return apps.isEmpty ? "of screen time" : "in " + apps
    }

    static func folderName(_ task: TaskDefinition) -> String? {
        guard let path = task.params.path, !path.isEmpty else { return nil }
        return URL(fileURLWithPath: path).lastPathComponent
    }

    static func appNames(_ task: TaskDefinition) -> String {
        (task.params.bundleIDs ?? []).map(appName).joined(separator: " / ")
    }

    static func appName(_ bundleID: String) -> String {
        if let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) {
            return FileManager.default.displayName(atPath: url.path)
                .replacingOccurrences(of: ".app", with: "")
        }
        return bundleID.split(separator: ".").last.map(String.init) ?? bundleID
    }
}
