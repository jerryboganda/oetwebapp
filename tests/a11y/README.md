# Speaking Module — Accessibility Tests

axe-core via Playwright. Fails on **serious + critical** violations only.

## Run

```bash
pnpm exec playwright test tests/a11y
```

Set `PLAYWRIGHT_BASE_URL` to target a non-default origin.

## Helper

`helpers/axe-runner.ts` exposes `runAxe(page, options?)`. Defaults cover WCAG 2.1 AA + 2.2 AA.

## Adding a spec

Drop a file at `tests/a11y/<surface>.a11y.spec.ts` matching the existing pattern:

```ts
import { test } from '@playwright/test';
import { runAxe } from './helpers/axe-runner';

test('speaking home is accessible', async ({ page }) => {
  await page.goto('/speaking');
  await page.waitForLoadState('networkidle');
  await runAxe(page);
});
```

## CI

Nightly via `.github/workflows/speaking-a11y.yml`. HTML report uploaded as `speaking-a11y-report`.

## Triage

- **Critical / serious** → P1 fix or accessibility regression block.
- **Moderate** → P2 in next sprint.
- **Minor** → backlog.
