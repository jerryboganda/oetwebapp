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

export async function createHlsEngine(video: HTMLVideoElement, url: string): Promise<HlsEngineHandle> {
  if (supportsNativeHls(video)) {
    return createNativeEngine(video, url);
  }

  const { default: Hls } = await import('hls.js');
  if (!Hls.isSupported()) {
    // Last-resort fallback: let the element try (some WebViews expose MSE oddly).
    return createNativeEngine(video, url);
  }

  let fatalHandler: (() => void) | null = null;
  let levelsHandler: ((levels: HlsQualityLevel[]) => void) | null = null;

  let hls = new Hls({
    // Keep buffers modest for long lecture videos inside WebViews.
    maxBufferLength: 60,
    backBufferLength: 30,
  });

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
      const position = video.currentTime;
      const wasPlaying = !video.paused && !video.ended;
      hls.destroy();
      hls = new Hls({ maxBufferLength: 60, backBufferLength: 30 });
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
