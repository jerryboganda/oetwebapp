import { loadEnvConfig } from '@next/env';
import type { CapacitorConfig } from '@capacitor/cli';
import { KeyboardResize } from '@capacitor/keyboard';

import { resolveCapacitorAppUrl } from './lib/mobile/capacitor-config';

loadEnvConfig(process.cwd(), false, console);

const configuredAppUrl = resolveCapacitorAppUrl();
const productionAppUrl = 'https://app.oetwithdrhesham.co.uk';
const appUrl = configuredAppUrl || productionAppUrl;

if (!configuredAppUrl) {
  console.warn(`[capacitor] APP_URL/CAPACITOR_APP_URL was not set; falling back to ${productionAppUrl}`);
}

const config: CapacitorConfig = {
  appId: 'com.oetprep.learner',
  appName: 'OET Prep Learner',
  webDir: 'capacitor-web',
  server: {
    url: appUrl,
    cleartext: appUrl.startsWith('http://'),
  },
  ios: {
    contentInset: 'automatic',
    scrollEnabled: true,
  },
  android: {
    allowMixedContent: true,
  },
  plugins: {
    Keyboard: {
      resize: KeyboardResize.Body,
      resizeOnFullScreen: true,
    },
    SplashScreen: {
      launchShowDuration: 0,
      showSpinner: false,
      backgroundColor: '#f7f5ef',
    },
    StatusBar: {
      style: 'DARK',
      backgroundColor: '#f7f5ef',
    },
  },
};

export default config;