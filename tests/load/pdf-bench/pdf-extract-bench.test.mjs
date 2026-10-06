import assert from 'node:assert/strict';
import test from 'node:test';
import {
  GATE, checkParity, commandFor, compareParity, evaluateRustGate, listPdfs, measureContender, parseArgs, parseResult, percentile,
  renderMarkdown, run, splitCommand, summarizeContender,
} from './pdf-extract-bench.mjs';

const h = (n) => String(n).padStart(64, '0');
const envelope = (pages, extra = {}) => ({
  pageCount: pages.length,
  embeddedChars: pages.reduce((a, p) => a + p, 0),
  textSha256: h(pages.join('') || '0'),
  pageSha256s: pages.map((p) => h(p)),
  ...extra,
});

test('percentile is nearest-rank', () => {
  const v = [5, 1, 3, 2, 4, 10, 9, 8, 7, 6];
  assert.equal(percentile(v, 50), 5);
  assert.equal(percentile(v, 95), 10);
  assert.equal(percentile(v, 0), 1);
  assert.equal(percentile([7], 95), 7);
  assert.equal(percentile([], 50), null);
});

test('splitCommand handles quotes and substitutes {pdf} after splitting, so paths with spaces survive', () => {
  assert.deepEqual(splitCommand('dotnet "/opt/b y/a.dll" extract {pdf}'), ['dotnet', '/opt/b y/a.dll', 'extract', '{pdf}']);
  assert.deepEqual(commandFor('tool --in {pdf} --json', 'docs/a b.pdf'), ['tool', '--in', 'docs/a b.pdf', '--json']);
  assert.deepEqual(splitCommand('a "" b'), ['a', '', 'b']);
  assert.throws(() => splitCommand('a "unterminated'), /unbalanced quote/);
  assert.throws(() => commandFor('tool --in file.pdf', 'x.pdf'), /\{pdf\}/);
});

test('parseResult takes the last JSON line and validates the envelope', () => {
  const good = envelope([10, 20]);
  assert.deepEqual(parseResult(`warming up\n${JSON.stringify(good)}\n`, 't'), good);
  assert.throws(() => parseResult('', 't'), /no output/);
  assert.throws(() => parseResult('nope', 't'), /not JSON/);
  assert.throws(() => parseResult(JSON.stringify({ ...good, pageCount: 'x' }), 't'), /pageCount/);
  assert.throws(() => parseResult(JSON.stringify({ ...good, embeddedChars: -1 }), 't'), /embeddedChars/);
  assert.throws(() => parseResult(JSON.stringify({ ...good, textSha256: 'abc' }), 't'), /sha-256/);
  assert.throws(() => parseResult(JSON.stringify({ ...good, pageSha256s: [h(1)] }), 't'), /one sha-256 per page/);
});

test('parity is byte-exact: counts, characters, every page hash and the text hash', () => {
  const a = envelope([10, 20]);
  assert.deepEqual(compareParity(a, envelope([10, 20])), { ok: true, reason: null });
  assert.match(compareParity(a, envelope([10])).reason, /pageCount/);
  assert.match(compareParity(a, { ...a, embeddedChars: 31 }).reason, /embeddedChars/);
  assert.equal(compareParity(a, { ...a, pageSha256s: [h(10), h(21)] }).reason, 'page 2 differs');
  assert.equal(compareParity(a, { ...a, textSha256: h(99) }).reason, 'textSha256 differs');
});

const pdfs = [{ path: 'a.pdf', bytes: 1000 }, { path: 'b.pdf', bytes: 3000 }];

/** exec that answers per contender (argv[0]) and per pdf (last arg). */
function fakeExec(table, calls = []) {
  return (argv) => {
    calls.push(argv);
    const entry = table[argv[0]]?.[argv[argv.length - 1]];
    if (!entry) return { stdout: '', status: 1, wallMs: 1 };
    const next = typeof entry === 'function' ? entry() : entry;
    return { stdout: `${JSON.stringify(next.result)}\n`, status: 0, wallMs: next.wallMs ?? 100 };
  };
}

test('measureContender drops warm-up runs, takes the median and reports failures per document', () => {
  let n = 0;
  const durations = [999, 30, 10, 20]; // warm-up 1, then 3 measured: median 20
  const exec = fakeExec({
    tool: {
      'a.pdf': () => ({ result: envelope([5], { stats: { durationMs: durations[n++ % 4], cpuMs: 8, peakRssMiB: 64 } }), wallMs: 100 + n }),
    },
  });
  const m = measureContender({ name: 'x', template: 'tool {pdf}', pdfs, iterations: 3, warmup: 1, exec });
  const [a, b] = m.documents;
  assert.equal(a.error, null);
  assert.equal(a.durationMs, 20);
  assert.equal(a.cpuMs, 8);
  assert.equal(a.peakRssMiB, 64);
  assert.match(b.error, /exit 1 for b\.pdf/);
  assert.equal(b.result, null);
});

test('summarizeContender reports corpus p50 / p95 and CPU per job over the documents that ran', () => {
  const docs = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100].map((ms, i) => ({
    path: `${i}.pdf`, bytes: 100, error: null, durationMs: ms, cpuMs: ms / 2, processWallMs: ms + 50, peakRssMiB: 100 + i,
  }));
  docs.push({ path: 'bad.pdf', bytes: 100, error: 'boom', durationMs: null, cpuMs: null, processWallMs: null, peakRssMiB: null });
  const s = summarizeContender({ name: 'c', documents: docs });
  assert.equal(s.documents, 11);
  assert.equal(s.failed, 1);
  assert.equal(s.durationMs.p50, 50);
  assert.equal(s.durationMs.p95, 100);
  assert.equal(s.durationMs.total, 550);
  assert.equal(s.cpuMsPerJob, 27.5);
  assert.equal(s.peakRssMiB, 109);
  assert.equal(s.pdfBytes, 1100);
});

test('checkParity names the documents that differ and refuses to pass an empty comparison', () => {
  const doc = (path, pages) => ({ path, error: null, result: envelope(pages) });
  const oracle = { documents: [doc('a.pdf', [1, 2]), doc('b.pdf', [3])] };
  assert.deepEqual(checkParity(oracle, { documents: [doc('a.pdf', [1, 2]), doc('b.pdf', [3])] }), { checked: 2, mismatches: [], ok: true });
  const bad = checkParity(oracle, { documents: [doc('a.pdf', [2, 1]), { path: 'b.pdf', error: 'exit 1', result: null }] });
  assert.equal(bad.ok, false);
  assert.deepEqual(bad.mismatches, [{ path: 'a.pdf', reason: 'page 1 differs' }, { path: 'b.pdf', reason: 'exit 1' }]);
  assert.equal(checkParity({ documents: [] }, { documents: [] }).ok, false);
});

const base = { durationMs: { p95: 100 }, cpuMsPerJob: 50 };
const parityOk = { ok: true, checked: 40, mismatches: [] };

test('no Rust candidate: the gate is not evaluated and says Rust is not built', () => {
  const g = evaluateRustGate({ baseline: base, candidate: null, parity: undefined });
  assert.equal(g.evaluated, false);
  assert.equal(g.pass, false);
  assert.match(g.decision, /NOT BUILT/);
});

test('the Rust gate: exact parity AND (p95 -30 % OR CPU/job -40 %)', () => {
  assert.equal(GATE.p95Improvement, 0.3);
  assert.equal(GATE.cpuImprovement, 0.4);
  const pass = (candidate, parity = parityOk) => evaluateRustGate({ baseline: base, candidate, parity });
  assert.equal(pass({ durationMs: { p95: 70 }, cpuMsPerJob: 50 }).pass, true); // exactly -30 % p95
  assert.equal(pass({ durationMs: { p95: 71 }, cpuMsPerJob: 50 }).pass, false); // -29 %
  assert.equal(pass({ durationMs: { p95: 100 }, cpuMsPerJob: 30 }).pass, true); // exactly -40 % CPU
  assert.equal(pass({ durationMs: { p95: 100 }, cpuMsPerJob: 31 }).pass, false); // -38 %
  const mismatched = pass({ durationMs: { p95: 10 }, cpuMsPerJob: 5 }, { ok: false, checked: 40, mismatches: [{ path: 'x' }] });
  assert.equal(mismatched.pass, false);
  assert.match(mismatched.decision, /parity is not byte-exact/);
  const noData = pass({ durationMs: { p95: null }, cpuMsPerJob: null });
  assert.equal(noData.pass, false);
  assert.match(noData.decision, /neither threshold met/);
});

test('parseArgs validates', () => {
  assert.throws(() => parseArgs([]), /--bench-dll/);
  assert.throws(() => parseArgs(['--bench-dll', 'x', '--iterations', '0']), /iterations must be >= 1/);
  assert.throws(() => parseArgs(['--bench-dll', 'x', '--warmup', '-1']), /warmup/);
  assert.throws(() => parseArgs(['--bench-dll', 'x', '--max-pdfs', 'a']), /max-pdfs/);
  assert.throws(() => parseArgs(['--bench-dll', 'x', '--nope']), /unknown argument/);
  const a = parseArgs(['--bench-dll', 'b.dll', '--kernel', 'k {pdf}', '--rust', 'r {pdf}', '--steady', '--require-parity']);
  assert.equal(a.kernel, 'k {pdf}');
  assert.equal(a.steady, true);
  assert.equal(a.requireParity, true);
  assert.equal(a.iterations, 5);
});

test('listPdfs reads tracked pdfs from git in a stable order', () => {
  const git = () => ({ status: 0, stdout: 'b/two.pdf\0a/one.pdf\0' });
  const stat = (p) => ({ size: p.length });
  assert.deepEqual(listPdfs(git, stat), [{ path: 'a/one.pdf', bytes: 9 }, { path: 'b/two.pdf', bytes: 9 }]);
  assert.throws(() => listPdfs(() => ({ status: 1, stdout: '' }), stat), /git ls-files failed/);
});

function scenario(kernelOverride) {
  const dllTable = {};
  const toolTable = {};
  const kernelTable = {};
  const e = (pages, durationMs, cpuMs) => ({ result: envelope(pages, { stats: { durationMs, cpuMs, peakRssMiB: 80 } }), wallMs: durationMs + 40 });
  for (const [path, pages] of [['a.pdf', [10, 20]], ['b.pdf', [30]]]) {
    dllTable[path] = e(pages, 100, 90);
    toolTable[path] = e(kernelOverride?.[path] ?? pages, 40, 30);
    kernelTable[path] = e(pages, 105, 95);
  }
  return fakeExec({ dotnet: dllTable, rust: toolTable, kernel: kernelTable });
}

test('run: oracle only produces a baseline and an unevaluated gate', () => {
  const { report, markdown, exitCode } = run(parseArgs(['--bench-dll', 'B.dll', '--iterations', '2', '--warmup', '1']), { exec: scenario(), listPdfs: () => pdfs, cpus: 4, os: 'linux' });
  assert.equal(exitCode, 0);
  assert.deepEqual(Object.keys(report.contenders), ['oracle']);
  assert.deepEqual(report.parity, {});
  assert.equal(report.rustGate.evaluated, false);
  assert.equal(report.corpus.documents, 2);
  assert.match(markdown, /Benchmark evidence, not a release proof/);
  assert.match(markdown, /No second contender was supplied/);
});

test('run: a kernel is compared against the oracle and becomes the baseline; a faster exact Rust candidate passes the gate', () => {
  const { report, exitCode } = run(parseArgs(['--bench-dll', 'B.dll', '--kernel', 'kernel {pdf}', '--rust', 'rust {pdf}', '--iterations', '2', '--warmup', '0', '--require-parity']), {
    exec: scenario(), listPdfs: () => pdfs, cpus: 4, os: 'linux',
  });
  assert.equal(exitCode, 0);
  assert.equal(report.parity.kernel.ok, true);
  assert.equal(report.parity.rust.ok, true);
  assert.equal(report.rustGate.evaluated, true);
  assert.equal(report.rustGate.pass, true); // 40 ms vs the kernel's 105 ms
  assert.ok(report.rustGate.p95Gain > 0.6);
});

test('run: a Rust candidate that differs on one page fails parity, the gate, and --require-parity', () => {
  const { report, markdown, exitCode } = run(parseArgs(['--bench-dll', 'B.dll', '--rust', 'rust {pdf}', '--iterations', '2', '--warmup', '0', '--require-parity']), {
    exec: scenario({ 'a.pdf': [20, 10] }), listPdfs: () => pdfs, cpus: 4, os: 'linux',
  });
  assert.equal(report.parity.rust.ok, false);
  assert.deepEqual(report.parity.rust.mismatches, [{ path: 'a.pdf', reason: 'page 1 differs' }]);
  assert.equal(report.rustGate.pass, false);
  assert.equal(exitCode, 1);
  assert.match(markdown, /1 mismatch/);
  assert.match(markdown, /DO NOT BUILD/);
});

test('run: a failing oracle fails the run and no PDFs is an error', () => {
  const failing = () => ({ stdout: '', status: 1, wallMs: 1 });
  assert.equal(run(parseArgs(['--bench-dll', 'B.dll', '--iterations', '1', '--warmup', '0']), { exec: failing, listPdfs: () => pdfs }).exitCode, 1);
  assert.throws(() => run(parseArgs(['--bench-dll', 'B.dll']), { exec: failing, listPdfs: () => [] }), /no tracked PDFs/);
});

test('run honours --max-pdfs and the optional steady-state loop', () => {
  const loopOut = JSON.stringify({ iterations: [{ wallMs: 10, cpuMs: 8 }, { wallMs: 14, cpuMs: 9 }] });
  const exec = (argv) => (argv[2] === 'loop'
    ? { stdout: `${loopOut}\n`, status: 0, wallMs: 1 }
    : scenario()(argv));
  const { report } = run(parseArgs(['--bench-dll', 'B.dll', '--max-pdfs', '1', '--steady', '--iterations', '2', '--warmup', '0']), {
    exec, listPdfs: () => pdfs, cpus: 2, os: 'linux',
  });
  assert.equal(report.corpus.documents, 1);
  assert.equal(report.steadyState.documents, 1);
  assert.equal(report.steadyState.wallMs.p50, 10);
  assert.match(renderMarkdown(report), /In-process steady state/);
});
