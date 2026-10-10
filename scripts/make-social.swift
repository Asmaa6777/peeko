// Draws the 1280×640 GitHub social preview. Content stays inside the 40px safe border.
// Usage: swift scripts/make-social.swift <output.png>
import AppKit

let width = 1280, height = 640

func rgb(_ hex: Int, _ alpha: CGFloat = 1) -> NSColor {
    NSColor(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255, blue: CGFloat(hex & 0xFF) / 255, alpha: alpha)
}
let night = rgb(0x151515), chalk = rgb(0xF2F2EF), muted = rgb(0xA9A9A5)
let green = rgb(0x81DD86), strip = rgb(0x2B2B2B), stripText = rgb(0x9A9A96), card = rgb(0x222221)

guard CommandLine.arguments.count == 2 else {
    FileHandle.standardError.write("usage: swift make-social.swift <output.png>\n".data(using: .utf8)!)
    exit(2)
}

let rep = NSBitmapImageRep(
    bitmapDataPlanes: nil, pixelsWide: width, pixelsHigh: height,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
)!
let base = NSGraphicsContext(bitmapImageRep: rep)!
let cg = base.cgContext
cg.translateBy(x: 0, y: CGFloat(height))
cg.scaleBy(x: 1, y: -1)
// A flipped NSGraphicsContext so text draws upright in these top-left coordinates.
NSGraphicsContext.current = NSGraphicsContext(cgContext: cg, flipped: true)

func roundedFont(_ size: CGFloat, _ weight: NSFont.Weight) -> NSFont {
    let font = NSFont.systemFont(ofSize: size, weight: weight)
    return NSFont(descriptor: font.fontDescriptor.withDesign(.rounded) ?? font.fontDescriptor, size: size) ?? font
}
func mono(_ size: CGFloat, _ weight: NSFont.Weight = .regular) -> NSFont {
    NSFont.monospacedSystemFont(ofSize: size, weight: weight)
}
func text(_ string: String, _ font: NSFont, _ color: NSColor, at point: CGPoint, kern: CGFloat = 0, lineHeight: CGFloat? = nil) {
    let style = NSMutableParagraphStyle()
    if let lineHeight { style.minimumLineHeight = lineHeight; style.maximumLineHeight = lineHeight }
    NSAttributedString(string: string, attributes: [.font: font, .foregroundColor: color, .kern: kern, .paragraphStyle: style])
        .draw(at: point)
}
func roundedRect(_ rect: CGRect, top: CGFloat, bottom: CGFloat) -> CGPath {
    let p = CGMutablePath()
    p.move(to: CGPoint(x: rect.minX + top, y: rect.minY))
    p.addArc(tangent1End: CGPoint(x: rect.maxX, y: rect.minY), tangent2End: CGPoint(x: rect.maxX, y: rect.maxY), radius: top)
    p.addArc(tangent1End: CGPoint(x: rect.maxX, y: rect.maxY), tangent2End: CGPoint(x: rect.minX, y: rect.maxY), radius: bottom)
    p.addArc(tangent1End: CGPoint(x: rect.minX, y: rect.maxY), tangent2End: CGPoint(x: rect.minX, y: rect.minY), radius: bottom)
    p.addArc(tangent1End: CGPoint(x: rect.minX, y: rect.minY), tangent2End: CGPoint(x: rect.maxX, y: rect.minY), radius: top)
    p.closeSubpath()
    return p
}

// Background
cg.setFillColor(night.cgColor)
cg.fill(CGRect(x: 0, y: 0, width: width, height: height))

// Peeko, in the brand's 72×60 face units, hanging from the menu bar.
let stripHeight: CGFloat = 44
let s: CGFloat = 5.2
let origin = CGPoint(x: 96, y: stripHeight)
func unit(_ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat) -> CGRect {
    CGRect(x: origin.x + x * s, y: origin.y + y * s, width: w * s, height: h * s)
}
cg.addPath(roundedRect(unit(6, -12, 60, 62), top: 30 * s, bottom: 24 * s))
cg.setFillColor(green.cgColor)
cg.fillPath()
cg.setFillColor(NSColor.white.cgColor)
cg.fillEllipse(in: unit(16, 16, 16, 16))
cg.fillEllipse(in: unit(40, 16, 16, 16))
cg.setFillColor(night.cgColor)
cg.fillEllipse(in: unit(23, 19, 7, 7))
cg.fillEllipse(in: unit(47, 19, 7, 7))

// Menu bar, drawn over the top of the head.
cg.setFillColor(strip.cgColor)
cg.fill(CGRect(x: 0, y: 0, width: CGFloat(width), height: stripHeight))
let status = "Wi-Fi    100%    Sat 10 Oct  14:02"
let statusFont = mono(17)
let statusWidth = (status as NSString).size(withAttributes: [.font: statusFont]).width
text(status, statusFont, stripText, at: CGPoint(x: CGFloat(width) - 64 - statusWidth, y: 12))

// Wordmark and tagline
let wordmarkX: CGFloat = 520
text("peeko", roundedFont(188, .heavy), chalk, at: CGPoint(x: wordmarkX - 8, y: 70), kern: -9)
text("peeks in, ticks off", mono(30), muted, at: CGPoint(x: wordmarkX, y: 300))

// Description
text("A macOS menu bar app that keeps your\ndaily list and ticks things off by itself.",
     NSFont.systemFont(ofSize: 30, weight: .medium), chalk, at: CGPoint(x: 96, y: 420), lineHeight: 42)
text("Local-first · no account · Apple silicon", mono(20), muted, at: CGPoint(x: 96, y: 522))

// Notification card
let cardRect = CGRect(x: 768, y: 410, width: 432, height: 136)
cg.addPath(CGPath(roundedRect: cardRect, cornerWidth: 22, cornerHeight: 22, transform: nil))
cg.setFillColor(card.cgColor)
cg.fillPath()
cg.addPath(CGPath(roundedRect: cardRect.insetBy(dx: 0.5, dy: 0.5), cornerWidth: 22, cornerHeight: 22, transform: nil))
cg.setStrokeColor(NSColor.white.withAlphaComponent(0.08).cgColor)
cg.setLineWidth(1)
cg.strokePath()
cg.setFillColor(green.cgColor)
cg.fillEllipse(in: CGRect(x: cardRect.minX + 26, y: cardRect.minY + 34, width: 14, height: 14))
text("Peeko saw that!", NSFont.systemFont(ofSize: 25, weight: .semibold), chalk, at: CGPoint(x: cardRect.minX + 54, y: cardRect.minY + 24))
text("Read for 30 minutes ✓", NSFont.systemFont(ofSize: 22, weight: .regular), chalk, at: CGPoint(x: cardRect.minX + 54, y: cardRect.minY + 60))
text("2 of 4 today · one still checking…", mono(16), muted, at: CGPoint(x: cardRect.minX + 54, y: cardRect.minY + 96))

NSGraphicsContext.current = nil
let png = rep.representation(using: .png, properties: [:])!
try png.write(to: URL(fileURLWithPath: CommandLine.arguments[1]))
print("Wrote \(CommandLine.arguments[1])")
