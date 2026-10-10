// Draws the 1024×1024 app icon: Peeko hanging from a menu bar strip.
// Usage: swift scripts/make-icon.swift <output.png>
import AppKit

let size = 1024
let night = NSColor(srgbRed: 0x15 / 255, green: 0x15 / 255, blue: 0x15 / 255, alpha: 1)
let nightTop = NSColor(srgbRed: 0x22 / 255, green: 0x22 / 255, blue: 0x21 / 255, alpha: 1)
let strip = NSColor(srgbRed: 0x2E / 255, green: 0x2E / 255, blue: 0x2C / 255, alpha: 1)
let stripDot = NSColor(srgbRed: 0x55 / 255, green: 0x55 / 255, blue: 0x52 / 255, alpha: 1)
let green = NSColor(srgbRed: 0x81 / 255, green: 0xDD / 255, blue: 0x86 / 255, alpha: 1)

guard CommandLine.arguments.count == 2 else {
    FileHandle.standardError.write("usage: swift make-icon.swift <output.png>\n".data(using: .utf8)!)
    exit(2)
}

let rep = NSBitmapImageRep(
    bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
)!
rep.size = NSSize(width: size, height: size)

NSGraphicsContext.saveGraphicsState()
let context = NSGraphicsContext(bitmapImageRep: rep)!
NSGraphicsContext.current = context
let cg = context.cgContext
// Flip so y grows downward, matching the brand file's CSS geometry.
cg.translateBy(x: 0, y: CGFloat(size))
cg.scaleBy(x: 1, y: -1)

func roundedRect(_ rect: CGRect, top: CGFloat, bottom: CGFloat) -> CGPath {
    let p = CGMutablePath()
    p.move(to: CGPoint(x: rect.minX + top, y: rect.minY))
    p.addLine(to: CGPoint(x: rect.maxX - top, y: rect.minY))
    p.addArc(tangent1End: CGPoint(x: rect.maxX, y: rect.minY), tangent2End: CGPoint(x: rect.maxX, y: rect.minY + top), radius: top)
    p.addLine(to: CGPoint(x: rect.maxX, y: rect.maxY - bottom))
    p.addArc(tangent1End: CGPoint(x: rect.maxX, y: rect.maxY), tangent2End: CGPoint(x: rect.maxX - bottom, y: rect.maxY), radius: bottom)
    p.addLine(to: CGPoint(x: rect.minX + bottom, y: rect.maxY))
    p.addArc(tangent1End: CGPoint(x: rect.minX, y: rect.maxY), tangent2End: CGPoint(x: rect.minX, y: rect.maxY - bottom), radius: bottom)
    p.addLine(to: CGPoint(x: rect.minX, y: rect.minY + top))
    p.addArc(tangent1End: CGPoint(x: rect.minX, y: rect.minY), tangent2End: CGPoint(x: rect.minX + top, y: rect.minY), radius: top)
    p.closeSubpath()
    return p
}

// macOS icon grid: 824pt body centered on the 1024 canvas.
let body = CGRect(x: 100, y: 100, width: 824, height: 824)
let bodyPath = CGPath(roundedRect: body, cornerWidth: 185, cornerHeight: 185, transform: nil)

cg.saveGState()
// Shadow offsets ignore the flipped CTM, so negative y means down.
cg.setShadow(offset: CGSize(width: 0, height: -12), blur: 28, color: NSColor.black.withAlphaComponent(0.35).cgColor)
cg.addPath(bodyPath)
cg.setFillColor(night.cgColor)
cg.fillPath()
cg.restoreGState()

cg.saveGState()
cg.addPath(bodyPath)
cg.clip()

let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB), colors: [nightTop.cgColor, night.cgColor] as CFArray, locations: [0, 1])!
cg.drawLinearGradient(gradient, start: CGPoint(x: 0, y: body.minY), end: CGPoint(x: 0, y: body.maxY), options: [])

// Peeko, in the brand's 72×60 face units scaled ×10, hanging from the strip.
let s: CGFloat = 10
let stripBottom: CGFloat = 190
let origin = CGPoint(x: 512 - 36 * s, y: stripBottom)
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

// The menu bar it hangs from, drawn last so the head tucks under it.
cg.setFillColor(strip.cgColor)
cg.fill(CGRect(x: body.minX, y: body.minY, width: body.width, height: stripBottom - body.minY))
cg.setFillColor(stripDot.cgColor)
for (i, w) in [CGFloat(54), 34, 34].enumerated() {
    let x = 820 - CGFloat(i) * 62 - w
    cg.addPath(CGPath(roundedRect: CGRect(x: x, y: 136, width: w, height: 18), cornerWidth: 9, cornerHeight: 9, transform: nil))
}
cg.fillPath()

cg.restoreGState()
NSGraphicsContext.restoreGraphicsState()

let png = rep.representation(using: .png, properties: [:])!
try png.write(to: URL(fileURLWithPath: CommandLine.arguments[1]))
print("Wrote \(CommandLine.arguments[1])")
