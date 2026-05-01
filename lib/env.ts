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
  webPushPublicKey: process.env.NEXT_PUBLIC_WEB_PUSH_PUBLIC_KEY?.trim() || '',
} as const;
