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

export function requireCapacitorAppUrl(environment: CapacitorEnvironment = process.env): string {
  const appUrl = resolveCapacitorAppUrl(environment);

  if (!appUrl) {
    throw new Error(
      'Capacitor packaging requires APP_URL or CAPACITOR_APP_URL to point at the deployed Next.js origin.',
    );
  }

  return appUrl;
}