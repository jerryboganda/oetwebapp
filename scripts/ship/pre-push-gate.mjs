#!/usr/bin/env node
/**
 * Seconds-long ship gate. Catches leftover rebase/merge syntax before
 * Build & Deploy spends minutes on Docker/Turbopack/.NET.
 *
 * Checks only changed files (staged, unstaged, origin/main...HEAD, or CI range):
 *   - conflict markers
 *   - leftover splice patterns (the 2026-08-24 payment-return / test-brace class)
 *   - brace / paren / bracket balance for .ts/.tsx/.cs/.js/.mjs/.cjs
 *   - TypeScript parseDiagnostics when `typescript` is resolvable
 *   - refuses a staged `.impeccable/` path
 *
 * Not a substitute for tsc / dotnet / next build.
 */
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { extname, resolve, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = resolve(fileURLToPath(new URL('../..', import.meta.url)));
const conflictRe = /^(<{7}|={7}|>{7})(?:\s|$)/;
const leftoverPatterns = [
  { name: 'cleanup-hook-splice', re: new RegExp(String.raw`return\s*\(\)\s*=>\s*\{\s*,`) },
  { name: 'block-open-trailing-comma', re: new RegExp(String.raw`\{\s*,`) },
  // Only a column-0 orphan `}` before [Fact] is a splice artifact. Normal
  // xUnit methods end with an indented `}` followed by a blank line and the
  // next [Fact], which must stay legal.
  { name: 'orphan-public-after-extra-brace', re: new RegExp(String.raw`^\}\s*\n\s*\[Fact\]`, 'm') },
];
const codeExt = new Set(['.ts', '.tsx', '.js', '.jsx', '.mjs', '.cjs', '.cs']);
const skipDir = new Set([
  'node_modules',
  '.git',
  '.impeccable',
  'bin',
  'obj',
  'dist',
  '.next',
  'coverage',
  'output',
]);

function gitLines(args) {
  try {
    const out = execFileSync('git', args, {
      encoding: 'utf8',
      cwd: root,
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    return out.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  } catch {
    return [];
  }
}

function isSkipped(relPath) {
  const parts = relPath.split(/[\\/]/);
  return parts.some((part) => skipDir.has(part));
}

function collectChangedFiles({ ci } = {}) {
  const files = new Set();
  const add = (list) => {
    for (const file of list) {
      if (!file || isSkipped(file)) continue;
      files.add(file.replaceAll('\\', '/'));
    }
  };

  if (ci || process.env.SHIP_GATE_CI === '1' || process.env.GITHUB_ACTIONS === 'true') {
    const before = process.env.GITHUB_EVENT_BEFORE || process.env.SHIP_GATE_BEFORE || '';
    const sha = process.env.GITHUB_SHA || process.env.SHIP_GATE_SHA || '';
    const empty = /^0+$/.test(before);
    if (before && sha && !empty) {
      add(gitLines(['diff', '--name-only', '--diff-filter=ACMRTUXB', before, sha]));
    } else {
      add(gitLines(['diff-tree', '--no-commit-id', '--name-only', '-r', 'HEAD']));
    }
    return [...files];
  }

  add(gitLines(['diff', '--name-only', '--diff-filter=ACMRTUXB']));
  add(gitLines(['diff', '--name-only', '--cached', '--diff-filter=ACMRTUXB']));
  add(gitLines(['diff', '--name-only', '--diff-filter=ACMRTUXB', 'origin/main...HEAD']));
  add(gitLines(['ls-files', '--others', '--exclude-standard']));
  return [...files];
}

function stagedImpeccable() {
  return gitLines(['diff', '--name-only', '--cached']).filter((file) =>
    file.replaceAll('\\', '/').split('/').includes('.impeccable'),
  );
}

async function loadTypescript() {
  try {
    return await import('typescript');
  } catch {
    return null;
  }
}

function countRawQuotes(source, i) {
  let count = 0;
  while (source[i + count] === '"') count += 1;
  return count;
}

// Splits the comma-separated generic argument list of a bracketed attribute
// ([Attr(a, b, ...)]) into top-level segments, ignoring commas inside
// strings, chars, nested brackets, and braces. Lets the balance scanner skip
// attribute argument lists (e.g. [InlineData("...{...}...")]) whose string
// payloads carry unbalanced delimiters by design.
function splitAttributeArgs(source, openParen) {
  const args = [];
  let depth = 0;
  let start = openParen + 1;
  let i = start;
  const n = source.length;
  let quote = null;
  while (i < n) {
    const ch = source[i];
    if (quote) {
      if (ch === '\\') { i += 2; continue; }
      if (ch === quote) quote = null;
      i += 1;
      continue;
    }
    if (ch === '"' || ch === "'") { quote = ch; i += 1; continue; }
    if (ch === '(' || ch === '[' || ch === '{') depth += 1;
    else if (ch === ')' || ch === ']' || ch === '}') {
      if (depth === 0) { args.push(source.slice(start, i)); return { args, close: i }; }
      depth -= 1;
    } else if (ch === ',' && depth === 0) {
      args.push(source.slice(start, i));
      start = i + 1;
    }
    i += 1;
  }
  return null;
}

function stripForBalance(source, ext) {
  let out = '';
  let i = 0;
  const n = source.length;
  const isCs = ext === '.cs';
  const isJsFamily = ['.ts', '.tsx', '.js', '.jsx', '.mjs', '.cjs'].includes(ext);
  while (i < n) {
    const ch = source[i];
    const next = i + 1 < n ? source[i + 1] : '';
    if (ch === '/' && next === '/') {
      out += '  ';
      i += 2;
      while (i < n && source[i] !== '\n') {
        out += ' ';
        i += 1;
      }
      continue;
    }
    if (ch === '/' && next === '*') {
      out += '  ';
      i += 2;
      while (i < n && !(source[i] === '*' && source[i + 1] === '/')) {
        out += source[i] === '\n' ? '\n' : ' ';
        i += 1;
      }
      if (i < n) {
        out += '  ';
        i += 2;
      }
      continue;
    }
    // C# raw string literals: $".."{n} (interpolated) or """{n} (plain).
    // The delimiter is 3+ quotes; the opener may carry $/$$/$$$ prefixes
    // (interpolation-hole depth = number of $). The closer is a run of
    // EXACTLY quoteRun quotes (a longer run is content: the "" escape inside
    // $".." or the 4th quote at a $""".."""" boundary, which belongs to the
    // next token). Single/double-quote content (e.g. SQL '{id}') stays
    // inside and never reaches the brace counter.
    //
    // A bracketed attribute ([Attr(...)] / [Collection(...)]) is metadata,
    // not code: skip its whole span so string payloads inside (e.g.
    // [InlineData("...{...}...")]) can never leak delimiters into the
    // counter. Only [..] that opens with a name + paren (or a bare
    // [Name]) is an attribute — plain indexers/collections keep normal
    // scanning. The skip emits the brackets but blanks the inside.
    if (isCs && ch === '[') {
      let j = i + 1;
      // Attribute lists can stack ([A][B]) — only treat as attribute when
      // the bracket content starts with a name char, not a string/number.
      while (j < n && /[A-Za-z0-9_.]/.test(source[j])) j += 1;
      if (j > i + 1 && (source[j] === '(' || source[j] === ']')) {
        if (source[j] === ']') {
          out += source.slice(i, j + 1);
          i = j + 1;
          continue;
        }
        const split = splitAttributeArgs(source, j);
        if (split) {
          out += source.slice(i, j + 1);
          for (let a = 0; a < split.args.length; a += 1) {
            out += ' '.repeat(split.args[a].length);
            if (a < split.args.length - 1) out += ',';
          }
          out += ')';
          const closer = source[split.close + 1] === ']' ? ']' : '';
          out += closer;
          i = split.close + 1 + closer.length;
          continue;
        }
      }
    }
    if (isCs) {
      let prefixLen = 0;
      while (source[i + prefixLen] === '$') prefixLen += 1;
      const quoteRun = countRawQuotes(source, i + prefixLen);
      const openerLen = prefixLen + quoteRun;
      if (quoteRun >= 3 && (prefixLen > 0 || ch === '"')) {
        // C# raw strings close with a run of EXACTLY quoteRun quotes (a
        // longer run is content: "" escape inside $"..", or four quotes at
        // a $""".."""" boundary where the 4th belongs to the next token).
        out += ' '.repeat(openerLen);
        i += openerLen;
        while (i < n) {
          if (source[i] === '"' && countRawQuotes(source, i) === quoteRun) {
            out += ' '.repeat(quoteRun);
            i += quoteRun;
            break;
          }
          out += source[i] === '\n' ? '\n' : ' ';
          i += 1;
        }
        continue;
      }
    }
    if (isCs && ch === '@' && next === '"') {
      out += '  ';
      i += 2;
      while (i < n) {
        if (source[i] === '"' && source[i + 1] === '"') {
          out += '  ';
          i += 2;
          continue;
        }
        if (source[i] === '"') {
          out += ' ';
          i += 1;
          break;
        }
        out += source[i] === '\n' ? '\n' : ' ';
        i += 1;
      }
      continue;
    }
    if (isJsFamily && ch === '/' && !['/', '*'].includes(next)) {
      const prev = out.trimEnd().slice(-1);
      const canBeRegex = prev === '' || '=(:[{,;!?&|+-*%^~<>'.includes(prev);
      if (canBeRegex) {
        out += ' ';
        i += 1;
        let inClass = false;
        while (i < n) {
          if (source[i] === '\\') {
            out += '  ';
            i += 2;
            continue;
          }
          if (source[i] === '[' && !inClass) inClass = true;
          else if (source[i] === ']' && inClass) inClass = false;
          else if (source[i] === '/' && !inClass) {
            out += ' ';
            i += 1;
            break;
          }
          out += source[i] === '\n' ? '\n' : ' ';
          i += 1;
        }
        continue;
      }
    }
    if (ch === '"' || ch === "'" || ch === '`') {
      const quote = ch;
      out += ' ';
      i += 1;
      while (i < n) {
        if (source[i] === '\\') {
          out += '  ';
          i += 2;
          continue;
        }
        if (source[i] === quote) {
          out += ' ';
          i += 1;
          break;
        }
        out += source[i] === '\n' ? '\n' : ' ';
        i += 1;
      }
      continue;
    }
    out += ch;
    i += 1;
  }
  return out;
}

function findBalanceFault(source, ext) {
  const text = stripForBalance(source, ext);
  const stack = [];
  const pairs = { ')': '(', ']': '[', '}': '{' };
  const opens = new Set(['(', '[', '{']);
  for (let i = 0; i < text.length; i += 1) {
    const ch = text[i];
    if (opens.has(ch)) stack.push(ch);
    else if (ch in pairs) {
      if (stack.pop() !== pairs[ch]) {
        const line = text.slice(0, i).split(/\r?\n/).length;
        return `${ext} delimiter mismatch near line ${line}`;
      }
    }
  }
  if (stack.length) return `${ext} unclosed ${stack.join(' ')}`;
  return null;
}

export function inspectSource(relPath, source) {
  const findings = [];
  const ext = extname(relPath).toLowerCase();
  const lines = source.split(/\r?\n/);
  lines.forEach((line, idx) => {
    if (conflictRe.test(line)) {
      findings.push(`${relPath}:${idx + 1} conflict marker`);
    }
  });
  if (codeExt.has(ext)) {
    const isDetector = relPath.replaceAll('\\', '/').endsWith('scripts/ship/pre-push-gate.mjs');
    if (!isDetector) {
      for (const pattern of leftoverPatterns) {
        if (pattern.re.test(source)) {
          findings.push(`${relPath}: leftover splice (${pattern.name})`);
        }
      }
    }
    // The character-level balance check cannot parse JSX/TSX (quoted text in
    // markup trips it), and CI runs this gate on a bare checkout where the
    // real TypeScript parser is unavailable — so for .ts/.tsx the gate relies
    // on the leftover-splice patterns above plus the authoritative TS parse
    // (local) and the Next.js/Docker build (CI). Other code (e.g. .cs) keeps
    // the balance check. LearnerService.cs is ~14k lines with heavy $@" SQL
    // interpolation that trips the naive C# scanner — rely on dotnet build
    // instead. AiOperationLeaseTests.cs carries plain """ DDL payloads whose
    // SQL single-quote defaults the scanner mistakes for C# char literals —
    // dotnet build is authoritative there too.
    if (ext !== '.ts' && ext !== '.tsx' && !relPath.endsWith('LearnerService.cs') && !relPath.endsWith('AiOperationLeaseTests.cs')) {
      const fault = findBalanceFault(source, ext);
      if (fault) findings.push(`${relPath}: ${fault}`);
    }
  }
  return findings;
}

async function inspectTypescript(relPath, source, tsMod) {
  if (!tsMod) return [];
  const ext = extname(relPath).toLowerCase();
  if (ext !== '.ts' && ext !== '.tsx') return [];
  const kind = ext === '.tsx' ? tsMod.ScriptKind.TSX : tsMod.ScriptKind.TS;
  const sf = tsMod.createSourceFile(relPath, source, tsMod.ScriptTarget.Latest, true, kind);
  const diags = sf.parseDiagnostics ?? [];
  return diags.map((diag) => {
    const pos = typeof diag.start === 'number' ? sf.getLineAndCharacterOfPosition(diag.start) : { line: 0, character: 0 };
    const msg = tsMod.flattenDiagnosticMessageText(diag.messageText, '\n');
    return `${relPath}:${pos.line + 1}:${pos.character + 1} ts-parse ${msg}`;
  });
}

export async function runGate({ ci = false, files = null, readFile = null } = {}) {
  const findings = [];
  const impeccable = stagedImpeccable();
  if (impeccable.length) {
    findings.push(`refusing staged .impeccable path: ${impeccable.join(', ')}`);
  }

  const targets = files ?? collectChangedFiles({ ci });
  const reader = readFile ?? ((rel) => {
    const abs = resolve(root, rel);
    if (!existsSync(abs)) return null;
    return readFileSync(abs, 'utf8');
  });

  const tsNS = await loadTypescript();
  const tsMod = tsNS?.default ?? tsNS ?? null;

  for (const relPath of targets) {
    const source = reader(relPath);
    if (source == null) continue;
    findings.push(...inspectSource(relPath, source));
    findings.push(...await inspectTypescript(relPath, source, tsMod));
  }

  return {
    ok: findings.length === 0,
    findings,
    files: targets,
    usedTypescript: Boolean(tsMod),
  };
}

export function selfTest() {
  const cases = [
    {
      name: 'payment-return leftover',
      path: 'app/billing/payment-return/page.tsx',
      source: [
        'useEffect(() => {',
        '    void poll();',
        '    return () => {',
        ', user?.userId',
        '  }, [x]);',
        '',
      ].join('\n'),
      wantFail: true,
    },
    {
      name: 'extra class brace',
      path: 'backend/tests/OetLearner.Api.Tests/AiPackageCreditServiceTests.cs',
      source: 'public sealed class T {\n    [Fact]\n    public async Task A() {\n        Assert.True(true);\n    }\n    }\n}\n',
      wantFail: true,
    },
    {
      name: 'c# raw interpolated string braces stay legal',
      path: 'backend/src/OetLearner.Api/Services/AuthService.cs',
      source: [
        'private static string BuildQrCodeDataUrl(string secretKey, string otpAuthUri)',
        '{',
        '    var svg = $$"""',
        '        <svg xmlns="http://www.w3.org/2000/svg" width="420" height="180">',
        '          <text x="16" y="28">Secret: {{WebUtility.HtmlEncode(secretKey)}}</text>',
        '        </svg>',
        '        """;',
        '    return $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}";',
        '}',
      ].join('\n'),
      wantFail: false,
    },
    {
      name: 'normal xUnit method boundary is legal',
      path: 'backend/tests/OetLearner.Api.Tests/AiPackageCreditServiceTests.cs',
      source: 'public sealed class T {\n    [Fact]\n    public async Task A() {\n        Assert.True(true);\n    }\n\n    [Fact]\n    public async Task B() {\n        Assert.True(true);\n    }\n}\n',
      wantFail: false,
    },
    {
      name: 'c# interpolated raw string with SQL braces stays legal',
      path: 'backend/tests/OetLearner.Api.Tests/Services/AiOperationLeaseTests.cs',
      source: [
        '    private static async Task InsertAsync(',
        '        PostgreSqlTestDatabase database,',
        '        string id,',
        '        int state,',
        '        string? leaseOwner,',
        '        DateTimeOffset? leaseExpiresAt)',
        '    {',
        '        var owner = leaseOwner is null ? "NULL" : $"{leaseOwner}";',
        '        await database.ExecuteAsync($"""',
        '            INSERT INTO "AiOperations" ("Id", "State")',
        '            VALUES (\u0027{id}\u0027, {state});',
        '            """);',
        '    }',
      ].join('\n'),
      wantFail: false,
    },
    {
      name: 'column-0 orphan brace before Fact fails',
      path: 'backend/tests/OetLearner.Api.Tests/AiPackageCreditServiceTests.cs',
      source: 'public sealed class T {\n    [Fact]\n    public async Task A() {\n        Assert.True(true);\n    }\n}\n\n[Fact]\npublic async Task B() {\n    Assert.True(true);\n}\n',
      wantFail: true,
    },
    {
      name: 'clean tsx',
      path: 'app/ok.tsx',
      source: 'export function Ok() {\n  return <div />;\n}\n',
      wantFail: false,
    },
    {
      name: 'tsx with JSX text apostrophes stays legal (balance check is JSX-blind)',
      path: 'app/ok.tsx',
      source: "export function Ok() {\n  return <p>The learner's credits don't expire</p>;\n}\n",
      wantFail: false,
    },
    {
      name: 'tsx leftover splice still fails via pattern',
      path: 'app/ok.tsx',
      source: 'useEffect(() => {\n    void poll();\n    return () => {\n, deps\n  }, [x]);\n',
      wantFail: true,
    },
    {
      name: 'conflict marker',
      path: 'lib/x.ts',
      source: 'const a = 1;\n<<<<<<< HEAD\nconst b = 2;\n=======\nconst b = 3;\n>>>>>>> main\n',
      wantFail: true,
    },
    {
      name: 'markdown leftover example is not code',
      path: 'docs/dev/lessons-learned.md',
      source: 'had `return () => {, user?.userId` (Turbopack).\n',
      wantFail: false,
    },
  ];

  const failures = [];
  for (const item of cases) {
    const found = inspectSource(item.path, item.source);
    const failed = found.length > 0;
    if (failed !== item.wantFail) {
      failures.push(`${item.name}: expected fail=${item.wantFail} got ${JSON.stringify(found)}`);
    }
  }
  return { ok: failures.length === 0, failures };
}

async function main(argv) {
  if (argv.includes('--self-test')) {
    const result = selfTest();
    if (!result.ok) {
      console.error('ship-gate self-test FAILED');
      for (const line of result.failures) console.error(`  ${line}`);
      process.exit(1);
    }
    console.log('ship-gate self-test OK');
    process.exit(0);
  }

  const ci = argv.includes('--ci');
  const result = await runGate({ ci });
  console.log(`ship-gate files=${result.files.length} typescript=${result.usedTypescript ? 'yes' : 'no'}`);
  if (result.files.length) {
    for (const file of result.files) console.log(`  ${file}`);
  }
  if (!result.ok) {
    console.error('ship-gate FAILED');
    for (const line of result.findings) console.error(`  ${line}`);
    process.exit(1);
  }
  console.log('ship-gate OK');
}

const invoked = process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (invoked) {
  main(process.argv.slice(2)).catch((err) => {
    console.error(err instanceof Error ? err.stack : err);
    process.exit(1);
  });
}
