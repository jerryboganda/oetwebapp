// Guards for the load / benchmark workflows. They run in the load-fleet preflight job, so a regression
// that would let a load gate be swallowed (the `|| true` that hid every speaking-load failure, the
// continue-on-error that made the performance k6 step advisory) or that would let a load test run on a
// schedule or against production fails before any generator time is spent.

import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import test from 'node:test';
import { activeLines, onBlock } from '../../scripts/deploy/verify-pipeline-contract.mjs';

const workflowsDir = new URL('../../.github/workflows/', import.meta.url);
const read = (name) => readFileSync(new URL(name, workflowsDir), 'utf8');
const allWorkflows = readdirSync(workflowsDir).filter((f) => f.endsWith('.yml'));

/** The step (list item) that contains line `index`: [first line, last line] of that step. */
function stepRange(lines, index) {
  let start = index;
  while (start > 0 && !/^\s*- (?:name|uses|run|id|if|env|with):/.test(lines[start])) start -= 1;
  const indent = /^(\s*)/.exec(lines[start])[1].length;
  let end = index;
  for (let i = index + 1; i < lines.length; i += 1) {
    if (lines[i].trim() === '') { end = i; continue; }
    const lineIndent = /^(\s*)/.exec(lines[i])[1].length;
    if (lineIndent <= indent) break;
    end = i;
  }
  return [start, end];
}

/** The shell text of every `run:` step key: an inline value, or the indented lines of a `|` / `>` block. */
function runBodies(source) {
  const lines = source.split('\n');
  const bodies = [];
  for (let i = 0; i < lines.length; i += 1) {
    const match = /^(\s*)(?:- )?run:\s*(.*)$/.exec(lines[i]);
    if (!match) continue;
    const indent = match[1].length;
    const inline = match[2].trim();
    if (inline !== '' && !/^[|>][+-]?$/.test(inline)) {
      bodies.push(inline);
      continue;
    }
    const block = [];
    for (let j = i + 1; j < lines.length; j += 1) {
      if (lines[j].trim() === '') { block.push(''); continue; }
      if (/^(\s*)/.exec(lines[j])[1].length <= indent) break;
      block.push(lines[j].trim());
    }
    bodies.push(block.join('\n').trim());
  }
  return bodies;
}

test('every k6 run in every workflow is gating: no `|| true` and no continue-on-error on its step', () => {
  const offenders = [];
  for (const file of allWorkflows) {
    const lines = activeLines(read(file)).split('\n');
    lines.forEach((line, index) => {
      if (!/\bk6 run\b/.test(line)) return;
      if (/\|\|\s*true\b/.test(line)) offenders.push(`${file}:${index + 1} swallows the k6 exit code with || true`);
      const [start, end] = stepRange(lines, index);
      if (lines.slice(start, end + 1).some((l) => /^\s*continue-on-error:\s*true\b/.test(l))) {
        offenders.push(`${file}:${index + 1} runs k6 in a continue-on-error step`);
      }
    });
  }
  assert.deepEqual(offenders, []);
});

test('the load and benchmark workflows are dispatch-only', () => {
  for (const file of ['load-fleet.yml', 'pdf-extract-bench.yml', 'speaking-load.yml', 'performance.yml']) {
    const block = onBlock(activeLines(read(file)));
    assert.match(block, /^\s+workflow_dispatch:/m, `${file} must keep workflow_dispatch`);
    for (const trigger of ['push', 'pull_request', 'pull_request_target', 'schedule', 'workflow_run']) {
      assert.doesNotMatch(block, new RegExp(`^\\s+${trigger}:`, 'm'), `${file} must not have a ${trigger} trigger`);
    }
  }
});

test('the fleet workflow never swallows a failure and keeps its non-production guard', () => {
  const source = activeLines(read('load-fleet.yml'));
  assert.doesNotMatch(source, /continue-on-error/);
  assert.doesNotMatch(source, /\|\|\s*true\b/);
  assert.match(source, /confirm_non_production/);
  assert.match(source, /scripts\/perf\/load-plan\.mjs/);
  assert.match(source, /k6 run tests\/load\/fleet-1000\.k6\.js/);
  // no inputs are interpolated into shell text: every `${{ ... }}` a run block needs goes through env
  const bodies = runBodies(source);
  assert.ok(bodies.length >= 8, 'the run blocks were not found');
  for (const body of bodies) {
    assert.doesNotMatch(body, /\$\{\{\s*(?:inputs|github\.event\.inputs)\./, 'a run block interpolates a dispatch input directly');
  }
});

test('runBodies reads inline and block scalars by indentation', () => {
  const sample = [
    'jobs:',
    '  a:',
    '    if: ${{ inputs.x }}',
    '    steps:',
    '      - name: one',
    '        run: echo ${{ inputs.bad }}',
    '      - name: two',
    '        env:',
    '          X: ${{ inputs.fine }}',
    '        run: |',
    '          echo "$X"',
    '          echo second',
    '      - name: three',
    '        if: ${{ inputs.also_fine }}',
  ].join('\n');
  assert.deepEqual(runBodies(sample), ['echo ${{ inputs.bad }}', 'echo "$X"\necho second']);
});

test('the harness hard-codes no production host', () => {
  const production = /app\.oetwithdrhesham\.co\.uk|api\.oetwithdrhesham\.co\.uk|185\.252\.233\.186/i;
  for (const file of ['load-fleet.yml', 'pdf-extract-bench.yml']) {
    assert.doesNotMatch(activeLines(read(file)), production, file);
  }
});

test('the PDF benchmark labels itself as evidence, not a release proof', () => {
  const source = read('pdf-extract-bench.yml');
  assert.match(source, /not a release proof/i);
  assert.match(source, /Rust/);
});
