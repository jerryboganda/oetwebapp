import { readFile } from 'node:fs/promises';
import { expect, test as setup } from '@playwright/test';
import { authStateTargets, type SeededRole } from '../fixtures/auth';
import { bootstrapSessionForRole, persistSessionToStorageState } from '../fixtures/auth-bootstrap';

setup.describe.configure({ mode: 'serial' });

// Security spec §3.1 single-active-session (`security.singleActiveSessionEnabled`,
// default on): every sign-in revokes all other sessions of the account. Signing
// the same seeded account in once per Playwright project (chromium-, firefox-,
// webkit-, mobile-, sydney-…) left only the LAST project of each role with a
// live session, so every other project's storage state bounced to /sign-in.
// Sign each role in exactly once and give every project of that role the same
// live session instead.
const roles = [...new Set(authStateTargets.map((target) => target.role))] as SeededRole[];

for (const role of roles) {
  const targets = authStateTargets.filter((target) => target.role === role);

  setup(`bootstrap ${role} auth state (${targets.map((target) => target.projectName).join(', ')})`, async ({ request }) => {
    const session = await bootstrapSessionForRole(request, role, undefined, {
      useDiskCache: false,
      isolateSession: true,
    });
    await persistSessionToStorageState(session, targets.map((target) => target.path), request, role);

    for (const target of targets) {
      const rawState = JSON.parse(await readFile(target.path, 'utf8')) as {
        cookies: Array<unknown>;
        origins: Array<{ origin: string; localStorage: Array<{ name: string; value: string }> }>;
      };
      const originState = rawState.origins.find((origin) => origin.origin.startsWith('http'));
      expect(originState, `Expected persisted origin storage for ${target.projectName}`).toBeTruthy();
      expect(
        rawState.cookies.some((cookie) => {
          const maybeCookie = cookie as { name?: string };
          return maybeCookie.name === 'oet_auth';
        }),
        `Expected persisted auth indicator cookie for ${target.projectName}`,
      ).toBeTruthy();
      expect(originState?.localStorage.some((entry) => entry.name === 'oet.auth.session.local')).toBeTruthy();
      expect(
        rawState.origins.length,
        `Expected non-empty persisted auth state for ${target.projectName}`,
      ).toBeGreaterThan(0);
    }
  });
}
