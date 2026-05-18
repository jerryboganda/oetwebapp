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
  appId: 'com.oetprep.learner',
  appName: 'OET Prep Learner',
  webDir: 'capacitor-web',
  server: {
    url: appUrl,
    cleartext: isCapacitorLocalHttpAllowed(appUrl),
    androidScheme: 'https',
    iosScheme: 'capacitor',
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
      overlaysWebView: false,
      style: 'DARK',
      backgroundColor: '#f7f5ef',
    },
  },
};

export default config;
