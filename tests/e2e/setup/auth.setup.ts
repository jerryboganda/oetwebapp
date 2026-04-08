import { readFile } from 'node:fs/promises';
import { expect, test as setup } from '@playwright/test';
import { authStateTargets } from '../fixtures/auth';
import { bootstrapSessionForRole, persistSessionToStorageState } from '../fixtures/auth-bootstrap';

setup.describe.configure({ mode: 'serial' });

for (const target of authStateTargets) {
  setup(`bootstrap ${target.projectName} auth state`, async ({ request }) => {
    const session = await bootstrapSessionForRole(request, target.role, undefined, {
      useDiskCache: false,
      isolateSession: true,
    });
    await persistSessionToStorageState(session, target.path);

    const rawState = JSON.parse(await readFile(target.path, 'utf8')) as {
      cookies: Array<unknown>;
      origins: Array<{ origin: string; localStorage: Array<{ name: string; value: string }> }>;
    };
    const originState = rawState.origins.find((origin) => origin.origin.startsWith('http'));
    expect(originState, `Expected persisted origin storage for ${target.projectName}`).toBeTruthy();
    expect(originState?.localStorage.some((entry) => entry.name === 'oet.auth.session.local')).toBeTruthy();
    expect(
      rawState.origins.length,
      `Expected non-empty persisted auth state for ${target.projectName}`,
    ).toBeGreaterThan(0);
  });
}
