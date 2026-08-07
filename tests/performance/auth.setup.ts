import { readFile, writeFile } from 'node:fs/promises';
import { expect, test as setup } from '@playwright/test';
import {
  bootstrapSessionForRole,
  persistSessionToStorageState,
} from '../e2e/fixtures/auth-bootstrap';
import type { SeededRole } from '../e2e/fixtures/auth';

type PerformanceAuthTarget = {
  projectName: 'perf-learner-chromium' | 'perf-admin-chromium';
  role: Extract<SeededRole, 'learner' | 'admin'>;
  path: string;
};

const authTargets: readonly PerformanceAuthTarget[] = [
  {
    projectName: 'perf-learner-chromium',
    role: 'learner',
    path: 'playwright/.auth/perf-learner.json',
  },
  {
    projectName: 'perf-admin-chromium',
    role: 'admin',
    path: 'playwright/.auth/perf-admin.json',
  },
];

setup.describe.configure({ mode: 'serial' });

for (const target of authTargets) {
  setup(`bootstrap ${target.projectName} auth state`, async ({ request }) => {
    const session = await bootstrapSessionForRole(request, target.role, undefined, {
      useDiskCache: false,
      isolateSession: true,
    });
    await persistSessionToStorageState(session, target.path, request, target.role);

    const rawState = JSON.parse(await readFile(target.path, 'utf8')) as {
      cookies: Array<{ name?: string }>;
      origins: Array<{ origin: string; localStorage: Array<{ name: string; value: string }> }>;
    };
    const deviceId = process.env.PERF_DEVICE_ID ?? 'perf-playwright-local';
    const originState = rawState.origins.find((origin) => origin.origin.startsWith('http'));
    if (originState && !originState.localStorage.some((entry) => entry.name === 'oet_device_id')) {
      originState.localStorage.push({ name: 'oet_device_id', value: deviceId });
      await writeFile(target.path, JSON.stringify(rawState, null, 2), 'utf8');
    }

    expect(originState, `Expected persisted origin storage for ${target.projectName}`).toBeTruthy();
    expect(
      rawState.cookies.some((cookie) => cookie.name === 'oet_auth'),
      `Expected auth indicator cookie for ${target.projectName}`,
    ).toBeTruthy();
    expect(
      originState?.localStorage.some((entry) => entry.name === 'oet.auth.session.local'),
      `Expected persisted session for ${target.projectName}`,
    ).toBeTruthy();
  });
}
