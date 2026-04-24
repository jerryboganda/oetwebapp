/**
 * Offline Cache Encryption — Web Crypto API wrapper.
 *
 * Encrypts IndexedDB data at rest using AES-GCM with a key derived from
 * the user's session ID and a per-device salt stored in the meta store.
 * This applies to both Electron offline cache (JSON) and Capacitor
 * IndexedDB storage.
 *
 * Threat model: protects against an attacker who obtains the raw IndexedDB
 * files from disk (side-loaded backup, stolen device filesystem). Does NOT
 * protect against an attacker who can execute JS in the renderer process
 * (they could read the key from memory).
 */

const ALGORITHM = 'AES-GCM' as const;
const IV_LENGTH = 12; // 96-bit IV for AES-GCM
const SALT_LENGTH = 16;

/** Derive an AES-GCM key from a passphrase (user session id) + device salt. */
async function deriveKey(passphrase: string, salt: Uint8Array): Promise<CryptoKey> {
  const enc = new TextEncoder();
  const baseKey = await crypto.subtle.importKey(
    'raw',
    enc.encode(passphrase),
    { name: 'PBKDF2' },
    false,
    ['deriveKey'],
  );

  return crypto.subtle.deriveKey(
    {
      name: 'PBKDF2',
      salt: salt as unknown as BufferSource,
      iterations: 100_000,
      hash: 'SHA-256',
    } as Pbkdf2Params,
    baseKey,
    { name: 'AES-GCM', length: 256 } as AesKeyGenParams,
    false,
    ['encrypt', 'decrypt'],
  );
}

/**
 * Get or create a device salt persisted in localStorage.
 * The salt is NOT secret — it adds uniqueness to the key derivation.
 */
function getDeviceSalt(): Uint8Array {
  const KEY = 'oet_offline_salt';
  try {
    const stored = localStorage.getItem(KEY);
    if (stored) {
      return Uint8Array.from(atob(stored), (c) => c.charCodeAt(0));
    }
  } catch {
    // localStorage may be unavailable
  }

  const salt = crypto.getRandomValues(new Uint8Array(SALT_LENGTH));
  try {
    localStorage.setItem(KEY, btoa(String.fromCharCode(...salt)));
  } catch {
    // Proceed without persistence — key will rotate on next session
  }
  return salt;
}

/**
 * Encrypt a serializable value for offline storage.
 * Returns a base64-encoded string containing IV + ciphertext.
 */
export async function encryptForStorage(data: unknown, passphrase: string): Promise<string> {
  const salt = getDeviceSalt();
  const key = await deriveKey(passphrase, salt);
  const iv = crypto.getRandomValues(new Uint8Array(IV_LENGTH));
  const encoded = new TextEncoder().encode(JSON.stringify(data));

  const ciphertext = await crypto.subtle.encrypt(
    { name: ALGORITHM, iv },
    key,
    encoded,
  );

  // Pack IV (12 bytes) + ciphertext into a single buffer
  const packed = new Uint8Array(IV_LENGTH + ciphertext.byteLength);
  packed.set(iv, 0);
  packed.set(new Uint8Array(ciphertext), IV_LENGTH);

  return btoa(String.fromCharCode(...packed));
}

/**
 * Decrypt a value previously encrypted with `encryptForStorage`.
 * Returns the original deserialized data, or null if decryption fails.
 */
export async function decryptFromStorage<T = unknown>(
  encrypted: string,
  passphrase: string,
): Promise<T | null> {
  try {
    const salt = getDeviceSalt();
    const key = await deriveKey(passphrase, salt);
    const packed = Uint8Array.from(atob(encrypted), (c) => c.charCodeAt(0));

    const iv = packed.slice(0, IV_LENGTH);
    const ciphertext = packed.slice(IV_LENGTH);

    const decrypted = await crypto.subtle.decrypt(
      { name: ALGORITHM, iv },
      key,
      ciphertext,
    );

    const text = new TextDecoder().decode(decrypted);
    return JSON.parse(text) as T;
  } catch {
    // Decryption failure — key mismatch, corrupt data, or tampered ciphertext
    return null;
  }
}

/**
 * Check if the Web Crypto API is available (required for encryption).
 */
export function isEncryptionAvailable(): boolean {
  return (
    typeof crypto !== 'undefined' &&
    typeof crypto.subtle !== 'undefined' &&
    typeof crypto.subtle.encrypt === 'function'
  );
}
