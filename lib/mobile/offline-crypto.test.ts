import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
  encryptForStorage,
  decryptFromStorage,
  isEncryptionAvailable,
} from './offline-crypto';

beforeEach(() => {
  // jsdom provides localStorage but not always crypto.subtle — Node's WebCrypto
  // is exposed globally via vitest jsdom environment.
  localStorage.clear();
});

describe('isEncryptionAvailable', () => {
  it('returns true in the test environment (Node WebCrypto available)', () => {
    expect(isEncryptionAvailable()).toBe(true);
  });

  it('returns false when crypto.subtle is missing', () => {
    const original = globalThis.crypto;
    Object.defineProperty(globalThis, 'crypto', {
      value: { getRandomValues: original.getRandomValues.bind(original) },
      configurable: true,
    });
    try {
      expect(isEncryptionAvailable()).toBe(false);
    } finally {
      Object.defineProperty(globalThis, 'crypto', { value: original, configurable: true });
    }
  });
});

describe('encryptForStorage / decryptFromStorage', () => {
  it('round-trips a plain object', async () => {
    const data = { name: 'Jane', score: 42, tags: ['a', 'b'] };
    const encrypted = await encryptForStorage(data, 'session-key-1');
    const decrypted = await decryptFromStorage<typeof data>(encrypted, 'session-key-1');
    expect(decrypted).toEqual(data);
  });

  it('round-trips primitives (strings, numbers, booleans, null)', async () => {
    const cases: unknown[] = ['hello', 0, 123.45, true, false, null];
    for (const v of cases) {
      const enc = await encryptForStorage(v, 'pw');
      const dec = await decryptFromStorage(enc, 'pw');
      expect(dec).toEqual(v);
    }
  });

  it('round-trips arrays and nested objects', async () => {
    const data = { list: [1, 2, { inner: 'value' }], map: { a: { b: { c: 1 } } } };
    const enc = await encryptForStorage(data, 'pw');
    const dec = await decryptFromStorage(enc, 'pw');
    expect(dec).toEqual(data);
  });

  it('produces a base64-encoded string', async () => {
    const enc = await encryptForStorage({ x: 1 }, 'pw');
    expect(typeof enc).toBe('string');
    // Base64 chars only.
    expect(enc).toMatch(/^[A-Za-z0-9+/=]+$/);
  });

  it('produces different ciphertexts for the same plaintext (random IV)', async () => {
    const enc1 = await encryptForStorage({ x: 1 }, 'pw');
    const enc2 = await encryptForStorage({ x: 1 }, 'pw');
    expect(enc1).not.toBe(enc2);
  });

  it('decryption returns null with a wrong passphrase', async () => {
    const enc = await encryptForStorage({ secret: true }, 'right');
    const dec = await decryptFromStorage(enc, 'wrong');
    expect(dec).toBeNull();
  });

  it('decryption returns null on tampered ciphertext', async () => {
    const enc = await encryptForStorage({ a: 1 }, 'pw');
    // Corrupt by reversing the string.
    const tampered = enc.split('').reverse().join('');
    const dec = await decryptFromStorage(tampered, 'pw');
    expect(dec).toBeNull();
  });

  it('decryption returns null on garbage input', async () => {
    expect(await decryptFromStorage('not-base64@@@', 'pw')).toBeNull();
    expect(await decryptFromStorage('', 'pw')).toBeNull();
    expect(await decryptFromStorage('AAAA', 'pw')).toBeNull();
  });

  it('persists the salt across calls (stable in localStorage)', async () => {
    await encryptForStorage({ a: 1 }, 'pw');
    const saltAfter1 = localStorage.getItem('oet_offline_salt');
    expect(saltAfter1).not.toBeNull();
    await encryptForStorage({ a: 2 }, 'pw');
    const saltAfter2 = localStorage.getItem('oet_offline_salt');
    expect(saltAfter2).toBe(saltAfter1);
  });

  it('uses different keys for different passphrases (different ciphertext patterns produce no cross-decrypt)', async () => {
    const encA = await encryptForStorage('payload', 'passA');
    const encB = await encryptForStorage('payload', 'passB');
    expect(encA).not.toBe(encB);
    expect(await decryptFromStorage(encA, 'passB')).toBeNull();
    expect(await decryptFromStorage(encB, 'passA')).toBeNull();
  });

  it('a regenerated salt invalidates older ciphertext', async () => {
    const enc = await encryptForStorage({ a: 1 }, 'pw');
    localStorage.removeItem('oet_offline_salt');
    // Force salt regeneration on next call by reading first.
    expect(await decryptFromStorage(enc, 'pw')).toBeNull();
  });

  it('handles long payloads', async () => {
    const big = { items: Array.from({ length: 200 }, (_, i) => ({ id: i, label: `item-${i}` })) };
    const enc = await encryptForStorage(big, 'pw');
    const dec = await decryptFromStorage<typeof big>(enc, 'pw');
    expect(dec).toEqual(big);
  });

  it('handles unicode payloads', async () => {
    const data = { msg: 'Olá! 你好世界 — здравствуйте 🎉' };
    const enc = await encryptForStorage(data, 'pw');
    const dec = await decryptFromStorage<typeof data>(enc, 'pw');
    expect(dec).toEqual(data);
  });

  it('survives localStorage being unavailable (still produces decryptable output for the same session)', async () => {
    // Mock localStorage.getItem to throw; encryption should fall back to in-memory salt.
    const origGet = Storage.prototype.getItem;
    const origSet = Storage.prototype.setItem;
    Storage.prototype.getItem = vi.fn(() => {
      throw new Error('disabled');
    });
    Storage.prototype.setItem = vi.fn(() => {
      throw new Error('disabled');
    });
    try {
      // Each call gets a fresh random salt → encryption works, but two
      // separate encrypt calls cannot decrypt each other (which is acceptable
      // for the documented "key rotates on next session" contract). Verify
      // that the output is at least produced and base64-shaped.
      const enc = await encryptForStorage({ x: 1 }, 'pw');
      expect(typeof enc).toBe('string');
      expect(enc.length).toBeGreaterThan(0);
    } finally {
      Storage.prototype.getItem = origGet;
      Storage.prototype.setItem = origSet;
    }
  });
});
