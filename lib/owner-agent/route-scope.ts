/**
 * Route scoping for the Owner Agent Console.
 *
 * Runtime-neutral (no browser, Node or Next imports): used by proxy.ts (strict
 * CSP), the Sentry configs (drop console events/replays) and the console's
 * document boundary.
 */

export const OWNER_AGENT_CONSOLE_PATH = '/admin/agent-console';
const OWNER_AGENT_API_PATH_PATTERN = /\/v1\/owner-agent(?:\/|$|\?)/i;

/** True for `/admin/agent-console` and everything below it. */
export function isOwnerAgentConsolePath(pathname: string | null | undefined): boolean {
  if (!pathname) return false;
  const path = pathname.split('?')[0]?.split('#')[0] ?? '';
  return path === OWNER_AGENT_CONSOLE_PATH || path.startsWith(`${OWNER_AGENT_CONSOLE_PATH}/`);
}

/** True for any URL (absolute or relative) pointing at the console pages or the owner-agent API. */
export function isOwnerAgentUrl(url: string | null | undefined): boolean {
  if (!url || typeof url !== 'string') return false;
  let pathname = url;
  try {
    pathname = new URL(url, 'http://placeholder.invalid').pathname;
  } catch {
    // keep the raw value
  }
  return isOwnerAgentConsolePath(pathname) || OWNER_AGENT_API_PATH_PATTERN.test(pathname);
}

interface SentryLikeEvent {
  request?: { url?: unknown } | undefined;
  transaction?: unknown;
  tags?: Record<string, unknown> | undefined;
}

/**
 * Sentry must never receive anything from the console: transcripts, commands,
 * approval payloads and diffs can contain production data.
 */
export function isOwnerAgentSentryEvent(event: SentryLikeEvent | null | undefined, currentPathname?: string | null): boolean {
  if (!event) return false;
  if (currentPathname && isOwnerAgentConsolePath(currentPathname)) return true;
  const requestUrl = event.request && typeof event.request.url === 'string' ? event.request.url : null;
  if (requestUrl && isOwnerAgentUrl(requestUrl)) return true;
  if (typeof event.transaction === 'string' && isOwnerAgentUrl(event.transaction)) return true;
  const route = event.tags?.['url'] ?? event.tags?.['route'];
  return typeof route === 'string' && isOwnerAgentUrl(route);
}

/** Current browser pathname, or null outside the browser. */
export function currentBrowserPathname(): string | null {
  if (typeof window === 'undefined' || !window.location) return null;
  return window.location.pathname;
}
