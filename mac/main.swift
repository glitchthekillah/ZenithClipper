// ZenithClipper for macOS: one executable, three jobs.
//   (no arguments)  menu bar app: starts the engine, owns the shortcuts and the menu
//   --size M W      prints "WIDTHxHEIGHT HZ" for display M scaled to width W (0 = native, capped at 2560)
//   --capture M SIZE FPS AUDIO EXCLUDED
//                   ScreenCaptureKit capture: paced NV12 frames to stdout, 48 kHz s16le stereo system audio
//                   to the AUDIO fifo ("-" for none), apps in EXCLUDED (comma separated) kept out of the audio.
import Cocoa
import ScreenCaptureKit
import CoreMedia
import CoreVideo
import Carbon.HIToolbox
import ServiceManagement

func fail(_ message: String, _ code: Int32 = 1) -> Never {
    FileHandle.standardError.write((message + "\n").data(using: .utf8)!)
    exit(code)
}
let permissionHelp = "Screen Recording permission is needed. Open System Settings > Privacy & Security > Screen Recording, turn on ZenithClipper, then quit and reopen it."

// MARK: Displays

/// Main display first, then the rest, so monitor 0 is always the one with the menu bar.
func orderedDisplays(_ content: SCShareableContent) -> [SCDisplay] {
    let main = CGMainDisplayID()
    return content.displays.sorted { a, b in (a.displayID == main ? 0 : 1, a.displayID) < (b.displayID == main ? 0 : 1, b.displayID) }
}
func pickDisplay(_ content: SCShareableContent, _ index: Int) -> SCDisplay {
    let all = orderedDisplays(content)
    guard !all.isEmpty else { fail("No display found to record.") }
    return all[max(0, min(index, all.count - 1))]
}
func frameSize(_ display: SCDisplay, _ requested: Int) -> (Int, Int) {
    var pw = display.width, ph = display.height
    if let mode = CGDisplayCopyDisplayMode(display.displayID) { pw = mode.pixelWidth; ph = mode.pixelHeight }
    var w = requested > 0 ? min(requested, pw) : min(pw, 2560)
    w &= ~1
    var h = Int((Double(w) * Double(ph) / Double(pw)).rounded())
    h &= ~1
    if h < 2 { h = 2 }
    if w < 2 { w = 2 }
    return (w, h)
}
func refreshRate(_ display: SCDisplay) -> Int {
    if let screen = NSScreen.screens.first(where: { ($0.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value == display.displayID }) {
        let fps = screen.maximumFramesPerSecond
        if fps > 0 { return fps }
    }
    if let mode = CGDisplayCopyDisplayMode(display.displayID), mode.refreshRate > 0 { return Int(mode.refreshRate.rounded()) }
    return 60
}

// MARK: Capture

@discardableResult
func writeAll(_ fd: Int32, _ base: UnsafeRawPointer, _ count: Int) -> Bool {
    var offset = 0
    while offset < count {
        let n = write(fd, base + offset, count - offset)
        if n < 0 { if errno == EINTR { continue }; return false }
        if n == 0 { return false }
        offset += n
    }
    return true
}

final class Capturer: NSObject, SCStreamOutput, SCStreamDelegate {
    let monitor: Int, width: Int, height: Int, fps: Int
    let audioPath: String?, excluded: [String]
    var videoStream: SCStream?, audioStream: SCStream?
    let videoQueue = DispatchQueue(label: "zc.video"), audioQueue = DispatchQueue(label: "zc.audio")
    let frameLock = NSLock(); var latest: [UInt8]?
    let audioLock = NSLock(); var ring: [Int16] = []; var audioOpen = false

    init(monitor: Int, width: Int, height: Int, fps: Int, audioPath: String?, excluded: [String]) {
        self.monitor = monitor; self.width = width; self.height = height; self.fps = fps; self.audioPath = audioPath; self.excluded = excluded
    }

    func start() async throws {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
        let display = pickDisplay(content, monitor)

        // Video: the whole display, nothing hidden, scaled on the GPU to the clip size.
        let video = SCStreamConfiguration()
        video.width = width; video.height = height
        video.pixelFormat = kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange
        video.minimumFrameInterval = CMTime(value: 1, timescale: CMTimeScale(fps))
        video.showsCursor = true
        video.queueDepth = 5
        let vs = SCStream(filter: SCContentFilter(display: display, excludingWindows: []), configuration: video, delegate: self)
        try vs.addStreamOutput(self, type: .screen, sampleHandlerQueue: videoQueue)
        videoStream = vs

        // Audio: a second stream so excluded apps leave the sound but stay visible in the picture.
        if audioPath != nil {
            let skip = content.applications.filter { app in
                app.bundleIdentifier == Bundle.main.bundleIdentifier || excluded.contains { name in
                    !name.isEmpty && (app.applicationName.localizedCaseInsensitiveContains(name) || app.bundleIdentifier.localizedCaseInsensitiveContains(name))
                }
            }
            let audio = SCStreamConfiguration()
            audio.width = 2; audio.height = 2
            audio.minimumFrameInterval = CMTime(value: 1, timescale: 1)
            audio.capturesAudio = true
            audio.sampleRate = 48000
            audio.channelCount = 2
            audio.excludesCurrentProcessAudio = true
            let filter = SCContentFilter(display: display, excludingApplications: skip, exceptingWindows: [])
            let au = SCStream(filter: filter, configuration: audio, delegate: self)
            try au.addStreamOutput(self, type: .audio, sampleHandlerQueue: audioQueue)
            audioStream = au
            startAudioWriter()
        }
        try await vs.startCapture()
        if let au = audioStream { try await au.startCapture() }
        startVideoWriter()
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        fail("Screen capture stopped: \(error.localizedDescription)", 3)
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        if type == .screen { takeFrame(sampleBuffer) } else if type == .audio { takeAudio(sampleBuffer) }
    }

    func takeFrame(_ sample: CMSampleBuffer) {
        guard let pixels = CMSampleBufferGetImageBuffer(sample) else { return }
        CVPixelBufferLockBaseAddress(pixels, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(pixels, .readOnly) }
        guard CVPixelBufferGetPlaneCount(pixels) >= 2,
              let y = CVPixelBufferGetBaseAddressOfPlane(pixels, 0),
              let uv = CVPixelBufferGetBaseAddressOfPlane(pixels, 1) else { return }
        let yStride = CVPixelBufferGetBytesPerRowOfPlane(pixels, 0), uvStride = CVPixelBufferGetBytesPerRowOfPlane(pixels, 1)
        let rows = min(height, CVPixelBufferGetHeightOfPlane(pixels, 0)), uvRows = min(height / 2, CVPixelBufferGetHeightOfPlane(pixels, 1))
        let cols = min(width, yStride, uvStride)
        var frame = [UInt8](repeating: 0, count: width * height * 3 / 2)
        frame.withUnsafeMutableBytes { dst in
            let d = dst.baseAddress!
            for r in 0..<rows { memcpy(d + r * width, y + r * yStride, cols) }
            for r in 0..<uvRows { memcpy(d + width * height + r * width, uv + r * uvStride, cols) }
        }
        frameLock.lock(); latest = frame; frameLock.unlock()
    }

    /// FFmpeg reads raw frames at a fixed rate; ScreenCaptureKit only sends frames when the screen changes,
    /// so the last frame is repeated on a steady clock.
    func startVideoWriter() {
        let t = Thread { [self] in
            let interval = 1.0 / Double(fps)
            var next = Date().timeIntervalSince1970
            while true {
                frameLock.lock(); let frame = latest; frameLock.unlock()
                if let frame {
                    let ok = frame.withUnsafeBytes { writeAll(1, $0.baseAddress!, $0.count) }
                    if !ok { exit(0) }
                    next += interval
                } else { next = Date().timeIntervalSince1970 + interval }
                let now = Date().timeIntervalSince1970
                if now - next > 0.5 { next = now }
                if next > now { Thread.sleep(forTimeInterval: next - now) }
            }
        }
        t.qualityOfService = .userInteractive; t.start()
    }

    func takeAudio(_ sample: CMSampleBuffer) {
        guard let format = CMSampleBufferGetFormatDescription(sample),
              let asbd = CMAudioFormatDescriptionGetStreamBasicDescription(format)?.pointee else { return }
        let frames = CMSampleBufferGetNumSamples(sample)
        if frames <= 0 { return }
        let list = AudioBufferList.allocate(maximumBuffers: 8)
        defer { free(list.unsafeMutablePointer) }
        var block: CMBlockBuffer?
        let status = CMSampleBufferGetAudioBufferListWithRetainedBlockBuffer(sample, bufferListSizeNeededOut: nil, bufferListOut: list.unsafeMutablePointer,
            bufferListSize: AudioBufferList.sizeInBytes(maximumBuffers: 8), blockBufferAllocator: nil, blockBufferMemoryAllocator: nil,
            flags: UInt32(kCMSampleBufferFlag_AudioBufferList_Assure16ByteAlignment), blockBufferOut: &block)
        if status != noErr { return }
        let channels = max(1, Int(asbd.mChannelsPerFrame))
        let isFloat = asbd.mFormatFlags & kAudioFormatFlagIsFloat != 0
        let planar = asbd.mFormatFlags & kAudioFormatFlagIsNonInterleaved != 0
        var out = [Int16](repeating: 0, count: frames * 2)
        for ch in 0..<2 {
            let source = min(ch, channels - 1)
            let buffer = list[planar ? min(source, list.count - 1) : 0]
            guard let data = buffer.mData else { continue }
            let stride = planar ? 1 : channels, offset = planar ? 0 : source
            for i in 0..<frames {
                let index = i * stride + offset
                if isFloat {
                    let v = data.assumingMemoryBound(to: Float32.self)[index]
                    out[i * 2 + ch] = Int16(max(-1, min(1, v)) * 32767)
                } else {
                    out[i * 2 + ch] = data.assumingMemoryBound(to: Int16.self)[index]
                }
            }
        }
        audioLock.lock()
        if audioOpen {
            ring.append(contentsOf: out)
            if ring.count > 48000 * 2 { ring.removeFirst(ring.count - 48000 * 2) }
        }
        audioLock.unlock()
    }

    /// Writes audio in real time and fills gaps with silence, so FFmpeg never waits on a quiet Mac.
    func startAudioWriter() {
        guard let path = audioPath else { return }
        let t = Thread { [self] in
            let fd = open(path, O_WRONLY)
            if fd < 0 { fail("Could not open the audio pipe.") }
            audioLock.lock(); audioOpen = true; ring.removeAll(); audioLock.unlock()
            let start = Date().timeIntervalSince1970
            var written = 0
            while true {
                let due = Int((Date().timeIntervalSince1970 - start) * 48000) - written
                if due > 0 {
                    let count = min(due, 48000)
                    var chunk = [Int16](repeating: 0, count: count * 2)
                    audioLock.lock()
                    let available = min(ring.count, chunk.count)
                    if available > 0 { chunk.replaceSubrange(0..<available, with: ring[0..<available]); ring.removeFirst(available) }
                    audioLock.unlock()
                    let ok = chunk.withUnsafeBytes { writeAll(fd, $0.baseAddress!, $0.count) }
                    if !ok { exit(0) }
                    written += count
                }
                Thread.sleep(forTimeInterval: 0.01)
            }
        }
        t.qualityOfService = .userInteractive; t.start()
    }
}

// MARK: Menu bar app

let engine = Bundle.main.resourceURL!.appendingPathComponent("app/zenithclipper").path

@discardableResult
func cli(_ arguments: [String]) -> (Int32, String) {
    let p = Process()
    p.executableURL = URL(fileURLWithPath: engine)
    p.arguments = arguments
    let pipe = Pipe(); p.standardOutput = pipe; p.standardError = pipe
    do { try p.run() } catch { return (-1, error.localizedDescription) }
    let data = pipe.fileHandleForReading.readDataToEndOfFile()
    p.waitUntilExit()
    return (p.terminationStatus, String(decoding: data, as: UTF8.self))
}
func detached(_ arguments: [String]) {
    let p = Process()
    p.executableURL = URL(fileURLWithPath: engine)
    p.arguments = arguments
    p.standardOutput = FileHandle.nullDevice; p.standardError = FileHandle.nullDevice
    try? p.run()
}
func field(_ dict: [String: Any], _ key: String) -> Any? { dict.first { $0.key.caseInsensitiveCompare(key) == .orderedSame }?.value }

/// "Alt+C", "Ctrl+Shift+F9", "Shift+~" into a Carbon key code and modifiers.
func parseHotkey(_ text: String) -> (UInt32, UInt32)? {
    let keys: [String: Int] = ["a": kVK_ANSI_A, "b": kVK_ANSI_B, "c": kVK_ANSI_C, "d": kVK_ANSI_D, "e": kVK_ANSI_E, "f": kVK_ANSI_F, "g": kVK_ANSI_G,
        "h": kVK_ANSI_H, "i": kVK_ANSI_I, "j": kVK_ANSI_J, "k": kVK_ANSI_K, "l": kVK_ANSI_L, "m": kVK_ANSI_M, "n": kVK_ANSI_N, "o": kVK_ANSI_O,
        "p": kVK_ANSI_P, "q": kVK_ANSI_Q, "r": kVK_ANSI_R, "s": kVK_ANSI_S, "t": kVK_ANSI_T, "u": kVK_ANSI_U, "v": kVK_ANSI_V, "w": kVK_ANSI_W,
        "x": kVK_ANSI_X, "y": kVK_ANSI_Y, "z": kVK_ANSI_Z, "0": kVK_ANSI_0, "1": kVK_ANSI_1, "2": kVK_ANSI_2, "3": kVK_ANSI_3, "4": kVK_ANSI_4,
        "5": kVK_ANSI_5, "6": kVK_ANSI_6, "7": kVK_ANSI_7, "8": kVK_ANSI_8, "9": kVK_ANSI_9, "~": kVK_ANSI_Grave, "`": kVK_ANSI_Grave,
        "space": kVK_Space, "f1": kVK_F1, "f2": kVK_F2, "f3": kVK_F3, "f4": kVK_F4, "f5": kVK_F5, "f6": kVK_F6, "f7": kVK_F7, "f8": kVK_F8,
        "f9": kVK_F9, "f10": kVK_F10, "f11": kVK_F11, "f12": kVK_F12]
    var mods: UInt32 = 0; var code: UInt32?
    for part in text.split(separator: "+").map({ $0.trimmingCharacters(in: .whitespaces).lowercased() }) {
        switch part {
        case "alt", "option", "opt": mods |= UInt32(optionKey)
        case "ctrl", "control": mods |= UInt32(controlKey)
        case "shift": mods |= UInt32(shiftKey)
        case "cmd", "command", "win", "super": mods |= UInt32(cmdKey)
        default: if let k = keys[part] { code = UInt32(k) } else { return nil }
        }
    }
    guard let code else { return nil }
    return (code, mods)
}

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    static var shared: AppDelegate!
    var item: NSStatusItem!
    var hotkeyRefs: [EventHotKeyRef] = []
    var clipHotkey = "Alt+C"

    func applicationDidFinishLaunching(_ notification: Notification) {
        AppDelegate.shared = self
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        let image = NSImage(systemSymbolName: "record.circle", accessibilityDescription: "ZenithClipper")
        image?.isTemplate = true
        item.button?.image = image
        let menu = NSMenu(); menu.delegate = self; item.menu = menu
        if !CGPreflightScreenCaptureAccess() { CGRequestScreenCaptureAccess() }
        _ = cli(["init"])
        detached(["daemon"])
        registerHotkeys()
    }

    func applicationWillTerminate(_ notification: Notification) { _ = cli(["shutdown"]) }

    func config() -> (String, [String: Any]) {
        let path = cli(["config-path"]).1.trimmingCharacters(in: .whitespacesAndNewlines)
        let data = (try? Data(contentsOf: URL(fileURLWithPath: path))) ?? Data("{}".utf8)
        return (path, (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] ?? [:])
    }
    func update(_ key: String, _ value: Any) {
        var (path, dict) = config()
        let existing = dict.keys.first { $0.caseInsensitiveCompare(key) == .orderedSame } ?? key
        dict[existing] = value
        if let data = try? JSONSerialization.data(withJSONObject: dict, options: [.prettyPrinted]) { try? data.write(to: URL(fileURLWithPath: path)) }
        DispatchQueue.global().async { _ = cli(["configure"]) }
    }

    func registerHotkeys() {
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, _ in
            var id = EventHotKeyID()
            GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID), nil, MemoryLayout<EventHotKeyID>.size, nil, &id)
            let which = id.id
            DispatchQueue.main.async { if which == 1 { AppDelegate.shared.save(nil) } else { AppDelegate.shared.item.button?.performClick(nil) } }
            return noErr
        }, 1, &spec, nil, nil)
        let settings = config().1
        clipHotkey = field(settings, "ClipHotkey") as? String ?? "Alt+C"
        for (index, text) in [clipHotkey, field(settings, "PanelHotkey") as? String ?? "Shift+~"].enumerated() {
            guard let (code, mods) = parseHotkey(text) else { continue }
            var ref: EventHotKeyRef?
            if RegisterEventHotKey(code, mods, EventHotKeyID(signature: OSType(0x5A434C50), id: UInt32(index + 1)), GetApplicationEventTarget(), 0, &ref) == noErr, let ref { hotkeyRefs.append(ref) }
        }
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let (code, output) = cli(["status"])
        var status: [String: Any] = [:]
        if code == 0, let data = output.data(using: .utf8), let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
            status = field(json, "Status") as? [String: Any] ?? [:]
        }
        let capturing = field(status, "Capturing") as? Bool ?? false
        let seconds = Int(field(status, "BufferedSeconds") as? Double ?? 0)
        var line: String
        if code != 0 { line = "Engine not running" }
        else if let error = field(status, "Error") as? String, !error.isEmpty { line = error.contains("permission") || error.contains("TCC") ? "Needs Screen Recording permission" : "Error: " + error }
        else if capturing { line = String(format: "Recording · %d:%02d buffered", seconds / 60, seconds % 60) }
        else { line = (field(status, "State") as? String) ?? "Paused" }
        let head = NSMenuItem(title: String(line.prefix(80)), action: nil, keyEquivalent: ""); head.isEnabled = false; menu.addItem(head)
        menu.addItem(.separator())
        add(menu, "Save clip (\(clipHotkey))", #selector(save(_:)), tag: 0)
        add(menu, "Save last 60 seconds", #selector(save(_:)), tag: 60)
        add(menu, "Save last 2 minutes", #selector(save(_:)), tag: 120)
        menu.addItem(.separator())
        add(menu, capturing ? "Pause recording" : "Resume recording", #selector(toggleRecording(_:)), tag: capturing ? 1 : 0)
        let settings = config().1
        let mic = add(menu, "Microphone in clips", #selector(toggleMic(_:)))
        mic.state = (field(settings, "CaptureMicrophone") as? Bool ?? false) ? .on : .off
        let sound = add(menu, "Clip save sound", #selector(toggleSaveSound(_:)))
        sound.state = (field(settings, "ClipSaveSound") as? Bool ?? true) ? .on : .off
        let choice = NSMenuItem(title: "Default clip save sound", action: nil, keyEquivalent: "")
        let soundMenu = NSMenu()
        let soundIds = ["01-amber", "02-bloom", "03-cedar", "04-daylight", "05-ember", "06-fern", "07-halo", "08-linen", "09-morning", "10-pebble"]
        let storedSound = field(settings, "ClipSaveSoundId") as? String ?? ""
        let currentSound = soundIds.contains(storedSound) ? storedSound : soundIds[0]
        for (index, id) in soundIds.enumerated() {
            let name = String(id.dropFirst(3)).capitalized
            let option = add(soundMenu, "\(index + 1). \(name)", #selector(chooseSaveSound(_:)))
            option.representedObject = id
            option.state = id == currentSound ? .on : .off
        }
        choice.submenu = soundMenu
        menu.addItem(choice)
        add(menu, "Clip folder…", #selector(chooseFolder(_:)))
        add(menu, "Open clips folder", #selector(openFolder(_:)))
        add(menu, "Open editor", #selector(openEditor(_:)))
        menu.addItem(.separator())
        let login = add(menu, "Open at login", #selector(toggleLogin(_:)))
        login.state = SMAppService.mainApp.status == .enabled ? .on : .off
        add(menu, "Screen Recording settings…", #selector(openPrivacy(_:)))
        add(menu, "Check for updates…", #selector(openReleases(_:)))
        menu.addItem(.separator())
        add(menu, "Quit ZenithClipper", #selector(quit(_:)))
    }
    @discardableResult
    func add(_ menu: NSMenu, _ title: String, _ action: Selector, tag: Int = 0) -> NSMenuItem {
        let i = NSMenuItem(title: title, action: action, keyEquivalent: ""); i.target = self; i.tag = tag; menu.addItem(i); return i
    }

    @objc func save(_ sender: NSMenuItem?) {
        let seconds = sender?.tag ?? 0
        DispatchQueue.global().async {
            let (code, output) = cli(seconds > 0 ? ["save", "\(seconds)"] : ["save"])
            DispatchQueue.main.async {
                if code != 0 {
                    NSSound(named: "Basso")?.play()
                    self.alert("Couldn't save the clip", output)
                }
            }
        }
    }
    @objc func toggleRecording(_ sender: NSMenuItem) { let command = sender.tag == 1 ? "pause" : "resume"; DispatchQueue.global().async { _ = cli([command]) } }
    @objc func chooseSaveSound(_ sender: NSMenuItem) {
        if let id = sender.representedObject as? String { update("ClipSaveSoundId", id) }
    }
    @objc func toggleSaveSound(_ sender: NSMenuItem) { update("ClipSaveSound", sender.state != .on) }
    @objc func toggleMic(_ sender: NSMenuItem) { update("CaptureMicrophone", sender.state != .on) }
    @objc func chooseFolder(_ sender: Any?) {
        NSApp.activate(ignoringOtherApps: true)
        let panel = NSOpenPanel(); panel.canChooseDirectories = true; panel.canChooseFiles = false; panel.canCreateDirectories = true
        panel.prompt = "Save clips here"
        if panel.runModal() == .OK, let url = panel.url { update("ClipDirectory", url.path) }
    }
    @objc func openFolder(_ sender: Any?) {
        let dir = field(config().1, "ClipDirectory") as? String ?? (NSHomeDirectory() + "/Movies/ZenithClipper")
        try? FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        NSWorkspace.shared.open(URL(fileURLWithPath: dir))
    }
    @objc func openEditor(_ sender: Any?) { detached(["editor"]) }
    @objc func toggleLogin(_ sender: NSMenuItem) {
        do { if sender.state == .on { try SMAppService.mainApp.unregister() } else { try SMAppService.mainApp.register() } }
        catch { alert("Couldn't change the login setting", error.localizedDescription) }
    }
    @objc func openPrivacy(_ sender: Any?) { NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture")!) }
    @objc func openReleases(_ sender: Any?) { NSWorkspace.shared.open(URL(string: "https://github.com/glitchthekillah/ZenithClipper/releases/latest")!) }
    @objc func quit(_ sender: Any?) { NSApp.terminate(nil) }
    func alert(_ title: String, _ text: String) {
        NSApp.activate(ignoringOtherApps: true)
        let a = NSAlert(); a.messageText = title; a.informativeText = String(text.suffix(600)); a.runModal()
    }
}

// MARK: Entry

let arguments = CommandLine.arguments
signal(SIGPIPE, SIG_IGN)
var capturer: Capturer?
if arguments.count >= 4 && arguments[1] == "--size" {
    let monitor = Int(arguments[2]) ?? 0, width = Int(arguments[3]) ?? 0
    Task {
        do {
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
            let display = pickDisplay(content, monitor)
            let (w, h) = frameSize(display, width)
            print("\(w)x\(h) \(refreshRate(display))")
            exit(0)
        } catch { fail(permissionHelp + " (" + error.localizedDescription + ")") }
    }
    dispatchMain()
} else if arguments.count >= 6 && arguments[1] == "--capture" {
    let size = arguments[3].split(separator: "x").compactMap { Int($0) }
    guard size.count == 2 else { fail("Size must look like 1920x1080.") }
    capturer = Capturer(monitor: Int(arguments[2]) ?? 0, width: size[0], height: size[1], fps: max(1, Int(arguments[4]) ?? 60),
                        audioPath: arguments[5] == "-" ? nil : arguments[5],
                        excluded: arguments.count > 6 ? arguments[6].split(separator: ",").map(String.init) : [])
    Task {
        do { try await capturer!.start() } catch { fail(permissionHelp + " (" + error.localizedDescription + ")") }
    }
    dispatchMain()
} else {
    let app = NSApplication.shared
    let delegate = AppDelegate()
    app.delegate = delegate
    app.setActivationPolicy(.accessory)
    app.run()
}
