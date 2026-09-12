#!/usr/bin/env node
// Fails when CSS emitted for the Apple apps uses features newer than the
// engines we claim to support.
//
// Why this exists: on macOS and iOS the WebView engine is supplied by the OS,
// not by the app bundle. The remote content is a Next.js 16 + Tailwind v4 build
// whose upstream floor is Safari 16.4, while the shells and the declared OS
// ranges are tracked separately in apple-compatibility.json. Nothing in the
// toolchain ties those numbers together, so a routine dependency bump or a new
// utility class could silently raise the real floor above the declared one
// again. This guard is that tie.
//
// Two distinct floors are enforced:
//   contentSafariMin — the remote app. Checked against built CSS + iOS shell pages.
//   shellSafariMin   — the Tauri macOS splash, which must render its "update
//                      Safari" guidance on the oldest engine we claim to
//                      support, so it can never use newer features itself.
//
// Usage:
//   node scripts/apple/assert-webview-floor.mjs
//   node scripts/apple/assert-webview-floor.mjs --css path/to/styles.css
//   node scripts/apple/assert-webview-floor.mjs --self-test

import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative, resolve } from 'node:path';

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');

// Scanned recursively rather than pinned to .next/static/css, because the
// chunk layout differs between the webpack and Turbopack build pipelines.
const BUILT_CSS_ROOT = '.next/static';

const CONTENT_FILES = [
  'capacitor-web/index.html',
  'capacitor-web/error.html',
];

const SHELL_FILES = [
  'src-tauri/splash/index.html',
  'src-tauri/splash/splash.js',
];

// Feature -> first Safari version that supports it. Every entry must be a
// feature we would genuinely break on, not a theoretical concern. The table
// only lists features newer than the lowest floor we ever enforce (the shell
// floor, Safari 15.0), because anything older can never fail the check.
const FEATURES = [
  { label: 'oklch() colour values', pattern: /\boklch\(/gi, safari: '15.4' },
  { label: 'color-mix()', pattern: /\bcolor-mix\(/gi, safari: '16.2' },
  { label: '@layer at-rule', pattern: /@layer\b/gi, safari: '15.4' },
  { label: ':has() selector', pattern: /:has\(/gi, safari: '15.4' },
  { label: ':focus-visible selector', pattern: /:focus-visible\b/gi, safari: '15.4' },
  { label: 'dynamic viewport units (dvh/svh/lvh)', pattern: /\d(?:dvh|svh|lvh)\b/gi, safari: '15.4' },
  { label: '@property at-rule', pattern: /@property\b/gi, safari: '16.4' },
  { label: '@container queries', pattern: /@container\b/gi, safari: '16.0' },
  { label: 'grid subgrid', pattern: /\bsubgrid\b/gi, safari: '16.0' },
  { label: 'text-wrap: balance', pattern: /text-wrap\s*:\s*balance/gi, safari: '17.5' },
  { label: '@starting-style at-rule', pattern: /@starting-style\b/gi, safari: '17.5' },
  { label: 'field-sizing', pattern: /\bfield-sizing\s*:/gi, safari: '18.0' },
];

function compareVersions(left, right) {
  const a = String(left).split('.').map(Number);
  const b = String(right).split('.').map(Number);
  for (let index = 0; index < Math.max(a.length, b.length); index += 1) {
    const difference = (a[index] ?? 0) - (b[index] ?? 0);
    if (difference !== 0) return difference;
  }
  return 0;
}

// Blank out comments and string literals before scanning, so that a feature
// name mentioned in prose or used as probe data is never mistaken for live
// usage. The splash's capability gate has to *name* oklch()/color-mix()/
// @property in order to test for them, and those names must not fail the check
// that exists to keep the splash itself simple.
//
// lineComments is off for CSS: unquoted url(https://…) would otherwise be
// mistaken for a line comment and could hide a real feature further along.
function stripCommentsAndStrings(source, { lineComments }) {
  let output = '';
  let index = 0;

  while (index < source.length) {
    const char = source[index];
    const next = source[index + 1];

    const blockEnd = char === '/' && next === '*'
      ? '*/'
      : (char === '<' && source.startsWith('<!--', index) ? '-->' : null);

    if (blockEnd) {
      const end = source.indexOf(blockEnd, index + 2);
      index = end === -1 ? source.length : end + blockEnd.length;
      output += ' ';
      continue;
    }

    if (lineComments && char === '/' && next === '/') {
      const end = source.indexOf('\n', index + 2);
      index = end === -1 ? source.length : end;
      output += ' ';
      continue;
    }

    if (char === '"' || char === "'" || char === '`') {
      index += 1;
      while (index < source.length) {
        if (source[index] === '\\') {
          index += 2;
          continue;
        }
        if (source[index] === char) {
          index += 1;
          break;
        }
        index += 1;
      }
      output += ' ';
      continue;
    }

    output += char;
    index += 1;
  }

  return output;
}

function findOffendingFeatures(content, floor) {
  const offenders = [];
  for (const feature of FEATURES) {
    if (compareVersions(feature.safari, floor) <= 0) continue;
    const matches = content.match(feature.pattern);
    if (matches && matches.length > 0) {
      offenders.push({ ...feature, occurrences: matches.length });
    }
  }
  return offenders.sort((a, b) => compareVersions(a.safari, b.safari));
}

function collectCssFiles(root) {
  const found = [];
  if (!existsSync(root)) return found;

  for (const entry of readdirSync(root, { withFileTypes: true })) {
    const fullPath = join(root, entry.name);
    if (entry.isDirectory()) {
      found.push(...collectCssFiles(fullPath));
    } else if (entry.name.toLowerCase().endsWith('.css') && statSync(fullPath).isFile()) {
      found.push(fullPath);
    }
  }
  return found;
}

function scanFile(absolutePath, floor) {
  const extension = absolutePath.slice(absolutePath.lastIndexOf('.')).toLowerCase();
  const source = stripCommentsAndStrings(readFileSync(absolutePath, 'utf8'), {
    lineComments: extension !== '.css',
  });
  return findOffendingFeatures(source, floor).map((offender) => ({
    file: relative(REPO_ROOT, absolutePath).replaceAll('\\', '/'),
    ...offender,
  }));
}

function report(label, floor, findings) {
  if (findings.length === 0) {
    console.log(`OK   ${label} stays within Safari ${floor}.`);
    return 0;
  }

  console.error(`FAIL ${label} requires a newer engine than the declared Safari ${floor}:`);
  const byFile = new Map();
  for (const finding of findings) {
    if (!byFile.has(finding.file)) byFile.set(finding.file, []);
    byFile.get(finding.file).push(finding);
  }
  for (const [file, fileFindings] of byFile) {
    console.error(`  ${file}`);
    for (const finding of fileFindings) {
      console.error(`    - ${finding.label}: needs Safari ${finding.safari} (${finding.occurrences} occurrence(s))`);
    }
  }
  return findings.length;
}

function selfTest() {
  const failures = [];
  const expect = (condition, message) => {
    if (!condition) failures.push(message);
  };

  // Version comparison must be numeric, not lexical ("16.4" > "15.4").
  expect(compareVersions('16.4', '15.4') > 0, 'compareVersions failed on 16.4 vs 15.4');
  expect(compareVersions('15.0', '15.0') === 0, 'compareVersions failed on equal versions');
  expect(compareVersions('9.0', '10.0') < 0, 'compareVersions failed on 9.0 vs 10.0');
  expect(compareVersions('16', '16.0') === 0, 'compareVersions failed on 16 vs 16.0');

  // Each detection must fire when the floor is below the feature's
  // requirement, and must stay quiet once the floor allows that feature.
  const cases = [
    { css: '.a{color:oklch(0 0 0)}', label: 'oklch() colour values', required: '15.4', below: '15.0' },
    { css: '.a{color:color-mix(in oklab,red,blue)}', label: 'color-mix()', required: '16.2', below: '16.0' },
    { css: '@property --x{syntax:"*"}', label: '@property at-rule', required: '16.4', below: '16.2' },
    { css: '.a:has(> b){color:red}', label: ':has() selector', required: '15.4', below: '15.2' },
    { css: '.a{height:100dvh}', label: 'dynamic viewport units (dvh/svh/lvh)', required: '15.4', below: '15.2' },
    { css: '.a:focus-visible{outline:0}', label: ':focus-visible selector', required: '15.4', below: '15.2' },
  ];

  for (const testCase of cases) {
    const detected = findOffendingFeatures(testCase.css, testCase.below);
    expect(
      detected.some((entry) => entry.label === testCase.label),
      `failed to detect ${testCase.label} against floor ${testCase.below}`,
    );

    const allowed = findOffendingFeatures(testCase.css, testCase.required);
    expect(
      allowed.length === 0,
      `falsely flagged ${testCase.label} against floor ${testCase.required}`,
    );
  }

  // Comments and string data must be invisible to the scan, otherwise the
  // splash's own feature-detection probes would fail the floor they enforce.
  const stripped = [
    {
      name: 'a CSS block comment',
      source: '/* oklch(0 0 0) @property */ .a{color:red}',
      options: { lineComments: false },
    },
    {
      name: 'a JS string literal',
      source: "const probe = 'color-mix(in oklab, red, blue)';",
      options: { lineComments: true },
    },
    {
      name: 'a JS line comment',
      source: '// @property needs Safari 16.4\nconst a = 1;',
      options: { lineComments: true },
    },
    {
      name: 'an HTML comment',
      source: '<!-- :focus-visible needs 15.4 --><div></div>',
      options: { lineComments: true },
    },
  ];

  for (const testCase of stripped) {
    const result = findOffendingFeatures(
      stripCommentsAndStrings(testCase.source, testCase.options),
      '15.0',
    );
    expect(result.length === 0, `${testCase.name} was not stripped before scanning`);
  }

  // ...but live usage alongside a stripped region must still be caught.
  const liveUsage = stripCommentsAndStrings(
    "const probe = 'oklch(0 0 0)'; /* @property */ .a{color:oklch(0 0 0)}",
    { lineComments: true },
  );
  expect(
    findOffendingFeatures(liveUsage, '15.0').some((entry) => entry.label === 'oklch() colour values'),
    'live usage next to a stripped region was missed',
  );

  if (failures.length > 0) {
    console.error('assert-webview-floor self-test failed:');
    for (const failure of failures) console.error(`  - ${failure}`);
    process.exit(1);
  }

  console.log(`assert-webview-floor self-test passed (${cases.length * 2 + 4 + stripped.length + 1} checks).`);
}

function main() {
  if (process.argv.includes('--self-test')) {
    selfTest();
    return;
  }

  const source = JSON.parse(readFileSync(resolve(REPO_ROOT, 'apple-compatibility.json'), 'utf8'));
  const contentFloor = source.webview.contentSafariMin;
  const shellFloor = source.webview.shellSafariMin;

  const explicitCss = process.argv.reduce((accumulator, argument, index, all) => {
    if (argument === '--css' && all[index + 1]) accumulator.push(all[index + 1]);
    return accumulator;
  }, []);

  const builtCssFiles = collectCssFiles(resolve(REPO_ROOT, BUILT_CSS_ROOT));
  const cssFiles = explicitCss.length > 0
    ? explicitCss.map((entry) => resolve(REPO_ROOT, entry))
    : builtCssFiles;

  if (cssFiles.length === 0) {
    console.error(
      `No built CSS found at ${BUILT_CSS_ROOT}/. Run "pnpm build" first, or pass --css <path>.`,
    );
    process.exit(1);
  }

  const contentFindings = [];
  for (const file of cssFiles) {
    if (!existsSync(file)) {
      console.error(`--css target does not exist: ${file}`);
      process.exit(1);
    }
    contentFindings.push(...scanFile(file, contentFloor));
  }
  for (const relativePath of CONTENT_FILES) {
    const absolutePath = resolve(REPO_ROOT, relativePath);
    if (existsSync(absolutePath)) contentFindings.push(...scanFile(absolutePath, contentFloor));
  }

  const shellFindings = [];
  for (const relativePath of SHELL_FILES) {
    const absolutePath = resolve(REPO_ROOT, relativePath);
    if (!existsSync(absolutePath)) {
      console.error(`Expected shell file is missing: ${relativePath}`);
      process.exit(1);
    }
    shellFindings.push(...scanFile(absolutePath, shellFloor));
  }

  console.log(
    `Scanned ${cssFiles.length} built CSS file(s) + ${CONTENT_FILES.length} shell page(s) `
      + `at Safari ${contentFloor}, and ${SHELL_FILES.length} splash file(s) at Safari ${shellFloor}.`,
  );

  const violations = report('content', contentFloor, contentFindings)
    + report('shell', shellFloor, shellFindings);

  if (violations > 0) {
    console.error(
      '\nFix by either removing the newer feature, or raising the floor deliberately in '
        + 'apple-compatibility.json plus the matching deployment target.',
    );
    process.exit(1);
  }
}

main();
