import { expect, test as setup } from '@playwright/test';
import { authStatePaths, type SeededRole } from '../fixtures/auth';
import { bootstrapSessionForRole, persistSessionToStorageState } from '../fixtures/auth-bootstrap';

const roles: SeededRole[] = ['learner', 'expert', 'admin'];

setup.describe.configure({ mode: 'serial' });

for (const role of roles) {
  setup(`bootstrap ${role} auth state`, async ({ page, request }) => {
    const session = await bootstrapSessionForRole(request, role, undefined, { useDiskCache: false });
    await persistSessionToStorageState(page, role, session);

    const storageState = await page.context().storageState();
    const originState = storageState.origins.find((origin: { origin: string }) => origin.origin.startsWith('http'));
    expect(originState, `Expected persisted origin storage for ${role}`).toBeTruthy();

    const rawState = await page.context().storageState({ path: authStatePaths[role] });
    expect(rawState.origins.length, `Expected non-empty persisted auth state for ${role}`).toBeGreaterThan(0);
  });
}
