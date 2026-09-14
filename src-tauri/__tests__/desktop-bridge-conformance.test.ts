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
  updater: ['check', 'install', 'relaunch', 'onProgress'],
  reload: ['hard'],
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

// 0.7.7 shipped a hardcoded `{ platform: 'win32' }` fallback: had the shell's
// __OET_DESKTOP__ injection ever regressed, the macOS binary would have
// reported itself as win32. These tests pin the fallback to runtime UA
// detection and to injected-value precedence.
describe('tauri desktop-bridge platform fallback (injection-regression guard)', () => {
  const win = window as unknown as AnyWindow;
  let originalUa: string;

  const setUa = (ua: string) => {
    Object.defineProperty(win.navigator, 'userAgent', { value: ua, configurable: true });
  };

  const evalBridgeWithoutInjection = () => {
    delete win.desktopBridge;
    delete win.__OET_DESKTOP__;
    win.__TAURI_INTERNALS__ = { invoke: vi.fn(() => Promise.resolve({ ok: true })) };
    // eslint-disable-next-line no-eval
    (0, eval)(BRIDGE_SOURCE);
    return win.desktopBridge;
  };

  const uas: Array<[string, string, string]> = [
    ['macOS WKWebView', 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15', 'darwin'],
    ['Windows WebView2', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0', 'win32'],
    ['Android WebView', 'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36', 'android'],
    ['iOS WKWebView', 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1', 'ios'],
    ['Linux WebKitGTK', 'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Safari/605.1.15', 'linux'],
  ];

  beforeEach(() => {
    originalUa = win.navigator.userAgent;
  });

  afterEach(() => {
    Object.defineProperty(win.navigator, 'userAgent', { value: originalUa, configurable: true });
    delete win.desktopBridge;
    delete win.__OET_DESKTOP__;
    delete win.__TAURI_INTERNALS__;
  });

  it.each(uas)('derives the fallback platform from the UA on %s', (_label, ua, expected) => {
    setUa(ua);
    expect(evalBridgeWithoutInjection().platform).toBe(expected);
  });

  it('never falls back to win32 on a macOS WebView', () => {
    setUa(uas[0][1]);
    expect(evalBridgeWithoutInjection().platform).not.toBe('win32');
  });

  it('prefers the injected __OET_DESKTOP__ over UA detection', () => {
    setUa(uas[0][1]); // macOS UA — the injected value must win even against it
    delete win.desktopBridge;
    win.__OET_DESKTOP__ = { platform: 'darwin', tauri: '2.9.5' };
    win.__TAURI_INTERNALS__ = { invoke: vi.fn(() => Promise.resolve({ ok: true })) };
    // eslint-disable-next-line no-eval
    (0, eval)(BRIDGE_SOURCE);
    expect(win.desktopBridge.platform).toBe('darwin');
    expect(win.desktopBridge.versions.tauri).toBe('2.9.5');
  });

  it('ships no hardcoded platform literal in the bridge source', () => {
    expect(BRIDGE_SOURCE).not.toMatch(/platform:\s*'(win32|darwin|linux|android|ios)'/);
  });
});
