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
 * resolveLearnerChrome() must describe exactly what each page under
 * app/(learner) renders today, so the group layout can own the shell without
 * changing any page's chrome. Pages are parsed with the TypeScript compiler:
 * shell opening tags contain `=>` and JSX, which a regex cannot read.
 */

const LEARNER_DIR = path.join(process.cwd(), 'app', '(learner)');
const SELF_CHROME_IMPORTS = new Set(['AppShell', 'LearnerLiveRoomShell']);

interface ShellUsage {
  focus: boolean;
  requireAuth: boolean;
  /** `distractionFree` or `requireAuth` is an expression the layout cannot resolve from the URL. */
  dynamic: boolean;
  /** The `pageTitle` literal, or the key of its `t('…')` call. */
  title?: string;
  titleKey?: string;
}

interface LearnerPage {
  file: string;
  route: string;
  shells: ShellUsage[];
  selfChromeImport: boolean;
  /** The page module this page re-exports or renders (`../page`, `../submissions/page`). */
  reExportOf?: string;
  redirectStub: boolean;
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
  const page: LearnerPage = { file: relative(file), route: routeFor(file), shells: [], selfChromeImport: false, redirectStub: false };

  function shellUsage(node: ts.JsxOpeningElement | ts.JsxSelfClosingElement): ShellUsage {
    const usage: ShellUsage = { focus: false, requireAuth: true, dynamic: false };
    for (const property of node.attributes.properties) {
      if (!ts.isJsxAttribute(property)) continue;
      const name = property.name.getText(sourceFile);
      const initializer = property.initializer;
      const expression = initializer && ts.isJsxExpression(initializer) ? initializer.expression : undefined;
      const isTrue = !initializer || expression?.kind === ts.SyntaxKind.TrueKeyword;
      const isFalse = expression?.kind === ts.SyntaxKind.FalseKeyword;

      if (name === 'distractionFree' || name === 'requireAuth') {
        if (!isTrue && !isFalse) usage.dynamic = true;
        if (name === 'distractionFree') usage.focus = isTrue;
        else usage.requireAuth = !isFalse;
      }
      if (name === 'pageTitle') {
        if (initializer && ts.isStringLiteral(initializer)) usage.title = initializer.text;
        else if (expression && ts.isStringLiteralLike(expression)) usage.title = expression.text;
        else if (
          expression && ts.isCallExpression(expression)
          && expression.expression.getText(sourceFile) === 't'
          && expression.arguments[0] && ts.isStringLiteralLike(expression.arguments[0])
        ) {
          usage.titleKey = expression.arguments[0].text;
        }
      }
    }
    return usage;
  }

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
    if ((ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node))
      && node.tagName.getText(sourceFile) === 'LearnerDashboardShell') {
      page.shells.push(shellUsage(node));
    }
    ts.forEachChild(node, visit);
  }

  visit(sourceFile);
  page.redirectStub = page.shells.length === 0 && source.split('\n').length < 20 && source.includes('redirect(');
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
    expect(learnerPages().filter((page) => page.shells.length > 0).length).toBeGreaterThan(100);
  });

  it('gives every LearnerDashboardShell page the chrome mode and auth it asks for', TIMEOUT, () => {
    const failures: string[] = [];
    for (const page of learnerPages().filter(({ shells }) => shells.length > 0)) {
      const [first, ...rest] = page.shells;
      const chrome = resolveLearnerChrome(page.route);
      if (page.shells.some((usage) => usage.dynamic)) {
        failures.push(`${page.file}: distractionFree/requireAuth must be literal`);
      }
      if (rest.some((usage) => usage.focus !== first.focus || usage.requireAuth !== first.requireAuth)) {
        failures.push(`${page.file}: shell usages disagree on distractionFree/requireAuth`);
      }
      if (chrome.mode === 'none') failures.push(`${page.route}: renders LearnerDashboardShell but resolves to 'none'`);
      if ((chrome.mode === 'focus') !== first.focus) {
        failures.push(`${page.route}: distractionFree=${first.focus} but resolves to '${chrome.mode}'`);
      }
      if (chrome.requireAuth !== first.requireAuth) {
        failures.push(`${page.route}: requireAuth=${first.requireAuth} but resolves to ${chrome.requireAuth}`);
      }
    }
    expect(failures).toEqual([]);
  });

  it('resolves pages that render AppShell or LearnerLiveRoomShell to no chrome', TIMEOUT, () => {
    const offenders = learnerPages()
      .filter((page) => page.selfChromeImport && resolveLearnerChrome(page.route).mode !== 'none')
      .map((page) => page.route);
    expect(offenders).toEqual([]);
  });

  it('resolves shell-less pages to no chrome, except redirect stubs and re-exports', TIMEOUT, () => {
    const pages = learnerPages();
    const offenders = pages
      .filter((page) => page.shells.length === 0 && !page.selfChromeImport && !page.reExportOf && !page.redirectStub)
      .filter((page) => resolveLearnerChrome(page.route).mode !== 'none')
      .map((page) => page.route);
    expect(offenders).toEqual([]);

    // A re-export page renders another page's shell, so it resolves like that page.
    const reExportMismatches = pages
      .filter((page) => page.reExportOf)
      .filter((page) => {
        const target = pages.find(({ file }) => file === page.reExportOf);
        return !target || resolveLearnerChrome(page.route).mode !== resolveLearnerChrome(target.route).mode;
      })
      .map((page) => `${page.route} -> ${page.reExportOf}`);
    expect(reExportMismatches).toEqual([]);
  });

  it('titles focus routes with the page title copy or i18n key', TIMEOUT, () => {
    const failures: string[] = [];
    for (const page of learnerPages().filter(({ shells }) => shells.some((usage) => usage.focus))) {
      const { title, titleKey } = resolveLearnerChrome(page.route);
      for (const usage of page.shells) {
        if (usage.title !== title || usage.titleKey !== titleKey) {
          failures.push(`${page.route}: pageTitle ${usage.titleKey ?? usage.title} vs policy ${titleKey ?? title}`);
        }
      }
    }
    expect(failures).toEqual([]);
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
