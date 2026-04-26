import type { BrowserContext, Page } from '@playwright/test';

const PROD_URL = process.env.PROD_URL ?? 'https://app.oetwithdrhesham.co.uk';
const API_URL = process.env.PROD_API_URL ?? 'https://api.oetwithdrhesham.co.uk';

export interface SeedAuthOptions {
  email: string;
  password: string;
  /** Cooldown between retries on 429 (rate-limit). Default 35s. */
  retryCooldownMs?: number;
  /** Max retries on 429. Default 2. */
  maxRetries?: number;
}

/**
 * Sign in directly to the API host (matches prod browser, which uses
 * NEXT_PUBLIC_API_BASE_URL=https://api.host) and seed cookies + localStorage
 * into the browser context so the SPA boots authenticated.
 *
 * Retries with exponential backoff on 429 (AuthBruteforce 10/min) so multiple
 * specs can run back-to-back without manual cooldowns.
 */
export async function seedProdAuth(
  page: Page,
  context: BrowserContext,
  opts: SeedAuthOptions,
): Promise<void> {
  const retryCooldownMs = opts.retryCooldownMs ?? 35_000;
  const maxRetries = opts.maxRetries ?? 2;

  let attempt = 0;
  let lastErr = '';
  // eslint-disable-next-line no-constant-condition
  while (true) {
    const resp = await page.request.post(`${API_URL}/v1/auth/sign-in`, {
      data: { email: opts.email, password: opts.password, rememberMe: true },
      headers: { 'content-type': 'application/json' },
    });

    if (resp.ok()) {
      const session = await resp.json();
      const appHost = new URL(PROD_URL).host;
      await context.addCookies([
        {
          name: 'oet_auth',
          value: '1',
          domain: appHost,
          path: '/',
          httpOnly: false,
          secure: PROD_URL.startsWith('https'),
          sameSite: 'Lax',
        },
      ]);
      const snap = JSON.stringify({
        accessTokenExpiresAt: session.accessTokenExpiresAt,
        refreshTokenExpiresAt: session.refreshTokenExpiresAt,
        currentUser: session.currentUser,
      });
      await context.addInitScript((s: string) => {
        try {
          window.localStorage.setItem('oet.auth.session.local', s);
        } catch {
          /* ignore */
        }
      }, snap);
      return;
    }

    const status = resp.status();
    const body = await resp.text().catch(() => '<no body>');
    lastErr = `${status} ${body.slice(0, 200)}`;

    // Retry on rate-limit (429) or transient 5xx
    if ((status === 429 || status >= 500) && attempt < maxRetries) {
      attempt += 1;
      // eslint-disable-next-line no-console
      console.log(`[seedProdAuth] sign-in ${status}, retry ${attempt}/${maxRetries} after ${retryCooldownMs}ms`);
      await new Promise((r) => setTimeout(r, retryCooldownMs * attempt));
      continue;
    }

    throw new Error(`API sign-in failed: ${lastErr}`);
  }
}
