import { createHmac, timingSafeEqual } from 'node:crypto';
import { config } from './config.js';

/**
 * Verify the GitHub webhook signature (HMAC-SHA256).
 * Returns true if the signature is valid.
 */
export function verifyWebhookSignature(payload, signature) {
  if (!config.WEBHOOK_SECRET) {
    // If no secret configured, skip verification (dev mode)
    console.warn('[auth] WEBHOOK_SECRET not set — skipping signature verification');
    return true;
  }

  if (!signature) return false;

  const sig = signature.startsWith('sha256=') ? signature.slice(7) : signature;
  const expected = createHmac('sha256', config.WEBHOOK_SECRET)
    .update(payload, 'utf8')
    .digest('hex');

  if (sig.length !== expected.length) return false;

  return timingSafeEqual(Buffer.from(sig, 'hex'), Buffer.from(expected, 'hex'));
}

/**
 * Extract user identity from the x-github-token header.
 * Calls the GitHub API to get the authenticated user.
 * Returns { login, id, name } or null if invalid.
 */
export async function extractUserFromToken(token) {
  if (!token) return null;

  try {
    const res = await fetch('https://api.github.com/user', {
      headers: {
        Authorization: `Bearer ${token}`,
        Accept: 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
      },
    });

    if (!res.ok) return null;

    const data = await res.json();
    return { login: data.login, id: data.id, name: data.name };
  } catch (err) {
    console.error('[auth] Failed to fetch user from token:', err.message);
    return null;
  }
}

/**
 * Middleware-style auth check. Returns { user } or writes 401 and returns null.
 */
export async function authenticateRequest(req, rawBody) {
  // Verify webhook signature
  const signature = req.headers['x-hub-signature-256'] || req.headers['x-github-signature'];
  if (!verifyWebhookSignature(rawBody, signature)) {
    return { error: 'Invalid webhook signature', status: 401 };
  }

  // Extract user from GitHub token
  const ghToken = req.headers['x-github-token'];
  const user = await extractUserFromToken(ghToken);
  if (!user) {
    return { error: 'Invalid or missing x-github-token', status: 401 };
  }

  return { user, ghToken };
}
