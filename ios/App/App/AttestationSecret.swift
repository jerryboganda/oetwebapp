import Foundation

/// Build-time attestation secret for app-only video playback.
///
/// Mechanism: CI (.github/workflows/mobile-release.yml) injects the
/// OET_MOBILE_ATTEST_SECRET repository secret into Info.plist under the
/// OET_ATTEST_SECRET key via PlistBuddy just before `xcodebuild archive`, so the
/// value ships inside the signed app bundle without ever being committed to the
/// repo or exposed to JavaScript. (A Run Script / generated-source approach was
/// rejected as too fragile to wire headlessly; the plist key is set at build
/// time only — the checked-in Info.plist deliberately has no such key.)
///
/// Debug builds without the key fall back to the shared public dev constant;
/// release builds without it return nil and PlaybackAttestationPlugin refuses
/// to sign ("attestation secret not embedded").
enum AttestationSecret {
    static let devFallback = "dev-attestation-secret-not-for-production"

    static func resolve() -> String? {
        if let value = Bundle.main.object(forInfoDictionaryKey: "OET_ATTEST_SECRET") as? String,
           !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            return value
        }
        #if DEBUG
        return devFallback
        #else
        return nil
        #endif
    }
}
