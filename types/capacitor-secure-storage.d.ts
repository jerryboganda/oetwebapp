// Ambient declaration for the native-only secure-storage plugin the mobile
// layer is migrating to (see lib/mobile/secure-storage.ts). The package is not
// yet installed in package.json (native migration pending), so this lets tsc
// resolve the dynamic `import('@aparajita/capacitor-secure-storage')`.
//
// TODO(mobile): remove this shim once `@aparajita/capacitor-secure-storage` is
// added as a real dependency; the package ships its own types.
declare module '@aparajita/capacitor-secure-storage' {
  export const SecureStorage: {
    getItem(key: string): Promise<string | null>;
    setItem(key: string, value: string): Promise<void>;
    removeItem(key: string): Promise<void>;
    clear(): Promise<void>;
  };
}
