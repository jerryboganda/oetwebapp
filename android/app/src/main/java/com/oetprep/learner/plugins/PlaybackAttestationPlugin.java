package com.oetprep.learner.plugins;

import android.os.Build;
import android.os.Debug;
import android.view.Window;
import android.view.WindowManager;

import androidx.appcompat.app.AppCompatActivity;

import com.getcapacitor.JSArray;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.annotation.CapacitorPlugin;
import com.oetprep.learner.BuildConfig;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/**
 * Native HMAC attestation for app-only video playback.
 *
 * The backend issues a short-lived nonce challenge; {@code sign} signs
 * "{nonce}|{videoId}|{userId}|capacitor-android|v1" with HMAC-SHA256 and returns the
 * signature as lowercase hex. The secret is injected at BUILD time (CI secret
 * OET_MOBILE_ATTEST_SECRET -> BuildConfig.OET_ATTEST_SECRET, see app/build.gradle) so it
 * lives only in compiled native code — never in JS. Debug builds without the CI secret
 * fall back to a public dev constant; release builds without it refuse to sign.
 *
 * {@code setSecureScreen} toggles FLAG_SECURE on the activity window so video playback
 * cannot be screenshotted / screen-recorded / mirrored while enabled — unlike iOS, Android
 * actually BLOCKS the capture at the OS level, so no screenshot-detection listener is needed
 * here (contrast {@code PlaybackAttestationPlugin.swift}, which can only detect, not block).
 *
 * {@code getIntegrity} reports best-effort root/emulator/debugger heuristics (spec §3).
 */
@CapacitorPlugin(name = "PlaybackAttestation")
public class PlaybackAttestationPlugin extends Plugin {
    private static final String PLATFORM = "capacitor-android";
    private static final String KEY_ID = "v1";
    private static final int MAX_INPUT_LENGTH = 512;
    private static final String DEV_FALLBACK_SECRET = "dev-attestation-secret-not-for-production";

    @com.getcapacitor.PluginMethod
    public void sign(PluginCall call) {
        String nonce = requireChallengeParam(call, "nonce");
        if (nonce == null) {
            return;
        }
        String videoId = requireChallengeParam(call, "videoId");
        if (videoId == null) {
            return;
        }
        String userId = requireChallengeParam(call, "userId");
        if (userId == null) {
            return;
        }

        String secret = BuildConfig.OET_ATTEST_SECRET;
        if (secret == null || secret.isEmpty()) {
            // Dev fallback ONLY for debug builds; a release build without the CI
            // secret must fail loudly rather than sign with a public constant.
            if (BuildConfig.DEBUG) {
                secret = DEV_FALLBACK_SECRET;
            } else {
                call.reject("attestation secret not embedded");
                return;
            }
        }

        String message = nonce + "|" + videoId + "|" + userId + "|" + PLATFORM + "|" + KEY_ID;

        String signature;
        try {
            signature = hmacSha256LowercaseHex(secret, message);
        } catch (GeneralSecurityException error) {
            call.reject("Failed to compute the attestation signature.", error);
            return;
        }

        JSObject result = new JSObject();
        result.put("signature", signature);
        result.put("platform", PLATFORM);
        result.put("keyId", KEY_ID);
        result.put("appVersion", BuildConfig.VERSION_NAME);
        call.resolve(result);
    }

    @com.getcapacitor.PluginMethod
    public void setSecureScreen(PluginCall call) {
        Boolean enabledValue = call.getBoolean("enabled");
        if (enabledValue == null) {
            call.reject("A boolean 'enabled' parameter is required.");
            return;
        }
        final boolean enabled = enabledValue;

        AppCompatActivity activity = getActivity();
        if (activity == null) {
            call.reject("No active activity to toggle the secure screen on.");
            return;
        }

        activity.runOnUiThread(() -> {
            Window window = activity.getWindow();
            if (window == null) {
                call.reject("No window available to toggle the secure screen on.");
                return;
            }
            if (enabled) {
                window.addFlags(WindowManager.LayoutParams.FLAG_SECURE);
            } else {
                window.clearFlags(WindowManager.LayoutParams.FLAG_SECURE);
            }
            JSObject result = new JSObject();
            result.put("ok", true);
            call.resolve(result);
        });
    }

    private static final String[] ROOT_INDICATOR_PATHS = {
        "/system/app/Superuser.apk",
        "/sbin/su",
        "/system/bin/su",
        "/system/xbin/su",
        "/data/local/xbin/su",
        "/data/local/bin/su",
        "/system/sd/xbin/su",
        "/system/bin/failsafe/su",
        "/data/local/su",
        "/su/bin/su",
        "/system/xbin/busybox"
    };

    private static final String[] EMULATOR_FINGERPRINT_PREFIXES = {
        "generic", "unknown", "google_sdk", "sdk", "sdk_gphone"
    };

    /**
     * Security spec §3 (mobile hardening) — best-effort root/emulator
     * heuristics. Every check is independently defeatable (Magisk hides most
     * of these); the backend treats this as one signal among several, never
     * a sole gate.
     */
    @com.getcapacitor.PluginMethod
    public void getIntegrity(PluginCall call) {
        List<String> signals = new ArrayList<>();

        for (String path : ROOT_INDICATOR_PATHS) {
            if (new File(path).exists()) {
                signals.add("root_path");
                break;
            }
        }

        String tags = Build.TAGS;
        if (tags != null && tags.contains("test-keys")) {
            signals.add("test_keys_build");
        }

        String fingerprint = Build.FINGERPRINT == null ? "" : Build.FINGERPRINT.toLowerCase(Locale.ROOT);
        String hardware = Build.HARDWARE == null ? "" : Build.HARDWARE.toLowerCase(Locale.ROOT);
        String model = Build.MODEL == null ? "" : Build.MODEL.toLowerCase(Locale.ROOT);
        String product = Build.PRODUCT == null ? "" : Build.PRODUCT.toLowerCase(Locale.ROOT);
        String manufacturer = Build.MANUFACTURER == null ? "" : Build.MANUFACTURER.toLowerCase(Locale.ROOT);

        boolean looksLikeEmulator = false;
        for (String prefix : EMULATOR_FINGERPRINT_PREFIXES) {
            if (fingerprint.startsWith(prefix) || product.startsWith(prefix)) {
                looksLikeEmulator = true;
                break;
            }
        }
        looksLikeEmulator = looksLikeEmulator
            || hardware.contains("goldfish")
            || hardware.contains("ranchu")
            || model.contains("google_sdk")
            || model.contains("emulator")
            || model.contains("android sdk built for x86")
            || manufacturer.contains("genymotion");
        if (looksLikeEmulator) {
            signals.add("emulator_fingerprint");
        }

        if (Debug.isDebuggerConnected()) {
            signals.add("debugger_connected");
        }

        JSArray signalsArray = new JSArray();
        for (String signal : signals) {
            signalsArray.put(signal);
        }

        JSObject result = new JSObject();
        result.put("signals", signalsArray);
        result.put("isSuspicious", !signals.isEmpty());
        call.resolve(result);
    }

    /**
     * Reads a required challenge param; rejects the call and returns null when the
     * param is missing, empty, or oversized (mirrors the desktop shell's validation).
     */
    private String requireChallengeParam(PluginCall call, String name) {
        String value = call.getString(name);
        if (value == null || value.trim().isEmpty()) {
            call.reject("A non-empty '" + name + "' parameter is required.");
            return null;
        }
        if (value.length() >= MAX_INPUT_LENGTH) {
            call.reject("'" + name + "' exceeds the maximum length.");
            return null;
        }
        return value;
    }

    private static String hmacSha256LowercaseHex(String secret, String message) throws GeneralSecurityException {
        Mac mac = Mac.getInstance("HmacSHA256");
        mac.init(new SecretKeySpec(secret.getBytes(StandardCharsets.UTF_8), "HmacSHA256"));
        byte[] bytes = mac.doFinal(message.getBytes(StandardCharsets.UTF_8));
        StringBuilder hex = new StringBuilder(bytes.length * 2);
        for (byte b : bytes) {
            hex.append(String.format("%02x", b));
        }
        return hex.toString();
    }
}
