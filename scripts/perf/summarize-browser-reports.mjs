import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

function formatMetric(value, suffix = '') {
  return value === null || value === undefined ? 'n/a' : `${value}${suffix}`;
}

function formatBytes(value) {
  if (!Number.isFinite(value)) return 'n/a';
  return `${Math.round(value).toLocaleString('en-US')} B`;
}

function sanitizeCell(value) {
  return String(value ?? 'n/a').replaceAll('|', '\\|').replaceAll('\n', ' ');
}

function sanitizeRoute(value) {
  const rawRoute = String(value ?? 'n/a');
  try {
    return new URL(rawRoute, 'https://performance.invalid').pathname;
  } catch {
    return rawRoute.split(/[?#]/u, 1)[0] || '/';
  }
}

export function buildBrowserPerformanceSummary(reports) {
  const sortedReports = [...reports].sort((left, right) => (
    `${left.project}:${left.route}`.localeCompare(`${right.project}:${right.route}`)
  ));
  const lines = [
    '# Browser performance summary',
    '',
    '| Project | Route | LCP | FCP | INP | CLS | JS encoded | JS decoded | CSS encoded | CSS decoded | Errors | Violations |',
    '| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |',
  ];

  for (const report of sortedReports) {
    const js = report.resources?.totals?.javascript ?? {};
    const css = report.resources?.totals?.css ?? {};
    const errorCount = (report.errors?.pageErrors ?? 0) + (report.errors?.requestFailures ?? 0);
    const violations = Array.isArray(report.violations) && report.violations.length > 0
      ? report.violations.join('; ')
      : 'none';

    lines.push(`| ${sanitizeCell(report.project)} | ${sanitizeCell(sanitizeRoute(report.route))} | ${formatMetric(report.webVitals?.lcpMs, 'ms')} | ${formatMetric(report.webVitals?.fcpMs, 'ms')} | ${formatMetric(report.webVitals?.inpMs, 'ms')} | ${formatMetric(report.webVitals?.cls)} | ${formatBytes(js.encodedBytes)} | ${formatBytes(js.decodedBytes)} | ${formatBytes(css.encodedBytes)} | ${formatBytes(css.decodedBytes)} | ${errorCount} | ${sanitizeCell(violations)} |`);
  }

  return `${lines.join('\n')}\n`;
}

export async function readBrowserPerformanceReports(directory) {
  const names = (await readdir(directory)).filter((name) => name.endsWith('.json')).sort();
  return Promise.all(names.map(async (name) => JSON.parse(await readFile(path.join(directory, name), 'utf8'))));
}

export async function summarizeBrowserPerformanceReports(directory) {
  const reports = await readBrowserPerformanceReports(directory);
  if (reports.length === 0) {
    throw new Error(`No browser performance reports found in ${directory}`);
  }

  const markdown = buildBrowserPerformanceSummary(reports);
  const hasViolations = reports.some((report) => Array.isArray(report.violations) && report.violations.length > 0);
  return { reports, markdown, hasViolations };
}

const isMain = process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1]);
if (isMain) {
  const directory = process.argv[2] ?? path.join(process.cwd(), 'output', 'performance', 'browser');
  try {
    const result = await summarizeBrowserPerformanceReports(directory);
    process.stdout.write(result.markdown);
    if (process.env.PERF_ENFORCE_BUDGETS === '1' && result.hasViolations) {
      process.exitCode = 1;
    }
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
