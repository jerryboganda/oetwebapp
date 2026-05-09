import { test } from '@playwright/test';

test.describe('debug @debug', () => {
  test('capture admin warnings', async ({ page }, testInfo) => {
    if (testInfo.project.name !== 'chromium-admin') test.skip();

    await page.addInitScript(() => {
      const origError = console.error;
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (console as any).error = (...args: any[]) => {
        try {
          const stack = new Error('captured').stack;
          // eslint-disable-next-line no-underscore-dangle
          (window as unknown as { __captures: unknown[] }).__captures = (window as unknown as { __captures?: unknown[] }).__captures ?? [];
          (window as unknown as { __captures: unknown[] }).__captures.push({ args: args.map((a) => (typeof a === 'string' ? a : String(a))), stack });
        } catch { /* noop */ }
        origError.apply(console, args);
      };
      (window as unknown as { __captures: unknown[] }).__captures = [];
    });

    await page.goto('/expert');
    await page.waitForURL(/\/admin$/, { timeout: 30000 }).catch(() => undefined);
    await page.waitForTimeout(4000);

    const captures = await page.evaluate(() => (window as unknown as { __captures: unknown[] }).__captures ?? []);
    console.log('CAPTURED ERRORS:');
    for (const c of captures) {
      console.log('---');
      console.log(JSON.stringify(c, null, 2));
    }
  });
});
