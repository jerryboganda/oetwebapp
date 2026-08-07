import {
  DEFAULT_PERFORMANCE_BUDGETS,
  evaluatePerformanceBudget,
  summarizeResourceTotals,
  type BrowserPerformanceReport,
} from './metrics';

function makeReport(overrides: Partial<BrowserPerformanceReport['webVitals']> = {}): BrowserPerformanceReport {
  return {
    route: '/sign-in',
    project: 'perf-unauth-chromium',
    capturedAt: '2026-08-07T00:00:00.000Z',
    navigation: {
      ttfbMs: 120,
      domContentLoadedMs: 500,
      loadEventMs: 700,
    },
    webVitals: {
      lcpMs: 2_000,
      fcpMs: 1_200,
      inpMs: null,
      cls: 0.02,
      longTaskMs: 0,
      ...overrides,
    },
    resources: {
      totals: {
        javascript: { encodedBytes: 0, decodedBytes: 0 },
        css: { encodedBytes: 0, decodedBytes: 0 },
      },
      count: 0,
    },
    errors: {
      pageErrors: 0,
      requestFailures: 0,
      messages: [],
    },
    layout: {
      horizontalOverflow: false,
      primaryContentVisible: true,
      clientWidth: 1_000,
      scrollWidth: 1_000,
    },
  };
}

describe('browser performance metrics', () => {
  it('reports only metrics that exceed the configured budgets', () => {
    const violations = evaluatePerformanceBudget(
      makeReport({ lcpMs: 2_501, fcpMs: 1_700, inpMs: 210, cls: 0.11 }),
      DEFAULT_PERFORMANCE_BUDGETS,
    );

    expect(violations).toEqual([
      'LCP 2501ms exceeds 2500ms',
      'INP 210ms exceeds 200ms',
      'CLS 0.11 exceeds 0.1',
    ]);
  });

  it('allows a missing INP sample but rejects missing paint and layout metrics', () => {
    expect(evaluatePerformanceBudget(makeReport({ lcpMs: null, fcpMs: null, cls: null }))).toEqual([
      'LCP unavailable',
      'FCP unavailable',
      'CLS unavailable',
    ]);
  });

  it('flags runtime errors, overflow, and invisible primary content', () => {
    expect(evaluatePerformanceBudget({
      ...makeReport(),
      errors: { pageErrors: 1, requestFailures: 2, messages: ['failed request'] },
      layout: { horizontalOverflow: true, primaryContentVisible: false, clientWidth: 390, scrollWidth: 412 },
    })).toEqual([
      'page errors 1 exceeds 0',
      'request failures 2 exceeds 0',
      'horizontal overflow detected',
      'primary content is not visible',
    ]);
  });

  it('keeps encoded and decoded JavaScript/CSS totals distinct', () => {
    expect(summarizeResourceTotals([
      { name: '/app.js', initiatorType: 'script', transferSize: 100, decodedBodySize: 300 },
      { name: '/app.css', initiatorType: 'link', transferSize: 50, decodedBodySize: 150 },
      { name: '/font.woff2', initiatorType: 'link', transferSize: 25, decodedBodySize: 80 },
    ])).toEqual({
      javascript: { encodedBytes: 100, decodedBytes: 300 },
      css: { encodedBytes: 50, decodedBytes: 150 },
    });
  });
});
