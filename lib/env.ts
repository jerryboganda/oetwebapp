/**
 * Runtime environment validation.
 * Import this at the app entry point to fail fast on missing config.
 */

function requireEnv(name: string, value: string | undefined): string {
  if (!value || value.trim() === '') {
    throw new Error(`Missing required environment variable: ${name}`);
  }
  return value;
}

function optionalEnv(name: string, fallback: string): string {
  return process.env[name] || fallback;
}

export const env = {
  // API
  apiBaseUrl:
    process.env.NODE_ENV === 'production'
      ? requireEnv('NEXT_PUBLIC_API_BASE_URL', process.env.NEXT_PUBLIC_API_BASE_URL).replace(/\/$/, '')
      : optionalEnv('NEXT_PUBLIC_API_BASE_URL', 'http://localhost:5198').replace(/\/$/, ''),
} as const;
