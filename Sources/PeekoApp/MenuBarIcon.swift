import AppKit

/// Eyes-only template image, so macOS tints it for light and dark menu bars.
@MainActor
enum MenuBarIcon {
    static func image(for mood: Mood) -> NSImage {
        let image = NSImage(size: NSSize(width: 22, height: 16), flipped: true) { _ in
            NSColor.black.set()
            for cx in [7.0, 15.0] {
                draw(mood, centerX: cx, centerY: 8)
            }
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = "Peeko"
        return image
    }

    private static func draw(_ mood: Mood, centerX cx: CGFloat, centerY cy: CGFloat) {
        switch mood {
        case .dozing:
            NSBezierPath(roundedRect: NSRect(x: cx - 3, y: cy - 1, width: 6, height: 2), xRadius: 1, yRadius: 1).fill()
        case .looking:
            let ring = NSBezierPath(ovalIn: NSRect(x: cx - 3.5, y: cy - 3.5, width: 7, height: 7))
            ring.lineWidth = 1.5
            ring.stroke()
            NSBezierPath(ovalIn: NSRect(x: cx - 1, y: cy - 1, width: 3.2, height: 3.2)).fill()
        case .beaming:
            let arch = NSBezierPath()
            arch.move(to: NSPoint(x: cx - 3.5, y: cy + 1.5))
            arch.curve(
                to: NSPoint(x: cx + 3.5, y: cy + 1.5),
                controlPoint1: NSPoint(x: cx - 3, y: cy - 3.5),
                controlPoint2: NSPoint(x: cx + 3, y: cy - 3.5)
            )
            arch.lineWidth = 2
            arch.lineCapStyle = .round
            arch.stroke()
        }
    }
}
