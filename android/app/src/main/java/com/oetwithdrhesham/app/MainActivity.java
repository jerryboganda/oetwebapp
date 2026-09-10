package com.oetwithdrhesham.app;

import android.os.Bundle;
import android.webkit.WebView;

import androidx.core.graphics.Insets;
import androidx.core.view.ViewCompat;
import androidx.core.view.WindowCompat;
import androidx.core.view.WindowInsetsCompat;

import com.getcapacitor.BridgeActivity;
import com.getcapacitor.WebViewListener;
import com.oetwithdrhesham.app.plugins.InstallerSourcePlugin;
import com.oetwithdrhesham.app.plugins.PlaybackAttestationPlugin;
import com.oetwithdrhesham.app.plugins.SpeakingRecorderPlugin;

public class MainActivity extends BridgeActivity {
	/**
	 * Last insets the platform reported. The listener installed below only fires
	 * when the system bars change — but this app loads its page from a remote URL
	 * (capacitor.config.ts server.url), so the document that received the first
	 * push is discarded when the real page navigates in, taking the inline
	 * --safe-area-inset-* values with it. Caching the values lets us re-apply them
	 * on every page load instead of waiting for the next inset change, which is
	 * why the layout used to stay broken until the app was backgrounded.
	 */
	private Insets lastSafeAreaInsets;

	@Override
	public void onCreate(Bundle savedInstanceState) {
		// BridgeActivity.onCreate() builds the Bridge (registers plugins into
		// PluginHeaders and starts loading the WebView) as part of super.onCreate()
		// itself. registerPlugin() only appends to the Bridge.Builder's plugin
		// list, so calling it AFTER super.onCreate() is a no-op on the already-built
		// Bridge — these plugins silently never reach the JS side. Register before
		// super.onCreate(), matching iOS's OETBridgeViewController.capacitorDidLoad().
		registerPlugin(InstallerSourcePlugin.class);
		registerPlugin(SpeakingRecorderPlugin.class);
		registerPlugin(PlaybackAttestationPlugin.class);
		super.onCreate(savedInstanceState);

		// Android 15+ (API 35+; API 36 is our current compile/target SDK) force-enables edge-to-edge —
		// the WebView draws under the status bar, display cutout, and gesture/nav
		// bar. We used to opt back out of that via
		// android:windowOptOutEdgeToEdgeEnforcement, which (a) fights a platform
		// default that Google is removing in a future release and (b) was reported
		// to still let the header collide with the system status bar on some
		// large-screen/tall devices — the opt-out attribute is honored
		// inconsistently across OEM WebView/skin combinations. Instead we embrace
		// edge-to-edge everywhere and bridge the *real* live inset values into the
		// WebView ourselves, so app/globals.css's safe-area padding is always
		// correct regardless of device, status-bar height, or cutout shape.
		WindowCompat.setDecorFitsSystemWindows(getWindow(), false);
		installEdgeToEdgeInsetsBridge();
	}

	@Override
	public void onWindowFocusChanged(boolean hasFocus) {
		super.onWindowFocusChanged(hasFocus);
		if (hasFocus) {
			reapplySafeAreaInsets();
		}
	}

	/**
	 * Reads the live system-bar + display-cutout insets and pushes them into the
	 * WebView as CSS custom properties (--safe-area-inset-top/right/bottom/left),
	 * which app/globals.css consumes with an env(safe-area-inset-*) fallback. This
	 * re-runs automatically on every inset change the platform reports (rotation,
	 * multi-window resize, cutout mode change) — there is no device-specific
	 * value anywhere in this method.
	 */
	private void installEdgeToEdgeInsetsBridge() {
		WebView webView = getBridge() != null ? getBridge().getWebView() : null;
		if (webView == null) {
			return;
		}

		ViewCompat.setOnApplyWindowInsetsListener(webView, (view, windowInsets) -> {
			Insets bars = windowInsets.getInsets(
				WindowInsetsCompat.Type.statusBars()
					| WindowInsetsCompat.Type.navigationBars()
					| WindowInsetsCompat.Type.displayCutout()
			);
			lastSafeAreaInsets = bars;
			// The listener callback's `view` parameter is typed as the generic
			// android.view.View (OnApplyWindowInsetsListener's signature), not
			// WebView — reuse the already-typed `webView` this listener was
			// attached to instead of casting.
			applySafeAreaInsets(webView, bars);
			// Let the WebView's own content continue to receive the insets too
			// (e.g. for any native scroll-adjustment behavior) rather than
			// consuming them here.
			return windowInsets;
		});

		// Re-push the cached insets once each document has loaded, so a cold launch
		// (where the first push landed on the pre-navigation document) is correct
		// on the first render. This uses Capacitor's own page-loaded callback
		// rather than replacing the WebViewClient.
		getBridge().addWebViewListener(new WebViewListener() {
			@Override
			public void onPageLoaded(WebView loadedWebView) {
				reapplySafeAreaInsets();
			}
		});

		ViewCompat.requestApplyInsets(webView);
	}

	private void reapplySafeAreaInsets() {
		if (lastSafeAreaInsets == null) {
			return;
		}
		WebView webView = getBridge() != null ? getBridge().getWebView() : null;
		if (webView == null) {
			return;
		}
		applySafeAreaInsets(webView, lastSafeAreaInsets);
	}

	private void applySafeAreaInsets(WebView webView, Insets bars) {
		float density = getResources().getDisplayMetrics().density;
		if (density <= 0f) {
			density = 1f;
		}

		// Android insets are raw pixels; CSS px in the WebView (viewport
		// width=device-width, initial-scale=1) map 1:1 to Android dp, so divide
		// by density to convert.
		double top = bars.top / density;
		double right = bars.right / density;
		double bottom = bars.bottom / density;
		double left = bars.left / density;

		// The CSS custom properties drive app/globals.css. window.__oetSafeAreaInsets
		// mirrors them so lib/mobile/runtime.ts can re-apply the same values after
		// the React app hydrates (the inline styles above are written before the
		// remote document finishes loading on a cold launch).
		String script = String.format(
			java.util.Locale.US,
			"var d=document.documentElement;"
				+ "d.style.setProperty('--safe-area-inset-top','%.2fpx');"
				+ "d.style.setProperty('--safe-area-inset-right','%.2fpx');"
				+ "d.style.setProperty('--safe-area-inset-bottom','%.2fpx');"
				+ "d.style.setProperty('--safe-area-inset-left','%.2fpx');"
				+ "window.__oetSafeAreaInsets={top:%.2f,right:%.2f,bottom:%.2f,left:%.2f};",
			top,
			right,
			bottom,
			left,
			top,
			right,
			bottom,
			left
		);

		webView.evaluateJavascript(script, null);
	}
}
