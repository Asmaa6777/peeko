import SwiftUI

enum Theme {
    static let night = Color(red: 0x15 / 255, green: 0x15 / 255, blue: 0x15 / 255)
    static let chalk = Color(red: 0xF2 / 255, green: 0xF2 / 255, blue: 0xEF / 255)
    // oklch(0.82 0.15 145) converted to sRGB.
    static let green = Color(red: 0x81 / 255, green: 0xDD / 255, blue: 0x86 / 255)
    // oklch(0.82 0.15 330) converted to sRGB.
    static let blush = Color(red: 0xFA / 255, green: 0x9F / 255, blue: 0xF2 / 255)
    // Task-group colors: the same recipe, oklch(0.82 0.15 hue), so they read as one family.
    static let sky = Color(red: 0x66 / 255, green: 0xCD / 255, blue: 0xFF / 255)       // hue 245
    static let amber = Color(red: 0xF0 / 255, green: 0xBB / 255, blue: 0x3B / 255)     // hue 85
    static let lavender = Color(red: 0xCB / 255, green: 0xAF / 255, blue: 0xFF / 255)  // hue 295
    static let peach = Color(red: 0xFF / 255, green: 0xA2 / 255, blue: 0x6E / 255)     // hue 45
    // Hue 195 at chroma 0.15 falls outside sRGB, so teal uses 0.11.
    static let teal = Color(red: 0x5E / 255, green: 0xDA / 255, blue: 0xDA / 255)
    static let muted = Color(red: 0xA9 / 255, green: 0xA9 / 255, blue: 0xA5 / 255)
    static let card = Color.white.opacity(0.05)
    static let cardHover = Color.white.opacity(0.09)

    static let contentHeight: CGFloat = 400
    static let rowTitle = Font.system(size: 14, weight: .semibold)

    static func mono(_ size: CGFloat = 11) -> Font { .system(size: size, design: .monospaced) }
    static func display(_ size: CGFloat) -> Font { .system(size: size, weight: .heavy, design: .rounded) }
}
