export type CapacitorEnvironment = Record<string, string | undefined>;

function normalizeAbsoluteHttpUrl(value: string | undefined): string | null {
  if (typeof value !== 'string') {
    return null;
  }

  const trimmedValue = value.trim();
  if (!trimmedValue) {
    return null;
  }

  try {
    const parsedUrl = new URL(trimmedValue);
    if (parsedUrl.protocol !== 'http:' && parsedUrl.protocol !== 'https:') {
      return null;
    }

    return parsedUrl.toString().replace(/\/$/, '');
  } catch {
    return null;
  }
}

export function resolveCapacitorAppUrl(environment: CapacitorEnvironment = process.env): string | null {
  return normalizeAbsoluteHttpUrl(environment.CAPACITOR_APP_URL) ?? normalizeAbsoluteHttpUrl(environment.APP_URL);
}

function isLoopbackHttpUrl(value: string): boolean {
  try {
    const parsedUrl = new URL(value);
    return (
      parsedUrl.protocol === 'http:'
      && ['localhost', '127.0.0.1', '::1'].includes(parsedUrl.hostname.toLowerCase())
    );
  } catch {
    return false;
  }
}

export function isCapacitorLocalHttpAllowed(
  appUrl: string,
  environment: CapacitorEnvironment = process.env,
): boolean {
  return isLoopbackHttpUrl(appUrl) && environment.CAPACITOR_ALLOW_LOCAL_HTTP === 'true';
}

export function requireCapacitorAppUrl(environment: CapacitorEnvironment = process.env): string {
  const appUrl = resolveCapacitorAppUrl(environment);

  if (!appUrl) {
    throw new Error(
      'Capacitor packaging requires APP_URL or CAPACITOR_APP_URL to point at the deployed Next.js origin.',
    );
  }

  if (appUrl.startsWith('http://') && !isCapacitorLocalHttpAllowed(appUrl, environment)) {
    throw new Error(
      'Capacitor release packaging requires an HTTPS app URL. Set CAPACITOR_ALLOW_LOCAL_HTTP=true only for local loopback development builds.',
    );
  }

  return appUrl;
}
