// Conformance gate: the Tauri-injected window.desktopBridge must have the exact
// shape the renderer expects (types/desktop.d.ts), so the frontend consumers
// work unchanged.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const BRIDGE_SOURCE = readFileSync(join(__dirname, '..', 'inject', 'desktop-bridge.js'), 'utf8');

// Canonical surface of the desktop bridge contract.
const EXPECTED_SHAPE: Record<string, string[] | null> = {
  platform: null,
  versions: ['chrome', 'node'],
  openExternal: null,
  runtime: ['info', 'onWindowStateChange'],
  secureSecrets: ['get', 'set', 'delete', 'status'],
  offlineCache: ['store', 'get', 'delete', 'list', 'clear'],
  notifications: ['show'],
  fileInfo: ['getDroppedFileInfo'],
  print: ['printPage'],
  attestation: ['signVideoChallenge'],
  speakingAudio: ['start', 'stop', 'getBlob', 'discard', 'getPlatform'],
};

type AnyWindow = Window & { desktopBridge?: any; __TAURI_INTERNALS__?: any; __OET_DESKTOP__?: any };

describe('tauri desktop-bridge conformance', () => {
  const win = window as unknown as AnyWindow;
  let invoked: Array<{ cmd: string; args: unknown }>;

  beforeEach(() => {
    invoked = [];
    delete win.desktopBridge;
    win.__OET_DESKTOP__ = { platform: 'win32', tauri: '2.x-test' };
    win.__TAURI_INTERNALS__ = {
      invoke: vi.fn((cmd: string, args: unknown) => {
        invoked.push({ cmd, args });
        return Promise.resolve({ ok: true });
      }),
    };
    // eslint-disable-next-line no-eval
    (0, eval)(BRIDGE_SOURCE);
  });

  afterEach(() => {
    delete win.desktopBridge;
    delete win.__TAURI_INTERNALS__;
    delete win.__OET_DESKTOP__;
  });

  it('exposes every namespace and method of the desktop bridge contract', () => {
    const bridge = win.desktopBridge;
    expect(bridge).toBeDefined();
    for (const [key, members] of Object.entries(EXPECTED_SHAPE)) {
      expect(bridge, `missing namespace ${key}`).toHaveProperty(key);
      if (members) {
        for (const member of members) {
          expect(bridge[key], `missing ${key}.${member}`).toHaveProperty(member);
        }
      }
    }
  });

  it('reports a NodeJS.Platform-compatible platform string', () => {
    expect(['win32', 'darwin', 'linux']).toContain(win.desktopBridge.platform);
  });

  it('routes invocations with the argument names the Rust commands expect', async () => {
    const bridge = win.desktopBridge;
    await bridge.openExternal('https://example.com');
    await bridge.secureSecrets.get('auth', 'token');
    await bridge.offlineCache.store('papers', { a: 1 });
    await bridge.notifications.show('t', 'b', '/dashboard');
    await bridge.fileInfo.getDroppedFileInfo('C:/file.pdf');
    await bridge.speakingAudio.start('sess-1', 'audio/webm');
    await bridge.attestation.signVideoChallenge('nonce-1', 'vid-1', 'user-1');

    expect(invoked).toEqual([
      { cmd: 'open_external', args: { url: 'https://example.com' } },
      { cmd: 'secret_get', args: { namespace: 'auth', key: 'token' } },
      { cmd: 'offline_cache_store', args: { key: 'papers', data: { a: 1 } } },
      { cmd: 'show_notification', args: { title: 't', body: 'b', route: '/dashboard' } },
      { cmd: 'get_dropped_file_info', args: { filePath: 'C:/file.pdf' } },
      { cmd: 'speaking_audio_start', args: { sessionId: 'sess-1', mimeType: 'audio/webm' } },
      // camelCase JS keys map onto the snake_case Rust params (video_id, user_id).
      { cmd: 'sign_video_challenge', args: { nonce: 'nonce-1', videoId: 'vid-1', userId: 'user-1' } },
    ]);
  });

  it('round-trips speaking audio chunks as base64 and rehydrates blobs', async () => {
    const bridge = win.desktopBridge;
    const bytes = new Uint8Array([1, 2, 3, 250]);
    await bridge.speakingAudio.stop('sess-1', [bytes.buffer]);
    const stopCall = invoked.find((c) => c.cmd === 'speaking_audio_stop') as any;
    expect(stopCall.args.chunksBase64).toEqual([btoa(String.fromCharCode(1, 2, 3, 250))]);

    win.__TAURI_INTERNALS__.invoke = vi.fn(() =>
      Promise.resolve({ ok: true, sessionId: 's', mimeType: 'audio/webm', sizeBytes: 4, dataBase64: btoa('abcd') }),
    );
    const blobRes = await bridge.speakingAudio.getBlob('s');
    expect(blobRes.ok).toBe(true);
    expect(blobRes.data).toBeInstanceOf(ArrayBuffer);
    expect(new Uint8Array(blobRes.data)).toEqual(new Uint8Array([97, 98, 99, 100]));
    expect(blobRes.dataBase64).toBeUndefined();
  });

  it('delivers window-state changes via CustomEvent and supports unsubscribe', () => {
    const seen: unknown[] = [];
    const unsubscribe = win.desktopBridge.runtime.onWindowStateChange((s: unknown) => seen.push(s));
    const detail = { isFocused: true, isVisible: true, isMinimized: false, isMaximized: false, isFullScreen: false };
    window.dispatchEvent(new CustomEvent('desktop:window-state-changed', { detail }));
    expect(seen).toEqual([detail]);
    unsubscribe();
    window.dispatchEvent(new CustomEvent('desktop:window-state-changed', { detail }));
    expect(seen).toHaveLength(1);
  });
});
