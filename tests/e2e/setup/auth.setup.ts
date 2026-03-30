import { test as setup } from '@playwright/test';
import { authStatePaths, signInThroughUi, type SeededRole } from '../fixtures/auth';

const roles: SeededRole[] = ['learner', 'expert', 'admin'];

for (const role of roles) {
  setup(`bootstrap ${role} auth state`, async ({ page }) => {
    await signInThroughUi(page, role);
    await page.context().storageState({ path: authStatePaths[role] });
  });
}
