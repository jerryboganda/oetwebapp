import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { expect, test, type Page, type TestInfo } from '@playwright/test';
import {
  DEFAULT_PERFORMANCE_BUDGETS,
  evaluatePerformanceBudget,
  summarizeResourceTotals,
  type BrowserPerformanceReport,
  type ResourceTimingLike,
} from './metrics';

type RouteDefinition = {
  route: string;
  readiness: 'auth' | 'main';
};

type ObserverSnapshot = {
  lcpMs: number | null;
  fcpMs: number | null;
  cls: number;
  inpMs: number | null;
  longTaskMs: number;
};

type PerformanceWindow = Window & {
  __oetPerformance?: ObserverSnapshot;
};

const routeByProject: Record<string, RouteDefinition> = {
  'perf-unauth-chromium': { route: '/sign-in', readiness: 'auth' },
  'perf-learner-chromium': { route: '/', readiness: 'main' },
  'perf-learner-pixel': { route: '/', readiness: 'main' },
  'perf-learner-iphone': { route: '/', readiness: 'main' },
  'perf-admin-chromium': { route: '/admin', readiness: 'main' },
};

function sanitizeErrorMessage(message: string) {
  return message
    .replace(/https?:\/\/[^\s]+/giu, '<url>')
    .replace(/(?:bearer|token|password|secret|authorization)[^\s]*/giu, '<redacted>')
    .slice(0, 240);
}

function safeFileName(project: string, route: string) {
  return `${project}-${route.replace(/[^a-z0-9]+/giu, '-').replace(/^-|-$/gu, '') || 'root'}`;
}

async function installObservers(page: Page) {
  await page.addInitScript(() => {
    const snapshot: ObserverSnapshot = {
      lcpMs: null,
      fcpMs: null,
      cls: 0,
      inpMs: null,
      longTaskMs: 0,
    };
    (window as PerformanceWindow).__oetPerformance = snapshot;

    if (typeof PerformanceObserver === 'undefined') {
      return;
    }

    const observe = (type: string, callback: (entry: PerformanceEntry) => void) => {
      try {
        if (PerformanceObserver.supportedEntryTypes?.includes(type)) {
          new PerformanceObserver((list) => {
            for (const entry of list.getEntries()) {
              callback(entry);
            }
          }).observe({ type, buffered: true });
        }
      } catch {
        // Unsupported observer types are represented by nullable metrics.
      }
    };

    observe('largest-contentful-paint', (entry) => {
      snapshot.lcpMs = entry.startTime;
    });

    observe('paint', (entry) => {
      if (entry.name === 'first-contentful-paint') {
        snapshot.fcpMs = entry.startTime;
      }
    });

    observe('layout-shift', (entry) => {
      const layoutShift = entry as PerformanceEntry & { hadRecentInput?: boolean; value?: number };
      if (!layoutShift.hadRecentInput) {
        snapshot.cls += layoutShift.value ?? 0;
      }
    });

    observe('event', (entry) => {
      const eventTiming = entry as PerformanceEntry & { duration?: number; interactionId?: number };
      if ((eventTiming.interactionId ?? 0) > 0) {
        snapshot.inpMs = Math.max(snapshot.inpMs ?? 0, eventTiming.duration ?? 0);
      }
    });

    observe('longtask', (entry) => {
      snapshot.longTaskMs += entry.duration;
    });
  });
}

async function waitForRouteReadiness(page: Page, route: RouteDefinition) {
  await page.locator('main, [role="main"]').first().waitFor({ state: 'visible', timeout: 30_000 });

  if (route.readiness === 'auth') {
    await expect(page.getByRole('heading', { name: /login to your account|access your workspace/i })).toBeVisible();
  }

  await page.evaluate(async () => {
    if (document.fonts?.ready) {
      await document.fonts.ready;
    }

    await new Promise<void>((resolve) => {
      requestAnimationFrame(() => requestAnimationFrame(() => resolve()));
    });
  });
}

async function collectBrowserPerformance(
  page: Page,
  route: RouteDefinition,
  projectName: string,
  testInfo: TestInfo,
): Promise<BrowserPerformanceReport> {
  const messages: string[] = [];
  let pageErrors = 0;
  let requestFailures = 0;

  const isExpectedRequestCancellation = (errorText: string | undefined) => (
    Boolean(errorText && /ERR_ABORTED|NS_BINDING_ABORTED|CANCELLED|CANCELED/iu.test(errorText))
  );

  page.on('pageerror', (error) => {
    pageErrors += 1;
    messages.push(`pageerror: ${sanitizeErrorMessage(error.message)}`);
  });

  page.on('requestfailed', (request) => {
    let pathname = request.resourceType();
    const errorText = request.failure()?.errorText;
    try {
      pathname = new URL(request.url()).pathname;
    } catch {
      // Keep the resource type when the browser does not expose a valid URL.
    }

    const message = `${request.method()} ${pathname}${errorText ? ` (${sanitizeErrorMessage(errorText)})` : ''}`;
    if (isExpectedRequestCancellation(errorText)) {
      messages.push(`requestcancelled: ${message}`);
      return;
    }

    requestFailures += 1;
    messages.push(`requestfailed: ${message}`);
  });

  await installObservers(page);
  await page.goto(route.route, { waitUntil: 'domcontentloaded' });
  await page.waitForLoadState('load', { timeout: 10_000 }).catch(() => undefined);

  let primaryContentVisible = false;
  try {
    await waitForRouteReadiness(page, route);
    primaryContentVisible = true;
  } catch (error) {
    messages.push(`readiness: ${sanitizeErrorMessage(error instanceof Error ? error.message : String(error))}`);
  }

  const browserData = await page.evaluate(() => {
    const navigation = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined;
    const paintEntries = performance.getEntriesByType('paint') as PerformancePaintTiming[];
    const lcpEntries = performance.getEntriesByType('largest-contentful-paint');
    const snapshot = (window as PerformanceWindow).__oetPerformance ?? {
      lcpMs: null,
      fcpMs: null,
      cls: 0,
      inpMs: null,
      longTaskMs: 0,
    };
    const resources = performance.getEntriesByType('resource').map((entry) => {
      const resource = entry as PerformanceResourceTiming;
      return {
        name: resource.name,
        initiatorType: resource.initiatorType,
        transferSize: resource.transferSize,
        decodedBodySize: resource.decodedBodySize,
      } satisfies ResourceTimingLike;
    });

    return {
      navigation: {
        ttfbMs: navigation ? navigation.responseStart : null,
        domContentLoadedMs: navigation ? navigation.domContentLoadedEventEnd : null,
        loadEventMs: navigation ? navigation.loadEventEnd : null,
      },
      webVitals: {
        lcpMs: snapshot.lcpMs ?? lcpEntries.at(-1)?.startTime ?? null,
        fcpMs: snapshot.fcpMs
          ?? paintEntries.find((entry) => entry.name === 'first-contentful-paint')?.startTime
          ?? null,
        inpMs: snapshot.inpMs,
        cls: snapshot.cls,
        longTaskMs: snapshot.longTaskMs,
      },
      resources,
      layout: {
        horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        clientWidth: document.documentElement.clientWidth,
        scrollWidth: document.documentElement.scrollWidth,
      },
    };
  });

  const report: BrowserPerformanceReport = {
    route: route.route,
    project: projectName,
    capturedAt: new Date().toISOString(),
    navigation: browserData.navigation,
    webVitals: browserData.webVitals,
    resources: {
      totals: summarizeResourceTotals(browserData.resources),
      count: browserData.resources.length,
    },
    errors: {
      pageErrors,
      requestFailures,
      messages,
    },
    layout: {
      ...browserData.layout,
      primaryContentVisible,
    },
  };
  report.violations = evaluatePerformanceBudget(report, DEFAULT_PERFORMANCE_BUDGETS);

  const outputDirectory = path.join(process.cwd(), 'output', 'performance', 'browser');
  await mkdir(outputDirectory, { recursive: true });
  const reportBody = JSON.stringify(report, null, 2);
  const fileName = `${safeFileName(projectName, route.route)}.json`;
  await writeFile(path.join(outputDirectory, fileName), reportBody, 'utf8');
  await testInfo.attach(`performance-${fileName}`, {
    body: Buffer.from(reportBody, 'utf8'),
    contentType: 'application/json',
  });

  return report;
}

test.describe('browser performance budgets', () => {
  test('captures route timing, web vitals, resources, and layout health', async ({ page }, testInfo) => {
    const route = routeByProject[testInfo.project.name];
    if (!route) {
      throw new Error(`No performance route configured for ${testInfo.project.name}`);
    }

    const report = await collectBrowserPerformance(page, route, testInfo.project.name, testInfo);
    const enforcementEnabled = process.env.PERF_ENFORCE_BUDGETS === '1';

    if (enforcementEnabled) {
      expect(report.violations, report.violations?.join('; ')).toEqual([]);
    } else if (report.violations && report.violations.length > 0) {
      console.warn(`[performance report-only] ${testInfo.project.name} ${route.route}: ${report.violations.join('; ')}`);
    }
  });
});
