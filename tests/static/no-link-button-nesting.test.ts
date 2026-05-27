import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import ts from 'typescript';

const SOURCE_ROOTS = ['app', 'components'];
const JSX_EXTENSIONS = new Set(['.jsx', '.tsx']);

type TagMatch = {
  tag: 'Link' | 'Button';
  line: number;
  allowsLinkChild?: boolean;
};

type NestingViolation = {
  file: string;
  outerTag: TagMatch['tag'];
  innerTag: TagMatch['tag'];
  outerLine: number;
  innerLine: number;
  snippet: string;
};

function listSourceFiles(directory: string): string[] {
  if (!existsSync(directory)) {
    return [];
  }

  const entries = readdirSync(directory);
  const files: string[] = [];

  for (const entry of entries) {
    const fullPath = path.join(directory, entry);
    const stats = statSync(fullPath);

    if (stats.isDirectory()) {
      if (entry === 'node_modules' || entry === '.next' || entry === '__tests__') {
        continue;
      }
      files.push(...listSourceFiles(fullPath));
      continue;
    }

    if (stats.isFile() && JSX_EXTENSIONS.has(path.extname(entry))) {
      files.push(fullPath);
    }
  }

  return files;
}

function findViolations(file: string): NestingViolation[] {
  const source = readFileSync(file, 'utf8');
  const sourceFile = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const stack: TagMatch[] = [];
  const violations: NestingViolation[] = [];

  function jsxTagName(tagName: ts.JsxTagNameExpression): TagMatch['tag'] | null {
    const text = tagName.getText(sourceFile);
    return text === 'Link' || text === 'Button' ? text : null;
  }

  function lineForNode(node: ts.Node) {
    return sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile)).line + 1;
  }

  function sourceLine(line: number) {
    return source.split(/\r?\n/)[line - 1]?.trim() ?? '';
  }

  function buttonAllowsLinkChild(node: ts.JsxOpeningElement | ts.JsxSelfClosingElement) {
    return node.attributes.properties.some((property) =>
      ts.isJsxAttribute(property) &&
      property.name.getText(sourceFile) === 'asChild' &&
      (property.initializer == null || property.initializer.getText(sourceFile) === '{true}'),
    );
  }

  function isAllowedComposition(parent: TagMatch, innerTag: TagMatch['tag']) {
    return parent.tag === 'Button' && innerTag === 'Link' && parent.allowsLinkChild === true;
  }

  function recordViolation(innerTag: TagMatch['tag'], innerLine: number) {
    const conflictingParent = stack.findLast((openTag) =>
      openTag.tag !== innerTag && !isAllowedComposition(openTag, innerTag),
    );

    if (!conflictingParent) {
      return;
    }

    violations.push({
      file,
      outerTag: conflictingParent.tag,
      innerTag,
      outerLine: conflictingParent.line,
      innerLine,
      snippet: [sourceLine(conflictingParent.line), sourceLine(innerLine)].join('\n'),
    });
  }

  function visit(node: ts.Node) {
    if (ts.isJsxElement(node)) {
      const tag = jsxTagName(node.openingElement.tagName);
      if (!tag) {
        ts.forEachChild(node, visit);
        return;
      }

      const line = lineForNode(node.openingElement);
      recordViolation(tag, line);
      stack.push({ tag, line, allowsLinkChild: tag === 'Button' && buttonAllowsLinkChild(node.openingElement) });
      for (const child of node.children) {
        visit(child);
      }
      stack.pop();
      return;
    }

    if (ts.isJsxSelfClosingElement(node)) {
      const tag = jsxTagName(node.tagName);
      if (tag) {
        recordViolation(tag, lineForNode(node));
      }
      return;
    }

    ts.forEachChild(node, visit);
  }

  visit(sourceFile);
  return violations;
}

describe('static accessibility regression guard', () => {
  // Parses every TSX file under the project's source roots — comfortably
  // exceeds vitest's default 5s timeout on Docker volume mounts. Allow 60s.
  it('does not nest Link and Button primitives in JSX source', { timeout: 60_000 }, () => {
    const projectRoot = process.cwd();
    const sourceFiles = SOURCE_ROOTS.flatMap((root) => listSourceFiles(path.join(projectRoot, root)));
    const violations = sourceFiles.flatMap(findViolations);

    expect(
      violations.map((violation) => {
        const relativeFile = path.relative(projectRoot, violation.file);
        return `${relativeFile}:${violation.outerLine}-${violation.innerLine} ` +
          `<${violation.outerTag}> contains <${violation.innerTag}>\n${violation.snippet}`;
      }),
    ).toEqual([]);
  });
});
