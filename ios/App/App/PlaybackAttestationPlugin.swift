import Foundation
import Capacitor
import CryptoKit

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

    @objc func setSecureScreen(_ call: CAPPluginCall) {
        // iOS has no direct FLAG_SECURE equivalent. The known workaround (re-parenting
        // the window layer into a UITextField with isSecureTextEntry) is fragile to
        // undo and has broken across iOS point releases (risking a black-screened
        // app), so this resolves honestly as a no-op instead of best-effort hacking.
        // TODO: revisit with UIScreen.capturedDidChangeNotification-driven overlay or
        // a maintained secure-view implementation if screen-capture blocking becomes
        // a hard requirement on iOS.
        _ = call.getBool("enabled") ?? false
        call.resolve(["ok": false])
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
