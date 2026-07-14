import Foundation
import Capacitor
import CryptoKit
import UIKit

/// Native HMAC attestation for app-only video playback.
///
/// The backend issues a short-lived nonce challenge; `sign` signs
/// "{nonce}|{videoId}|{userId}|capacitor-ios|v1" with HMAC-SHA256 and returns the
/// signature as lowercase hex. The secret lives only in native code (see
/// AttestationSecret.swift) — it is never readable from JavaScript, which can only
/// obtain signatures over server-issued, server-verified nonces.
@objc(PlaybackAttestationPlugin)
public class PlaybackAttestationPlugin: CAPPlugin, CAPBridgedPlugin {
    public let identifier = "PlaybackAttestationPlugin"
    public let jsName = "PlaybackAttestation"
    public let pluginMethods: [CAPPluginMethod] = [
        CAPPluginMethod(name: "sign", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "setSecureScreen", returnType: CAPPluginReturnPromise)
    ]

    private static let platform = "capacitor-ios"
    private static let keyId = "v1"
    private static let maxInputLength = 512

    @objc func sign(_ call: CAPPluginCall) {
        guard let nonce = requireChallengeParam(call, "nonce"),
              let videoId = requireChallengeParam(call, "videoId"),
              let userId = requireChallengeParam(call, "userId") else {
            return
        }

        guard let secret = AttestationSecret.resolve() else {
            // Release build without the CI-injected Info.plist key: fail loudly
            // rather than sign with the public dev constant.
            call.reject("attestation secret not embedded")
            return
        }

        let message = "\(nonce)|\(videoId)|\(userId)|\(Self.platform)|\(Self.keyId)"
        let key = SymmetricKey(data: Data(secret.utf8))
        let mac = HMAC<SHA256>.authenticationCode(for: Data(message.utf8), using: key)
        let signature = mac.map { String(format: "%02x", $0) }.joined()

        let appVersion = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"

        call.resolve([
            "signature": signature,
            "platform": Self.platform,
            "keyId": Self.keyId,
            "appVersion": appVersion
        ])
    }

    // Screen-capture blackout state (main-thread only).
    private var captureObserver: NSObjectProtocol?
    private var blackoutView: UIView?

    @objc func setSecureScreen(_ call: CAPPluginCall) {
        // iOS has no FLAG_SECURE, and it cannot make an ARBITRARY view render black
        // in a still SCREENSHOT (only DRM/AVPlayer-protected content can). What it
        // CAN do — and what actually matters for video piracy — is blank the app
        // during active screen RECORDING / mirroring, which is exactly what
        // UIScreen.isCaptured reports. We overlay an opaque black view whenever a
        // capture is in progress and observe capturedDidChangeNotification so the
        // blackout tracks recording start/stop for the lifetime of playback.
        let enabled = call.getBool("enabled") ?? false
        DispatchQueue.main.async {
            if enabled {
                self.enableCaptureBlackout()
            } else {
                self.disableCaptureBlackout()
            }
            // ok=true when we engaged the recording/mirroring blackout; the JS side
            // treats this as best-effort hardening, never a gate.
            call.resolve(["ok": enabled])
        }
    }

    private func enableCaptureBlackout() {
        if captureObserver == nil {
            captureObserver = NotificationCenter.default.addObserver(
                forName: UIScreen.capturedDidChangeNotification, object: nil, queue: .main
            ) { [weak self] _ in
                self?.applyBlackoutForCaptureState()
            }
        }
        applyBlackoutForCaptureState()
    }

    private func disableCaptureBlackout() {
        if let observer = captureObserver {
            NotificationCenter.default.removeObserver(observer)
            captureObserver = nil
        }
        blackoutView?.removeFromSuperview()
        blackoutView = nil
    }

    private func applyBlackoutForCaptureState() {
        guard let container = self.bridge?.viewController?.view else { return }
        if UIScreen.main.isCaptured {
            if blackoutView == nil {
                let view = UIView(frame: container.bounds)
                view.backgroundColor = .black
                view.autoresizingMask = [.flexibleWidth, .flexibleHeight]
                view.isUserInteractionEnabled = false
                blackoutView = view
            }
            if let view = blackoutView, view.superview == nil {
                container.addSubview(view)
                container.bringSubviewToFront(view)
            }
        } else {
            blackoutView?.removeFromSuperview()
        }
    }

    /// Reads a required challenge param; rejects the call and returns nil when the
    /// param is missing, empty, or oversized (mirrors the Android/desktop validation).
    private func requireChallengeParam(_ call: CAPPluginCall, _ name: String) -> String? {
        guard let value = call.getString(name),
              !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            call.reject("A non-empty '\(name)' parameter is required.")
            return nil
        }
        guard value.count < Self.maxInputLength else {
            call.reject("'\(name)' exceeds the maximum length.")
            return nil
        }
        return value
    }
}
