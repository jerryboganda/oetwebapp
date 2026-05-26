export function safeZoomUrl(value?: string | null): string | null {
  if (!value) return null;

  try {
    const url = new URL(value);
    if (url.protocol !== 'https:') return null;

    const host = url.hostname.toLowerCase();
    if (host === 'zoom.us' || host.endsWith('.zoom.us') || host === 'zoom.com' || host.endsWith('.zoom.com')) {
      return url.toString();
    }
  } catch {
    return null;
  }

  return null;
}