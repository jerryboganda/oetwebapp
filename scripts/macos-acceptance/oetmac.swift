// Helper for .github/workflows/macos-video-acceptance.yml (driven by run.sh).
// Drives the REAL installed desktop app through the macOS Accessibility API
// (no test hooks in the product), captures the screen through two system paths,
// and classifies whether the app window is hidden from those captures.
//
//   oetmac preflight                     screen-recording + accessibility trust
//   oetmac controls <controls.json>      backdrop + control windows (runs forever)
//   oetmac windows                       app window bounds + kCGWindowSharingState (JSON)
//   oetmac wait-webarea <sec>            wait for the web content to be exposed
//   oetmac move-window <x> <y>           place the app window (top-left coords)
//   oetmac wait-element <label> <sec>    wait for an element whose title/description
//                                        equals <label> (prefix "~" = contains)
//   oetmac press <label>                 AXPress it
//   oetmac focus <label>                 focus it and bring the app forward
//   oetmac key <space|return|escape>     post a key to the frontmost app
//   oetmac type-password <sec>           focus the secure field, verify, type
//                                        $OET_CI_LEARNER_PASSWORD, press Return
//   oetmac set-slider <label> <value>    set an AXSlider's value (Bunny seek bar)
//   oetmac fullscreen-state              prints true/false for the app window
//   oetmac sck-shot <out.png>            ScreenCaptureKit screenshot (macOS 14+)
//   oetmac frames <movie> <prefix> <t,…> extract PNG frames at seconds t
//   oetmac classify <png> <windows.json> <controls.json>   capture verdict (JSON)
//   oetmac dump <out.txt>                depth-limited AX tree (debug artifact)

import AVFoundation
import AppKit
import ApplicationServices
import CoreGraphics
import ImageIO
import ScreenCaptureKit
import UniformTypeIdentifiers

let bundleID = "com.oetprep.desktop"
let args = CommandLine.arguments

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data((message + "\n").utf8))
    exit(1)
}

func printJSON(_ value: Any) {
    let data = try! JSONSerialization.data(withJSONObject: value, options: [.sortedKeys])
    print(String(decoding: data, as: UTF8.self))
}

// MARK: - Accessibility

func runningApp() -> NSRunningApplication? {
    NSRunningApplication.runningApplications(withBundleIdentifier: bundleID).first
}

func appElement() -> AXUIElement {
    guard let app = runningApp() else { fail("app \(bundleID) is not running") }
    return AXUIElementCreateApplication(app.processIdentifier)
}

func attr<T>(_ element: AXUIElement, _ name: String) -> T? {
    var value: CFTypeRef?
    guard AXUIElementCopyAttributeValue(element, name as CFString, &value) == .success else { return nil }
    return value as? T
}

func children(_ element: AXUIElement) -> [AXUIElement] {
    attr(element, kAXChildrenAttribute) ?? []
}

/// Depth-first walk with depth and node caps (a full web page tree can be huge).
func walk(_ root: AXUIElement, maxDepth: Int = 60, _ visit: (AXUIElement, Int) -> Bool) {
    var stack: [(AXUIElement, Int)] = [(root, 0)]
    var visited = 0
    while let (element, depth) = stack.popLast() {
        visited += 1
        if visited > 40_000 { return }
        if visit(element, depth) { return }
        if depth < maxDepth {
            for child in children(element).reversed() { stack.append((child, depth + 1)) }
        }
    }
}

/// Title, description and (for static text) the string value.
func labels(_ element: AXUIElement) -> [String] {
    [kAXTitleAttribute, kAXDescriptionAttribute, kAXValueAttribute].compactMap { attr(element, $0) as String? }
}

func matches(_ element: AXUIElement, _ wanted: String) -> Bool {
    if wanted.hasPrefix("~") {
        let needle = String(wanted.dropFirst())
        return labels(element).contains { $0.contains(needle) }
    }
    return labels(element).contains(wanted)
}

func find(_ wanted: String, role: String? = nil) -> AXUIElement? {
    var hit: AXUIElement?
    walk(appElement()) { element, _ in
        if let role, (attr(element, kAXRoleAttribute) as String?) != role { return false }
        if matches(element, wanted) {
            hit = element
            return true
        }
        return false
    }
    return hit
}

func waitFor(_ seconds: Double, _ probe: () -> Bool) -> Bool {
    let deadline = Date().addingTimeInterval(seconds)
    repeat {
        if probe() { return true }
        usleep(500_000)
    } while Date() < deadline
    return false
}

func mainWindow() -> AXUIElement? {
    (attr(appElement(), kAXWindowsAttribute) as [AXUIElement]?)?.first
}

func activate() {
    runningApp()?.activate()
    usleep(300_000)
}

func postKey(_ code: CGKeyCode) {
    let source = CGEventSource(stateID: .hidSystemState)
    CGEvent(keyboardEventSource: source, virtualKey: code, keyDown: true)?.post(tap: .cghidEventTap)
    CGEvent(keyboardEventSource: source, virtualKey: code, keyDown: false)?.post(tap: .cghidEventTap)
    usleep(50_000)
}

func typeText(_ text: String) {
    let source = CGEventSource(stateID: .hidSystemState)
    for unit in text.utf16 {
        var character = unit
        for down in [true, false] {
            let event = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: down)
            event?.keyboardSetUnicodeString(stringLength: 1, unicodeString: &character)
            event?.post(tap: .cghidEventTap)
        }
        usleep(20_000)
    }
}

// MARK: - Windows

func appWindowInfo() -> [String: Any]? {
    guard let pid = runningApp()?.processIdentifier else { return nil }
    let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as? [[String: Any]] ?? []
    let mine = list.filter {
        ($0[kCGWindowOwnerPID as String] as? pid_t) == pid && ($0[kCGWindowLayer as String] as? Int) == 0
    }
    func area(_ info: [String: Any]) -> Double {
        let bounds = info[kCGWindowBounds as String] as? [String: Double] ?? [:]
        return (bounds["Width"] ?? 0) * (bounds["Height"] ?? 0)
    }
    guard let window = mine.max(by: { area($0) < area($1) }) else { return nil }
    let bounds = window[kCGWindowBounds as String] as? [String: Double] ?? [:]
    return [
        "x": bounds["X"] ?? 0, "y": bounds["Y"] ?? 0,
        "w": bounds["Width"] ?? 0, "h": bounds["Height"] ?? 0,
        "sharingState": window[kCGWindowSharingState as String] as? Int ?? -1,
    ]
}

// MARK: - Controls (backdrop + control windows)

func runControls(output: String) -> Never {
    let app = NSApplication.shared
    app.setActivationPolicy(.accessory)
    guard let screen = NSScreen.main else { fail("no screen") }
    let frame = screen.frame
    var windows: [NSWindow] = []

    func makeWindow(_ rect: NSRect, _ color: NSColor, _ sharing: NSWindow.SharingType, _ level: NSWindow.Level) -> NSWindow {
        let window = NSWindow(contentRect: rect, styleMask: [.borderless], backing: .buffered, defer: false)
        window.backgroundColor = color
        window.isOpaque = true
        window.hasShadow = false
        window.sharingType = sharing
        window.level = level
        window.ignoresMouseEvents = true
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        window.orderFrontRegardless()
        windows.append(window)
        return window
    }

    // Pure green backdrop just above the desktop: a window hidden from capture
    // reveals it; the Dock/menu bar stay above it.
    _ = makeWindow(frame, NSColor(srgbRed: 0, green: 1, blue: 0, alpha: 1), .readOnly,
                   NSWindow.Level(rawValue: Int(CGWindowLevelForKey(.desktopIconWindow)) + 1))
    let size = NSSize(width: 320, height: 200)
    // A (magenta, normal sharing) must appear in every valid capture — proves the
    // capture path had screen-recording permission. B (cyan, sharingType none) shows
    // what the OS itself does with the same flag the app uses.
    let aRect = NSRect(x: frame.maxX - size.width - 20, y: frame.maxY - size.height - 80,
                       width: size.width, height: size.height)
    let bRect = NSRect(x: aRect.minX, y: aRect.minY - size.height - 30, width: size.width, height: size.height)
    _ = makeWindow(aRect, NSColor(srgbRed: 1, green: 0, blue: 1, alpha: 1), .readOnly, .floating)
    _ = makeWindow(bRect, NSColor(srgbRed: 0, green: 1, blue: 1, alpha: 1), .none, .floating)
    // Blinking ticker so stream-based recorders keep receiving new frames.
    let tickerRect = NSRect(x: frame.maxX - 60, y: frame.minY + 90, width: 40, height: 40)
    let ticker = makeWindow(tickerRect, .white, .readOnly, .floating)
    Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { _ in
        ticker.backgroundColor = ticker.backgroundColor == .white ? .darkGray : .white
    }

    func topLeft(_ rect: NSRect) -> [String: Double] {
        ["x": rect.minX, "y": frame.maxY - rect.maxY, "w": rect.width, "h": rect.height]
    }
    let payload: [String: Any] = [
        "a": topLeft(aRect), "b": topLeft(bRect), "ticker": topLeft(tickerRect),
        // Excludes the menu bar and Dock, which are drawn above everything.
        "visible": topLeft(screen.visibleFrame),
        "screen": ["w": frame.width, "h": frame.height],
    ]
    let data = try! JSONSerialization.data(withJSONObject: payload, options: [.sortedKeys])
    FileManager.default.createFile(atPath: output, contents: data)
    app.run()
    exit(0)
}

// MARK: - Capture + classification

func savePNG(_ image: CGImage, _ path: String) {
    guard let destination = CGImageDestinationCreateWithURL(URL(fileURLWithPath: path) as CFURL,
                                                            UTType.png.identifier as CFString, 1, nil)
    else { fail("cannot write \(path)") }
    CGImageDestinationAddImage(destination, image, nil)
    guard CGImageDestinationFinalize(destination) else { fail("cannot write \(path)") }
}

func sckShot(_ path: String) async {
    guard #available(macOS 14.0, *) else { fail("ScreenCaptureKit screenshot needs macOS 14") }
    do {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        guard let display = content.displays.first else { fail("no display") }
        let configuration = SCStreamConfiguration()
        configuration.width = display.width
        configuration.height = display.height
        let image = try await SCScreenshotManager.captureImage(
            contentFilter: SCContentFilter(display: display, excludingWindows: []),
            configuration: configuration)
        savePNG(image, path)
    } catch {
        fail("ScreenCaptureKit capture failed: \(error)")
    }
}

func frames(_ movie: String, _ prefix: String, _ times: [Double]) async {
    let generator = AVAssetImageGenerator(asset: AVURLAsset(url: URL(fileURLWithPath: movie)))
    generator.requestedTimeToleranceBefore = .zero
    generator.requestedTimeToleranceAfter = .zero
    for time in times {
        do {
            let (image, _) = try await generator.image(at: CMTime(seconds: time, preferredTimescale: 600))
            savePNG(image, "\(prefix)-\(Int(time))s.png")
        } catch {
            FileHandle.standardError.write(Data("no frame at \(time)s: \(error)\n".utf8))
        }
    }
}

func readJSON(_ path: String) -> [String: Any] {
    guard let data = FileManager.default.contents(atPath: path),
          let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
    else { fail("cannot read \(path)") }
    return object
}

/// `fullscreen`: a hidden fullscreen window may come out black rather than showing
/// the backdrop, so black also counts as hidden — unless the player's white "Exit
/// fullscreen" icon (top-right corner) is visible, which proves a dark frame leaked.
func classify(png: String, windowsPath: String, controlsPath: String, fullscreen: Bool) {
    guard let source = CGImageSourceCreateWithURL(URL(fileURLWithPath: png) as CFURL, nil),
          let image = CGImageSourceCreateImageAtIndex(source, 0, nil)
    else { fail("cannot read \(png)") }
    let width = image.width, height = image.height
    var pixels = [UInt8](repeating: 0, count: width * height * 4)
    pixels.withUnsafeMutableBytes { buffer in
        let context = CGContext(data: buffer.baseAddress, width: width, height: height, bitsPerComponent: 8,
                                bytesPerRow: width * 4, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        // Bitmap memory is top-down: buffer row 0 is the image's top row, which
        // matches the top-left window coordinates used below.
        context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
    }

    let controls = readJSON(controlsPath)
    let appWindow = readJSON(windowsPath)
    let screen = controls["screen"] as? [String: Double] ?? [:]
    let scale = Double(width) / max(screen["w"] ?? Double(width), 1)

    func rect(_ value: Any?) -> CGRect {
        let r = value as? [String: Double] ?? [:]
        return CGRect(x: (r["x"] ?? 0) * scale, y: (r["y"] ?? 0) * scale,
                      width: (r["w"] ?? 0) * scale, height: (r["h"] ?? 0) * scale)
    }
    let a = rect(controls["a"]), b = rect(controls["b"]), ticker = rect(controls["ticker"])
    let windowRect = rect(appWindow)
    let app = fullscreen ? windowRect : windowRect.intersection(rect(controls["visible"]))

    // Fraction of sampled pixels in `area` (skipping `excluded`) that satisfy `test`.
    func fraction(_ area: CGRect, excluding excluded: [CGRect] = [], _ test: (Int, Int, Int) -> Bool) -> Double {
        let area = area.intersection(CGRect(x: 0, y: 0, width: width, height: height)).insetBy(dx: 4, dy: 4)
        guard !area.isNull, area.width > 0, area.height > 0 else { return -1 }
        var hits = 0, total = 0
        for y in stride(from: Int(area.minY), to: Int(area.maxY), by: 3) {
            for x in stride(from: Int(area.minX), to: Int(area.maxX), by: 3) {
                let point = CGPoint(x: x, y: y)
                if excluded.contains(where: { $0.insetBy(dx: -6, dy: -6).contains(point) }) { continue }
                let i = (y * width + x) * 4
                total += 1
                if test(Int(pixels[i]), Int(pixels[i + 1]), Int(pixels[i + 2])) { hits += 1 }
            }
        }
        return total == 0 ? -1 : Double(hits) / Double(total)
    }
    let magenta = fraction(a) { r, g, b in r > 200 && g < 70 && b > 200 }
    let cyan = fraction(b) { r, g, b in r < 70 && g > 200 && b > 200 }
    let appHidden = fraction(app, excluding: [a, b, ticker]) { r, g, b in
        (g > 200 && r < 70 && b < 70) || (fullscreen && r < 24 && g < 24 && b < 24)
    }
    let exitIconArea = CGRect(x: windowRect.maxX - 70 * scale, y: windowRect.minY, width: 70 * scale, height: 70 * scale)
    let exitIconVisible = fullscreen && fraction(exitIconArea) { r, g, b in r > 220 && g > 220 && b > 220 } > 0.01

    let verdict: String
    if magenta < 0.9 {
        verdict = "INCONCLUSIVE"   // control window missing: this path lacked capture permission
    } else if app.isNull || app.width < 10 || appHidden < 0 {
        verdict = "INCONCLUSIVE"   // app window not on screen
    } else if exitIconVisible {
        verdict = "VISIBLE"
    } else if appHidden >= 0.97 {
        verdict = "HIDDEN"
    } else {
        verdict = "VISIBLE"
    }
    printJSON([
        "png": png, "verdict": verdict, "controlAVisible": magenta, "controlBVisible": cyan,
        "appHiddenFraction": appHidden, "imageWidth": width, "imageHeight": height,
    ])
}

// MARK: - Commands

func requireArgs(_ count: Int, _ usage: String) {
    if args.count < count { fail("usage: oetmac \(usage)") }
}

requireArgs(2, "<command> …")
switch args[1] {
case "preflight":
    printJSON(["screenCapture": CGPreflightScreenCaptureAccess(), "accessibility": AXIsProcessTrusted()])

case "controls":
    requireArgs(3, "controls <controls.json>")
    runControls(output: args[2])

case "windows":
    guard let info = appWindowInfo() else { fail("app window not on screen") }
    printJSON(info)

case "wait-webarea":
    requireArgs(3, "wait-webarea <seconds>")
    let found = waitFor(Double(args[2]) ?? 60) {
        var hit = false
        walk(appElement(), maxDepth: 12) { element, _ in
            hit = (attr(element, kAXRoleAttribute) as String?) == "AXWebArea"
            return hit
        }
        return hit
    }
    if !found { fail("no AXWebArea within \(args[2])s") }

case "move-window":
    requireArgs(4, "move-window <x> <y>")
    guard let window = mainWindow() else { fail("no app window") }
    var point = CGPoint(x: Double(args[2]) ?? 0, y: Double(args[3]) ?? 0)
    if let value = AXValueCreate(.cgPoint, &point) {
        AXUIElementSetAttributeValue(window, kAXPositionAttribute as CFString, value)
    }

case "wait-element":
    requireArgs(4, "wait-element <label> <seconds>")
    if !waitFor(Double(args[3]) ?? 30, { find(args[2]) != nil }) {
        fail("element '\(args[2])' not found within \(args[3])s")
    }

case "press":
    requireArgs(3, "press <label>")
    guard let element = find(args[2]) else { fail("element '\(args[2])' not found") }
    let result = AXUIElementPerformAction(element, kAXPressAction as CFString)
    if result != .success { fail("AXPress '\(args[2])' failed: \(result.rawValue)") }

case "focus":
    requireArgs(3, "focus <label>")
    guard let element = find(args[2]) else { fail("element '\(args[2])' not found") }
    activate()
    AXUIElementSetAttributeValue(element, kAXFocusedAttribute as CFString, kCFBooleanTrue)
    usleep(300_000)

case "key":
    requireArgs(3, "key <space|return|escape>")
    let codes: [String: CGKeyCode] = ["space": 49, "return": 36, "escape": 53]
    guard let code = codes[args[2]] else { fail("unknown key \(args[2])") }
    activate()
    postKey(code)

case "type-password":
    requireArgs(3, "type-password <seconds>")
    guard let password = ProcessInfo.processInfo.environment["OET_CI_LEARNER_PASSWORD"], !password.isEmpty
    else { fail("OET_CI_LEARNER_PASSWORD is not set") }
    var field: AXUIElement?
    let found = waitFor(Double(args[2]) ?? 60) {
        walk(appElement()) { element, _ in
            if (attr(element, kAXSubroleAttribute) as String?) == "AXSecureTextField" {
                field = element
                return true
            }
            return false
        }
        return field != nil
    }
    guard found, let field else { fail("no password field within \(args[2])s") }
    activate()
    AXUIElementSetAttributeValue(field, kAXFocusedAttribute as CFString, kCFBooleanTrue)
    usleep(500_000)
    // Never type the password unless the secure field really has focus: a stray
    // attempt into the email field counts toward the account lockout.
    guard (attr(field, kAXFocusedAttribute) as Bool?) == true else { fail("password field did not take focus") }
    typeText(password)
    postKey(36)

case "set-slider":
    requireArgs(4, "set-slider <label> <value>")
    guard let slider = find(args[2], role: "AXSlider") else { fail("slider '\(args[2])' not exposed") }
    // WebKit only applies AXValue to a range input when it is a STRING (an NSNumber
    // is silently ignored yet still reports success), so set a string and read back.
    let target = Double(args[3]) ?? 0
    let result = AXUIElementSetAttributeValue(slider, kAXValueAttribute as CFString, args[3] as CFString)
    if result != .success { fail("setting slider '\(args[2])' failed: \(result.rawValue)") }
    usleep(800_000)
    let now = (attr(slider, kAXValueAttribute) as NSNumber?)?.doubleValue ?? -1
    if abs(now - target) > 2 { fail("slider '\(args[2])' is at \(now), not \(target)") }

case "fullscreen-state":
    guard let window = mainWindow() else { fail("no app window") }
    print((attr(window, "AXFullScreen") as Bool?) == true ? "true" : "false")

case "sck-shot":
    requireArgs(3, "sck-shot <out.png>")
    await sckShot(args[2])

case "frames":
    requireArgs(5, "frames <movie> <prefix> <t1,t2,…>")
    await frames(args[2], args[3], args[4].split(separator: ",").compactMap { Double($0) })

case "classify":
    requireArgs(5, "classify <png> <window.json> <controls.json> [fullscreen]")
    classify(png: args[2], windowsPath: args[3], controlsPath: args[4],
             fullscreen: args.count > 5 && args[5] == "fullscreen")

case "dump":
    requireArgs(3, "dump <out.txt>")
    var lines: [String] = []
    walk(appElement(), maxDepth: 40) { element, depth in
        let role: String = attr(element, kAXRoleAttribute) ?? "?"
        let subrole: String = attr(element, kAXSubroleAttribute) ?? ""
        lines.append(String(repeating: "  ", count: depth) + "\(role) \(subrole) \(labels(element))")
        return false
    }
    try? lines.joined(separator: "\n").write(toFile: args[2], atomically: true, encoding: .utf8)

default:
    fail("unknown command \(args[1])")
}
