import Foundation

public enum BreakMessages {
    public struct Message: Equatable, Sendable {
        public var title: String
        public var body: String
    }

    /// Rotated so the reminder doesn't read the same every time.
    public static func all(minutes: Int) -> [Message] {
        let time = duration(minutes: minutes)
        return [
            Message(title: "Time for a little walk",
                    body: "That's \(time) at the screen. Stand up and walk around for 5 minutes. I'll keep an eye on things."),
            Message(title: "Stretch break?",
                    body: "You've been going for \(time). Give your legs and eyes 5 minutes off. Stand, stretch, maybe grab some water."),
            Message(title: "Peeko says: up you get",
                    body: "\(time.prefix(1).uppercased() + time.dropFirst()) in. Walk around for 5 minutes and you'll come back sharper."),
        ]
    }

    public static func duration(minutes: Int) -> String {
        minutes == 60 ? "an hour" : "\(minutes) minutes"
    }
}
