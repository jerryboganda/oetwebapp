import { loadEnvConfig } from '@next/env';
import type { CapacitorConfig } from '@capacitor/cli';
import { KeyboardResize } from '@capacitor/keyboard';

import { isCapacitorLocalHttpAllowed, requireCapacitorAppUrl, resolveCapacitorAppUrl } from './lib/mobile/capacitor-config';

loadEnvConfig(process.cwd(), false, console);

const configuredAppUrl = resolveCapacitorAppUrl();
const productionAppUrl = 'https://app.oetwithdrhesham.co.uk';
const appUrl = configuredAppUrl ? requireCapacitorAppUrl() : productionAppUrl;

if (!configuredAppUrl) {
  console.warn(`[capacitor] APP_URL/CAPACITOR_APP_URL was not set; falling back to ${productionAppUrl}`);
}

const config: CapacitorConfig = {
  appId: 'com.oetwithdrhesham.app',
  appName: 'OET with Dr Ahmed Hesham',
  webDir: 'capacitor-web',
  server: {
    url: appUrl,
    cleartext: isCapacitorLocalHttpAllowed(appUrl),
    androidScheme: 'https',
    iosScheme: 'capacitor',
    // With server.url set, the WebView loads the remote app directly and the
    // bundled webDir is bypassed — so without this a failed first load (offline
    // launch) surfaced as a blank/WebKit error page with no way forward.
    // error.html is a self-contained, network-free recovery screen.
    errorPath: 'error.html',
  },
  ios: {
    contentInset: 'automatic',
    scrollEnabled: true,
  },
  android: {
    allowMixedContent: false,
    appendUserAgent: 'OETPrep-Capacitor',
    overrideUserAgent: undefined,
  },
  plugins: {
    Keyboard: {
      resize: KeyboardResize.Body,
      resizeOnFullScreen: true,
    },
    SplashScreen: {
      launchShowDuration: 300,
      launchAutoHide: true,
      showSpinner: false,
      backgroundColor: '#f7f5ef',
    },
    StatusBar: {
      // Android 15+ (targetSdk 36, our compile target) force-enables edge-to-edge
      // regardless of this flag, so `false` was already a fiction on Android —
      // the WebView draws under the status bar either way. `true` makes the
      // config match reality on both platforms; MainActivity installs a
      // WindowInsetsCompat bridge (see MainActivity#installEdgeToEdgeInsetsBridge)
      // that feeds the live inset values into CSS so content still clears the
      // status bar/cutout correctly instead of drawing underneath it.
      overlaysWebView: true,
      style: 'DARK',
      backgroundColor: '#f7f5ef',
    },
  },
};

export default config;
