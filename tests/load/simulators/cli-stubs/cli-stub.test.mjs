import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { delayMs, replyText, respond } from './cli-stub.mjs';
import { ENGINES, installShims, shimScript } from './install-shims.mjs';

test('claude login probe is instant and says signed in on the Max plan', () => {
  const r = respond('claude', ['auth', 'status'], '');
  assert.equal(r.code, 0);
  assert.equal(r.wait, false);
  assert.deepEqual(JSON.parse(r.stdout), { loggedIn: true, authMethod: 'claude.ai', subscriptionType: 'max' });
});

test('claude print mode returns the result event the sidecar parses, after the delay', () => {
  const r = respond('claude', ['-p', '--output-format', 'json', '--model', 'm'], 'x'.repeat(40), { SIM_LLM_RESPONSE: 'hello world!' });
  assert.equal(r.code, 0);
  assert.equal(r.wait, true);
  const event = JSON.parse(r.stdout.trim());
  assert.equal(event.type, 'result');
  assert.equal(event.subtype, 'success');
  assert.equal(event.is_error, false);
  assert.equal(event.result, 'hello world!');
  assert.deepEqual(event.usage, { input_tokens: 10, output_tokens: 3 });
});

test('codex probe and exec produce the event stream the sidecar reads', () => {
  assert.equal(respond('codex', ['login', 'status'], '').stdout, 'Logged in using ChatGPT\n');
  const r = respond('codex', ['exec', '--json', '-'], 'p'.repeat(8), { SIM_LLM_RESPONSE: 'abcd' });
  const lines = r.stdout.trim().split('\n').map((l) => JSON.parse(l));
  assert.deepEqual(lines[0], { type: 'item.completed', item: { type: 'agent_message', text: 'abcd' } });
  assert.deepEqual(lines[1], { type: 'turn.completed', usage: { input_tokens: 2, output_tokens: 1 } });
  assert.equal(r.wait, true);
});

test('a configured failure rate produces a quota-looking error the sidecar maps to 429', () => {
  const claude = respond('claude', ['-p'], 'x', { SIM_LLM_FAIL_RATE: '1' }, () => 0);
  assert.equal(claude.code, 1);
  assert.match(claude.stderr, /You have reached/);
  const codex = respond('codex', ['exec', '-'], 'x', { SIM_LLM_FAIL_RATE: '1' }, () => 0);
  assert.equal(codex.code, 1);
  assert.equal(respond('claude', ['-p'], 'x', { SIM_LLM_FAIL_RATE: '0' }, () => 0).code, 0);
});

test('unsupported invocations fail loudly', () => {
  const r = respond('claude', ['--version'], '');
  assert.equal(r.code, 2);
  assert.match(r.stderr, /unsupported invocation/);
  assert.equal(respond('other', [], '').code, 2);
});

test('the delay is configurable with jitter and never negative', () => {
  assert.equal(delayMs({ SIM_LLM_LATENCY_MS: '1000', SIM_LLM_JITTER_MS: '200' }, () => 0.5), 1000);
  assert.equal(delayMs({ SIM_LLM_LATENCY_MS: '1000', SIM_LLM_JITTER_MS: '200' }, () => 1), 1200);
  assert.equal(delayMs({ SIM_LLM_LATENCY_MS: '10', SIM_LLM_JITTER_MS: '200' }, () => 0), 0);
  assert.equal(delayMs({}, () => 0.5), 8000);
});

test('a captured real reply can be replayed from a file', () => {
  const dir = mkdtempSync(join(tmpdir(), 'clistub-'));
  const file = join(dir, 'grade.json');
  writeFileSync(file, '{"score":42}');
  assert.equal(replyText({ SIM_LLM_RESPONSE_FILE: file }), '{"score":42}');
  assert.equal(replyText({}), '{"ok":true,"simulated":true}');
});

test('shims exec the stub with the engine name and pass every argument through', () => {
  assert.equal(shimScript('claude', '/sim/cli-stub.mjs'), '#!/bin/sh\nexec node "/sim/cli-stub.mjs" claude "$@"\n');
  const dir = mkdtempSync(join(tmpdir(), 'shims-'));
  const written = installShims(dir, '/sim/cli-stub.mjs');
  assert.equal(written.length, ENGINES.length);
  for (const file of written) {
    assert.match(readFileSync(file, 'utf8'), /^#!\/bin\/sh\nexec node "\/sim\/cli-stub\.mjs" (claude|codex) "\$@"\n$/);
    // executable bit (not meaningful on Windows file systems)
    if (process.platform !== 'win32') assert.ok((statSync(file).mode & 0o111) !== 0);
  }
});
