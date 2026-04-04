import { loadEnvConfig } from '@next/env';
import type { CapacitorConfig } from '@capacitor/cli';

import { resolveCapacitorAppUrl } from './lib/mobile/capacitor-config';

loadEnvConfig(process.cwd(), false, console);

const configuredAppUrl = resolveCapacitorAppUrl();
const appUrl = configuredAppUrl || 'https://app.example.com';

if (!configuredAppUrl) {
  console.warn('[capacitor] APP_URL/CAPACITOR_APP_URL was not set; falling back to https://app.example.com');
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
    SplashScreen: {
      launchShowDuration: 0,
      showSpinner: false,
      backgroundColor: '#f8fafc',
    },
    StatusBar: {
      style: 'DARK',
      backgroundColor: '#f8fafc',
    },
  },
};

export default config;