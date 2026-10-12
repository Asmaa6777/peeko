import PeekoCore
import SwiftUI

/// How Peeko checks a task. Each group has its own color, used only on icons and chips,
/// never on status: rings, ticks and faces stay green.
enum TaskCategory: String, CaseIterable, Identifiable {
    case time = "Time in apps"
    case limits = "Limits"
    case writing = "Writing"
    case routine = "Sleep & mornings"
    case mac = "On your Mac"
    case online = "Online"
    case manual = "Tap it yourself"

    var id: String { rawValue }

    init(_ task: TaskDefinition) {
        switch task.type {
        case .appTime: self = .time
        case .appLimit: self = .limits
        case .folderActivity: self = .writing
        case .activity: self = .routine
        case .remoteCheck: self = .online
        case .gitCommits, .backup, .reminders, .folderTidy: self = .mac
        case .manual: self = .manual
        }
    }

    var color: Color {
        switch self {
        case .time: Theme.sky
        case .limits: Theme.blush
        case .writing: Theme.amber
        case .routine: Theme.lavender
        case .mac: Theme.teal
        case .online: Theme.peach
        case .manual: Theme.muted
        }
    }
}
