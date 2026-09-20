import { readFile } from 'node:fs/promises';
import { expect, test as setup } from '@playwright/test';
import { authStateTargets, type SeededRole } from '../fixtures/auth';
import { bootstrapBrowserSessionForRole, persistSessionToStorageState } from '../fixtures/auth-bootstrap';

setup.describe.configure({ mode: 'serial' });

for (const role of ['learner', 'expert', 'admin'] as const satisfies readonly SeededRole[]) {
  setup(`bootstrap ${role} auth state`, async ({ request }) => {
    const { session, cookies } = await bootstrapBrowserSessionForRole(request, role);
    const targets = authStateTargets.filter((target) => target.role === role);

    for (const target of targets) {
      await persistSessionToStorageState(session, target.path, cookies);

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
      expect(
        rawState.cookies.some((cookie) => {
          const maybeCookie = cookie as { name?: string };
          return maybeCookie.name === 'oet_rt';
        }),
        `Expected persisted refresh cookie for ${target.projectName}`,
      ).toBeTruthy();
      expect(originState?.localStorage.some((entry) => entry.name === 'oet.auth.session.local')).toBeTruthy();
      expect(
        rawState.origins.length,
        `Expected non-empty persisted auth state for ${target.projectName}`,
      ).toBeGreaterThan(0);
    }
  });
}
