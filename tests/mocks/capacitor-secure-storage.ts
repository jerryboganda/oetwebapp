// Test stub for the native-only @aparajita/capacitor-secure-storage plugin.
//
// The real module is dynamically imported ONLY on native platforms
// (lib/mobile/secure-storage.ts gates every call behind
// Capacitor.isNativePlatform()), so in jsdom this stub is never actually
// invoked. It exists purely so Vite can statically resolve the import during
// test collection while the native package migration is pending.
export const SecureStorage = {
  getItem: async (_key: string): Promise<string | null> => null,
  setItem: async (_key: string, _value: string): Promise<void> => {},
  removeItem: async (_key: string): Promise<void> => {},
  clear: async (): Promise<void> => {},
};

export default { SecureStorage };
