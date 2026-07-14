/**
 * HLS playback engine wrapper.
 *
 * Bunny Stream serves adaptive HLS (`playlist.m3u8` + segments). Engine
 * selection per WebView:
 *   - iOS WKWebView / macOS (Tauri on Mac): native HLS via `video.src`
 *   - Windows WebView2 / Android WebView (Chromium): MSE via hls.js
 *
 * hls.js is imported dynamically so it never lands in web bundles — the
 * web code path shows a lock screen and never reaches `createHlsEngine`.
 */

export interface HlsQualityLevel {
  index: number;
  height: number;
  bitrate: number;
  label: string;
}

export interface HlsEngineHandle {
  /** Available quality levels (empty on native-HLS where Apple auto-selects). */
  readonly levels: HlsQualityLevel[];
  /** -1 = auto. */
  setQuality(levelIndex: number): void;
  getCurrentQuality(): number;
  /** Swap to a re-signed URL (session renewal) preserving position + play state. */
  recoverWithUrl(url: string): Promise<void>;
  /** Called when the CDN rejects a request (expired token) — wire renewal here. */
  onFatalNetworkError(handler: () => void): void;
  onLevelsUpdated(handler: (levels: HlsQualityLevel[]) => void): void;
  destroy(): void;
  readonly mode: 'hls.js' | 'native';
}

function levelLabel(height: number): string {
  return height > 0 ? `${height}p` : 'Auto';
}

export function supportsNativeHls(video: HTMLVideoElement): boolean {
  return video.canPlayType('application/vnd.apple.mpegurl') !== '';
}

/** The signed query ("token=…&expires=…&token_path=…") from a Bunny playback URL, without the leading "?". */
function signedQuery(url: string): string {
  const q = url.indexOf('?');
  return q >= 0 ? url.slice(q + 1) : '';
}

export async function createHlsEngine(video: HTMLVideoElement, url: string): Promise<HlsEngineHandle> {
  // Engine selection — prefer hls.js (MSE) WHENEVER it is supported. This is the canonical
  // hls.js order and is deliberately NOT gated on canPlayType first.
  //
  // Why: a Chromium-family engine (Windows WebView2 — i.e. the Tauri desktop shell —, Electron,
  // desktop Chrome/Edge) reports canPlayType('application/vnd.apple.mpegurl') === 'maybe', yet it
  // CANNOT actually demux an HLS playlist natively. Trusting canPlayType first (the previous
  // behaviour) sent those clients down the native path where `video.src = playlist.m3u8` fails
  // silently — black screen, stuck at 0:00, and the native engine only surfaces MEDIA_ERR_NETWORK
  // (not SRC_NOT_SUPPORTED), so nothing even errored. hls.js/MSE plays the exact same stream fine.
  //
  // Native HLS is the RIGHT path only where hls.js/MSE is unavailable — Safari / iOS WKWebView —
  // and there native HLS genuinely works, so the fallback below still covers it.
  const { default: Hls } = await import('hls.js');
  if (!Hls.isSupported()) {
    return createNativeEngine(video, url);
  }

  let fatalHandler: (() => void) | null = null;
  let levelsHandler: ((levels: HlsQualityLevel[]) => void) | null = null;

  // Bunny directory token authentication signs a token_path covering /{videoId}/,
  // and the ?token&expires&token_path query MUST be present on EVERY request —
  // master playlist, media playlists, segments and keys. hls.js drops the parent
  // playlist's query string when resolving relative child URLs (verified: child
  // segment without the query → HTTP 403), so a custom loader re-appends it.
  // `currentQuery` is mutable so a renewed token propagates on recoverWithUrl().
  let currentQuery = signedQuery(url);
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const BaseLoader = Hls.DefaultConfig.loader as any;
  class TokenPropagatingLoader extends BaseLoader {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    load(context: any, config: unknown, callbacks: unknown) {
      if (currentQuery && context?.url && !/[?&]token=/.test(context.url)) {
        context.url += (context.url.includes('?') ? '&' : '?') + currentQuery;
      }
      super.load(context, config, callbacks);
    }
  }
  const makeConfig = () => ({
    // Keep buffers modest for long lecture videos inside WebViews.
    maxBufferLength: 60,
    backBufferLength: 30,
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    loader: TokenPropagatingLoader as any,
  });

  let hls = new Hls(makeConfig());

  const mapLevels = (): HlsQualityLevel[] =>
    hls.levels.map((level, index) => ({
      index,
      height: level.height ?? 0,
      bitrate: level.bitrate ?? 0,
      label: levelLabel(level.height ?? 0),
    }));

  const wire = () => {
    hls.on(Hls.Events.MANIFEST_PARSED, () => {
      levelsHandler?.(mapLevels());
    });
    hls.on(Hls.Events.ERROR, (_event, data) => {
      if (!data.fatal) return;
      if (data.type === Hls.ErrorTypes.NETWORK_ERROR) {
        fatalHandler?.();
      } else if (data.type === Hls.ErrorTypes.MEDIA_ERROR) {
        hls.recoverMediaError();
      } else {
        fatalHandler?.();
      }
    });
  };

  wire();
  hls.loadSource(url);
  hls.attachMedia(video);

  return {
    get levels() {
      return mapLevels();
    },
    setQuality(levelIndex: number) {
      hls.currentLevel = levelIndex;
    },
    getCurrentQuality() {
      return hls.currentLevel;
    },
    async recoverWithUrl(nextUrl: string) {
      currentQuery = signedQuery(nextUrl);
      const position = video.currentTime;
      const wasPlaying = !video.paused && !video.ended;
      hls.destroy();
      hls = new Hls(makeConfig());
      wire();
      hls.loadSource(nextUrl);
      hls.attachMedia(video);
      await new Promise<void>((resolve) => {
        const onParsed = () => {
          hls.off(Hls.Events.MANIFEST_PARSED, onParsed);
          resolve();
        };
        hls.on(Hls.Events.MANIFEST_PARSED, onParsed);
      });
      video.currentTime = position;
      if (wasPlaying) {
        void video.play().catch(() => undefined);
      }
    },
    onFatalNetworkError(handler) {
      fatalHandler = handler;
    },
    onLevelsUpdated(handler) {
      levelsHandler = handler;
    },
    destroy() {
      fatalHandler = null;
      levelsHandler = null;
      hls.destroy();
    },
    mode: 'hls.js',
  };
}

function createNativeEngine(video: HTMLVideoElement, url: string): HlsEngineHandle {
  let fatalHandler: (() => void) | null = null;

  const onError = () => {
    // MediaError code 2 = network — the signed token likely expired.
    if (video.error && video.error.code === MediaError.MEDIA_ERR_NETWORK) {
      fatalHandler?.();
    }
  };
  video.addEventListener('error', onError);
  video.src = url;

  return {
    levels: [],
    setQuality() {
      // Native HLS (Apple) auto-selects; no-op.
    },
    getCurrentQuality() {
      return -1;
    },
    async recoverWithUrl(nextUrl: string) {
      const position = video.currentTime;
      const wasPlaying = !video.paused && !video.ended;
      video.src = nextUrl;
      await new Promise<void>((resolve) => {
        const onMeta = () => {
          video.removeEventListener('loadedmetadata', onMeta);
          resolve();
        };
        video.addEventListener('loadedmetadata', onMeta);
        video.load();
      });
      video.currentTime = position;
      if (wasPlaying) {
        void video.play().catch(() => undefined);
      }
    },
    onFatalNetworkError(handler) {
      fatalHandler = handler;
    },
    onLevelsUpdated() {
      // Levels are not observable on native HLS.
    },
    destroy() {
      fatalHandler = null;
      video.removeEventListener('error', onError);
      video.removeAttribute('src');
      video.load();
    },
    mode: 'native',
  };
}
