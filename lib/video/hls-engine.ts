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
  /** TEMP diagnostic: last hls.js error + MSE codec support, for the on-screen HUD. */
  getDiag?: () => string;
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
  // TEMP diagnostic (surfaced to the on-screen HUD): the last hls.js error and whether
  // this engine's MediaSource actually supports the stream's codecs. On WebView2 the
  // stream fetches but buffers nothing — this tells us if MSE rejects the codec
  // (bufferAddCodecError / mse=N) or the transmux/append fails (bufferAppendError etc).
  let diagError = '-';
  let diagMse = '?';
  let diagErrCount = 0;
  // On the first fatal manifest error, fetch the SAME url the app uses and report the
  // raw outcome: fetch:200 = reachable (hls.js-specific bug), fetch:403 = token/auth,
  // fetchERR:TypeError = CSP/CORS/network block (paired with any securitypolicyviolation).
  let diagFetch = '-';
  let fetchProbed = false;
  // The manifest host + path (WITHOUT the token query) — reveals the CDN host and the
  // Bunny video id, so a manifestLoadError can be traced to a wrong host (CSP block),
  // a missing video (404), or a token issue (403).
  const manifestHost = (() => {
    try {
      const u = new URL(url);
      return u.host + u.pathname.replace(/\/playlist\.m3u8$/, '');
    } catch {
      return 'BADURL:' + String(url).slice(0, 28);
    }
  })();
  const computeMseSupport = () => {
    try {
      const MS = (typeof window !== 'undefined' && (window as unknown as { MediaSource?: typeof MediaSource }).MediaSource) || undefined;
      if (!MS || typeof MS.isTypeSupported !== 'function') return 'noMSE';
      return hls.levels
        .map((l) => {
          const codecs = [l.videoCodec, l.audioCodec].filter(Boolean).join(',');
          const mime = `video/mp4; codecs="${codecs}"`;
          return `${l.height ?? '?'}p:${MS.isTypeSupported(mime) ? 'Y' : 'N'}`;
        })
        .join(' ');
    } catch (e) {
      return `mseErr:${(e as Error).message?.slice(0, 20)}`;
    }
  };

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
      diagMse = computeMseSupport();
      levelsHandler?.(mapLevels());
    });
    hls.on(Hls.Events.ERROR, (_event, data) => {
      // Record EVERY error (incl. non-fatal) for the diagnostic HUD — buffer/codec
      // errors that stall WebView2 are often non-fatal but recurring.
      diagErrCount += 1;
      const d = data as {
        response?: { code?: number; text?: string };
        networkDetails?: { status?: number };
      };
      const code = d.response?.code ?? d.networkDetails?.status ?? '-';
      const txt = d.response?.text ? String(d.response.text).replace(/\s+/g, ' ').slice(0, 24) : '';
      diagError = `${data.details}${data.fatal ? '!' : ''} code=${code}${txt ? ` "${txt}"` : ''} x${diagErrCount}`;
      if (data.fatal && !fetchProbed) {
        fetchProbed = true;
        diagFetch = 'probing';
        fetch(url, { method: 'GET', cache: 'no-store' })
          .then((r) => {
            diagFetch = `fetch:${r.status}`;
          })
          .catch((e: unknown) => {
            const err = e as Error;
            diagFetch = `fetchERR:${err?.name ?? 'e'}:${String(err?.message ?? '').slice(0, 34)}`;
          });
      }
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
    getDiag: () => `${diagError} | ${diagFetch} | ${manifestHost} | mse=[${diagMse}]`,
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
