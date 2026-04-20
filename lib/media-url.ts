/**
 * Resolve a backend media URL into a full origin-prefixed URL when the client
 * is running against a different origin than the API (e.g. dev with NEXT_PUBLIC_API_BASE_URL).
 * For same-origin or relative URLs, returns the value unchanged.
 */
export function resolveApiMediaUrl(pathOrUrl: string | null | undefined): string | null {
  if (!pathOrUrl) return null;
  if (/^https?:\/\//i.test(pathOrUrl)) return pathOrUrl;
  const base = process.env.NEXT_PUBLIC_API_BASE_URL?.trim();
  if (!base) return pathOrUrl;
  return `${base.replace(/\/$/, '')}${pathOrUrl.startsWith('/') ? '' : '/'}${pathOrUrl}`;
}
