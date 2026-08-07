export type ResourceInitiatorType = 'script' | 'link' | 'other';

export type ResourceTimingLike = {
  name: string;
  initiatorType: ResourceInitiatorType | string;
  transferSize: number;
  decodedBodySize: number;
};

export type ByteTotals = {
  encodedBytes: number;
  decodedBytes: number;
};

export type ResourceTotals = {
  javascript: ByteTotals;
  css: ByteTotals;
};

export type BrowserPerformanceReport = {
  route: string;
  project: string;
  capturedAt: string;
  navigation: {
    ttfbMs: number | null;
    domContentLoadedMs: number | null;
    loadEventMs: number | null;
  };
  webVitals: {
    lcpMs: number | null;
    fcpMs: number | null;
    inpMs: number | null;
    cls: number | null;
    longTaskMs: number | null;
  };
  resources: {
    totals: ResourceTotals;
    count: number;
  };
  errors: {
    pageErrors: number;
    requestFailures: number;
    messages: string[];
  };
  layout: {
    horizontalOverflow: boolean;
    primaryContentVisible: boolean;
    clientWidth: number;
    scrollWidth: number;
  };
  violations?: string[];
};

export type PerformanceBudgets = {
  lcpMs: number;
  fcpMs: number;
  inpMs: number;
  cls: number;
  maxPageErrors: number;
  maxRequestFailures: number;
  allowMissingInp: boolean;
};

export const DEFAULT_PERFORMANCE_BUDGETS: PerformanceBudgets = {
  lcpMs: 2_500,
  fcpMs: 1_800,
  inpMs: 200,
  cls: 0.1,
  maxPageErrors: 0,
  maxRequestFailures: 0,
  allowMissingInp: true,
};

function formatMetric(value: number) {
  return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/0+$/u, '').replace(/\.$/u, '');
}

function addResourceTotal(target: ByteTotals, resource: ResourceTimingLike) {
  target.encodedBytes += Math.max(0, resource.transferSize || 0);
  target.decodedBytes += Math.max(0, resource.decodedBodySize || 0);
}

function isJavaScriptResource(resource: ResourceTimingLike) {
  return resource.initiatorType === 'script' || /\.(?:m?js)(?:[?#]|$)/iu.test(resource.name);
}

function isCssResource(resource: ResourceTimingLike) {
  return resource.initiatorType === 'link' && /\.css(?:[?#]|$)/iu.test(resource.name);
}

export function summarizeResourceTotals(resources: readonly ResourceTimingLike[]): ResourceTotals {
  const totals: ResourceTotals = {
    javascript: { encodedBytes: 0, decodedBytes: 0 },
    css: { encodedBytes: 0, decodedBytes: 0 },
  };

  for (const resource of resources) {
    if (isJavaScriptResource(resource)) {
      addResourceTotal(totals.javascript, resource);
    } else if (isCssResource(resource)) {
      addResourceTotal(totals.css, resource);
    }
  }

  return totals;
}

export function evaluatePerformanceBudget(
  report: BrowserPerformanceReport,
  budgets: PerformanceBudgets = DEFAULT_PERFORMANCE_BUDGETS,
): string[] {
  const violations: string[] = [];
  const { lcpMs, fcpMs, inpMs, cls } = report.webVitals;

  if (lcpMs === null) {
    violations.push('LCP unavailable');
  } else if (lcpMs > budgets.lcpMs) {
    violations.push(`LCP ${formatMetric(lcpMs)}ms exceeds ${formatMetric(budgets.lcpMs)}ms`);
  }

  if (fcpMs === null) {
    violations.push('FCP unavailable');
  } else if (fcpMs > budgets.fcpMs) {
    violations.push(`FCP ${formatMetric(fcpMs)}ms exceeds ${formatMetric(budgets.fcpMs)}ms`);
  }

  if (inpMs === null) {
    if (!budgets.allowMissingInp) {
      violations.push('INP unavailable');
    }
  } else if (inpMs > budgets.inpMs) {
    violations.push(`INP ${formatMetric(inpMs)}ms exceeds ${formatMetric(budgets.inpMs)}ms`);
  }

  if (cls === null) {
    violations.push('CLS unavailable');
  } else if (cls > budgets.cls) {
    violations.push(`CLS ${formatMetric(cls)} exceeds ${formatMetric(budgets.cls)}`);
  }

  if (report.errors.pageErrors > budgets.maxPageErrors) {
    violations.push(`page errors ${report.errors.pageErrors} exceeds ${budgets.maxPageErrors}`);
  }

  if (report.errors.requestFailures > budgets.maxRequestFailures) {
    violations.push(`request failures ${report.errors.requestFailures} exceeds ${budgets.maxRequestFailures}`);
  }

  if (report.layout.horizontalOverflow) {
    violations.push('horizontal overflow detected');
  }

  if (!report.layout.primaryContentVisible) {
    violations.push('primary content is not visible');
  }

  return violations;
}
