import Foundation
import Capacitor

/// App bridge view controller — the registration point for custom app-target
/// Capacitor plugins (Main.storyboard instantiates this instead of the plain
/// CAPBridgeViewController).
///
/// Why this exists: Capacitor 7 only auto-registers plugin classes listed in the
/// generated capacitor.config.json packageClassList, which `cap sync` builds by
/// scanning *installed npm packages* — plugins living in the app target (like
/// SpeakingRecorderPlugin and PlaybackAttestationPlugin) are never discovered
/// that way. `capacitorDidLoad()` is the official Capacitor 5+ hook for
/// registering them explicitly.
class OETBridgeViewController: CAPBridgeViewController {
    override open func capacitorDidLoad() {
        bridge?.registerPluginInstance(SpeakingRecorderPlugin())
        bridge?.registerPluginInstance(PlaybackAttestationPlugin())
    }
}
