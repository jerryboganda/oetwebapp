import { getRuntimeConfig } from './runtime-config';

const PROXY_PATH = '/api/backend';
const DEFAULT_PROXY_TARGET = 'http://127.0.0.1:5198';

function trimEnvValue(value: string | undefined): string | null {
  if (!value) {
    return null;
  }

  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

export function resolveApiBaseUrl(envSource: NodeJS.ProcessEnv = process.env): string {
  // Browser requests can safely use the same-origin proxy route.
  if (typeof window !== 'undefined') {
    return PROXY_PATH;
  }

  const explicitClientBaseUrl = trimEnvValue(envSource.NEXT_PUBLIC_API_BASE_URL);
  if (explicitClientBaseUrl) {
    return explicitClientBaseUrl.replace(/\/$/, '');
  }

  // Server-side fetches still need an absolute URL during SSR, tests, and route handlers.
  return (
    trimEnvValue(envSource.PUBLIC_API_BASE_URL) ??
    trimEnvValue(envSource.API_PROXY_TARGET_URL) ??
    DEFAULT_PROXY_TARGET
  ).replace(/\/$/, '');
}

export const env = {
  apiBaseUrl: resolveApiBaseUrl(),
  // Build-time NEXT_PUBLIC_* fallback. Wave 5: the browser now prefers the
  // DB-driven public VAPID key from the runtime-config endpoint (see
  // lib/runtime-config.ts + resolveWebPushPublicKey()); this stays as the
  // first-paint / offline fallback so boot never breaks.
  webPushPublicKey: process.env.NEXT_PUBLIC_WEB_PUSH_PUBLIC_KEY?.trim() || '',
} as const;

/**
 * Wave 5 — resolve the public web-push VAPID key preferring the runtime config
 * (admin/DB-driven, secret-free) and falling back to the build-time
 * NEXT_PUBLIC_WEB_PUSH_PUBLIC_KEY. Lazily imports the runtime-config singleton
 * to avoid a hard module cycle and to keep this usable outside React.
 */
export function resolveWebPushPublicKey(): string {
  if (typeof window !== 'undefined') {
    // Synchronous getter — returns the last-fetched config or NEXT_PUBLIC fallback.
    const runtimeKey = getRuntimeConfig().webPush.vapidPublicKey;
    if (runtimeKey) return runtimeKey;
  }
  return env.webPushPublicKey;
}
