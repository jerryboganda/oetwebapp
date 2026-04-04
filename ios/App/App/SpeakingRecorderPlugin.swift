import Foundation
import AVFoundation
import Capacitor

@objc(SpeakingRecorderPlugin)
public class SpeakingRecorderPlugin: CAPPlugin, CAPBridgedPlugin {
    public let identifier = "SpeakingRecorderPlugin"
    public let jsName = "SpeakingRecorder"
    public let pluginMethods: [CAPPluginMethod] = [
        CAPPluginMethod(name: "start", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "pause", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "resume", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "stop", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "cancel", returnType: CAPPluginReturnPromise)
    ]

    private var recorder: AVAudioRecorder?
    private var recordingURL: URL?
    private var recordingStartedAt: Date?
    private var pausedAt: Date?
    private var pausedDuration: TimeInterval = 0
    private var recordingMimeType = "audio/mp4"

    @objc func start(_ call: CAPPluginCall) {
        let session = AVAudioSession.sharedInstance()

        let beginRecording: () -> Void = { [weak self] in
            guard let self = self else {
                call.reject("Unable to start the native recording.")
                return
            }

            do {
                let directory = FileManager.default.temporaryDirectory.appendingPathComponent("speaking-recordings", isDirectory: true)
                try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)

                let fileURL = directory.appendingPathComponent("speaking-\(UUID().uuidString).m4a")
                let settings: [String: Any] = [
                    AVFormatIDKey: Int(kAudioFormatMPEG4AAC),
                    AVSampleRateKey: 44_100,
                    AVNumberOfChannelsKey: 1,
                    AVEncoderAudioQualityKey: AVAudioQuality.high.rawValue
                ]

                self.recordingURL = fileURL
                self.recordingStartedAt = Date()
                self.pausedAt = nil
                self.pausedDuration = 0
                self.recordingMimeType = "audio/mp4"
                self.recorder = try AVAudioRecorder(url: fileURL, settings: settings)
                self.recorder?.prepareToRecord()
                self.recorder?.record()

                call.resolve([
                    "mimeType": self.recordingMimeType,
                    "fileName": fileURL.lastPathComponent,
                    "startedAt": Int(Date().timeIntervalSince1970 * 1000)
                ])
            } catch {
                self.cleanupRecorder(deleteFile: true)
                call.reject("Failed to start the native recording.")
            }
        }

        switch session.recordPermission {
        case .granted:
            do {
                try session.setCategory(.playAndRecord, mode: .spokenAudio, options: [.defaultToSpeaker, .allowBluetooth])
                try session.setActive(true, options: [])
                beginRecording()
            } catch {
                call.reject("Failed to configure the audio session.")
            }
        case .denied:
            call.reject("Microphone permission was denied.")
        case .undetermined:
            session.requestRecordPermission { granted in
                DispatchQueue.main.async {
                    if !granted {
                        call.reject("Microphone permission was denied.")
                        return
                    }

                    do {
                        try session.setCategory(.playAndRecord, mode: .spokenAudio, options: [.defaultToSpeaker, .allowBluetooth])
                        try session.setActive(true, options: [])
                        beginRecording()
                    } catch {
                        call.reject("Failed to configure the audio session.")
                    }
                }
            }
        @unknown default:
            call.reject("Microphone permission is not available on this device.")
        }
    }

    @objc func pause(_ call: CAPPluginCall) {
        guard let recorder = recorder, recorder.isRecording else {
            call.reject("No active recording.")
            return
        }

        if recorder.isPaused {
            call.resolve()
            return
        }

        recorder.pause()
        pausedAt = Date()
        call.resolve()
    }

    @objc func resume(_ call: CAPPluginCall) {
        guard let recorder = recorder, recorder.isRecording else {
            call.reject("No active recording.")
            return
        }

        if !recorder.isPaused {
            call.resolve()
            return
        }

        if let pausedAt = pausedAt {
            pausedDuration += Date().timeIntervalSince(pausedAt)
        }

        self.pausedAt = nil
        recorder.record()
        call.resolve()
    }

    @objc func stop(_ call: CAPPluginCall) {
        guard let recorder = recorder, let recordingURL = recordingURL else {
            call.reject("No active recording.")
            return
        }

        recorder.stop()
        cleanupAudioSession()

        let fileData: Data
        do {
            fileData = try Data(contentsOf: recordingURL)
        } catch {
            cleanupRecorder(deleteFile: true)
            call.reject("Failed to read the native recording.")
            return
        }

        let endedAt = Date()
        let startedAt = recordingStartedAt ?? endedAt
        let durationMs = max(0, Int((endedAt.timeIntervalSince(startedAt) - pausedDuration) * 1000))

        let base64 = fileData.base64EncodedString()
        let fileName = recordingURL.lastPathComponent

        cleanupRecorder(deleteFile: true)
        call.resolve([
            "base64": base64,
            "mimeType": recordingMimeType,
            "fileName": fileName,
            "durationMs": durationMs
        ])
    }

    @objc func cancel(_ call: CAPPluginCall) {
        cleanupRecorder(deleteFile: true)
        cleanupAudioSession()
        call.resolve()
    }

    private func cleanupRecorder(deleteFile: Bool) {
        recorder?.stop()
        recorder = nil
        recordingStartedAt = nil
        pausedAt = nil
        pausedDuration = 0

        if deleteFile, let recordingURL = recordingURL {
            try? FileManager.default.removeItem(at: recordingURL)
        }

        recordingURL = nil
    }

    private func cleanupAudioSession() {
        try? AVAudioSession.sharedInstance().setActive(false, options: [.notifyOthersOnDeactivation])
    }
}
