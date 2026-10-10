import SwiftUI

enum Theme {
    static let night = Color(red: 0x15 / 255, green: 0x15 / 255, blue: 0x15 / 255)
    static let chalk = Color(red: 0xF2 / 255, green: 0xF2 / 255, blue: 0xEF / 255)
    // oklch(0.82 0.15 145) converted to sRGB.
    static let green = Color(red: 0x81 / 255, green: 0xDD / 255, blue: 0x86 / 255)
    static let muted = Color(red: 0xA9 / 255, green: 0xA9 / 255, blue: 0xA5 / 255)
    static let card = Color.white.opacity(0.05)
    static let cardHover = Color.white.opacity(0.09)

    static let contentHeight: CGFloat = 400
    static let rowTitle = Font.system(size: 14, weight: .semibold)

    static func mono(_ size: CGFloat = 11) -> Font { .system(size: size, design: .monospaced) }
    static func display(_ size: CGFloat) -> Font { .system(size: size, weight: .heavy, design: .rounded) }
}
