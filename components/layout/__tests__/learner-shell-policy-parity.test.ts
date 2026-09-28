import { readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import ts from 'typescript';

import {
  IMMERSIVE_LEARNER_ROUTE_PATTERNS,
  LEARNER_FOCUS_ROUTES,
  LEARNER_PUBLIC_ROUTES,
  LEARNER_SELF_CHROMED_ROUTES,
  resolveLearnerChrome,
} from '../learner-dashboard-route-policy';

/**
 * resolveLearnerChrome() picks each learner page's chrome in
 * app/(learner)/layout.tsx. This test once checked the policy against the
 * <LearnerDashboardShell> props every page passed; those wrappers are gone
 * (app/(learner)/learner-shell-ownership.test.ts keeps them out), so only
 * these stay checkable from the page files: pages that render their own shell
 * resolve to 'none', re-export pages resolve like their target, and every
 * policy entry names a real page. Imports are read with the TypeScript
 * compiler rather than a regex.
 */

const LEARNER_DIR = path.join(process.cwd(), 'app', '(learner)');
const SELF_CHROME_IMPORTS = new Set(['AppShell', 'LearnerLiveRoomShell']);

interface LearnerPage {
  file: string;
  route: string;
  selfChromeImport: boolean;
  /** The page module this page re-exports or renders (`../page`, `../submissions/page`). */
  reExportOf?: string;
}

function relative(file: string) {
  return path.relative(process.cwd(), file).replaceAll(path.sep, '/');
}

function walkPages(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const fullPath = path.join(dir, entry);
    if (statSync(fullPath).isDirectory()) return walkPages(fullPath);
    return entry === 'page.tsx' ? [fullPath] : [];
  });
}

/** `app/(learner)/speaking/(group)/exam/[id]/page.tsx` → `/speaking/exam/[id]`. */
function routeFor(file: string) {
  const segments = path
    .relative(LEARNER_DIR, path.dirname(file))
    .split(path.sep)
    .filter((segment) => segment && !/^\(.*\)$/.test(segment));
  return `/${segments.join('/')}`;
}

function parsePage(file: string): LearnerPage {
  const source = readFileSync(file, 'utf8');
  const sourceFile = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const page: LearnerPage = { file: relative(file), route: routeFor(file), selfChromeImport: false };

  function visit(node: ts.Node) {
    if ((ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) && node.moduleSpecifier && ts.isStringLiteral(node.moduleSpecifier)) {
      const specifier = node.moduleSpecifier.text;
      if (/^\.\.?\/(.*\/)?page$/.test(specifier)) page.reExportOf = relative(path.resolve(path.dirname(file), `${specifier}.tsx`));
      const bindings = ts.isImportDeclaration(node) ? node.importClause?.namedBindings : undefined;
      if (bindings && ts.isNamedImports(bindings)
        && bindings.elements.some((element) => SELF_CHROME_IMPORTS.has((element.propertyName ?? element.name).text))) {
        page.selfChromeImport = true;
      }
    }
    ts.forEachChild(node, visit);
  }

  visit(sourceFile);
  return page;
}

let cachedPages: LearnerPage[] | undefined;
function learnerPages() {
  cachedPages ??= walkPages(LEARNER_DIR).map(parsePage);
  return cachedPages;
}

// Parses every learner page; on a Docker volume mount that can exceed
// vitest's default timeout.
const TIMEOUT = { timeout: 60_000 };

describe('learner shell policy parity', () => {
  it('finds the learner pages', TIMEOUT, () => {
    expect(learnerPages().length).toBeGreaterThan(100);
  });

  it('resolves pages that render AppShell or LearnerLiveRoomShell to no chrome', TIMEOUT, () => {
    const offenders = learnerPages()
      .filter((page) => page.selfChromeImport && resolveLearnerChrome(page.route).mode !== 'none')
      .map((page) => page.route);
    expect(offenders).toEqual([]);
  });

  it('resolves a re-export page like the page it renders', TIMEOUT, () => {
    const pages = learnerPages();
    const reExportMismatches = pages
      .filter((page) => page.reExportOf)
      .filter((page) => {
        const target = pages.find(({ file }) => file === page.reExportOf);
        return !target || resolveLearnerChrome(page.route).mode !== resolveLearnerChrome(target.route).mode;
      })
      .map((page) => `${page.route} -> ${page.reExportOf}`);
    expect(reExportMismatches).toEqual([]);
  });

  it('lists only routes that map to a real learner page', TIMEOUT, () => {
    const routes = new Set(learnerPages().map((page) => page.route));
    const entries = [
      ...LEARNER_SELF_CHROMED_ROUTES,
      ...LEARNER_FOCUS_ROUTES.map(({ route }) => route),
      ...LEARNER_PUBLIC_ROUTES,
      ...IMMERSIVE_LEARNER_ROUTE_PATTERNS,
    ];
    expect(entries.filter((entry) => !routes.has(entry))).toEqual([]);
  });
});
