import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { HUB_POLL_SECONDS, THINK_RANGES, effectiveThinkSeconds, hubCadenceRows } from './cadence.mjs';

const close = (actual, expected, message) => assert.ok(Math.abs(actual - expected) < 1e-9, `${message ?? ''} expected ${expected}, got ${actual}`);

test('a pause shorter than one poll lasts one whole poll', () => {
  close(effectiveThinkSeconds(1, 3), HUB_POLL_SECONDS);
  close(effectiveThinkSeconds(4, 9), HUB_POLL_SECONDS);
  close(effectiveThinkSeconds(0.15, 0.45), HUB_POLL_SECONDS); // smoke runs think at 15 %: still a full poll
});

test('a longer pause rounds up to whole polls, on average', () => {
  close(effectiveThinkSeconds(15, 25), 30);
  close(effectiveThinkSeconds(8, 25), 405 / 17); // 7/17 of the draws take 15 s, 10/17 take 30 s
  close(effectiveThinkSeconds(12, 30), 27.5);
  close(effectiveThinkSeconds(15, 35), 33.75);
  close(effectiveThinkSeconds(20, 40), 37.5);
});

test('a fixed pause is its own range', () => {
  close(effectiveThinkSeconds(10, 10), 15);
  close(effectiveThinkSeconds(15, 15), 15);
  close(effectiveThinkSeconds(16, 16), 30);
});

test('invalid ranges are refused', () => {
  assert.throws(() => effectiveThinkSeconds(5, 3), RangeError);
  assert.throws(() => effectiveThinkSeconds(-1, 3), RangeError);
  assert.throws(() => effectiveThinkSeconds(1, 3, 0), RangeError);
  assert.throws(() => hubCadenceRows(0), RangeError);
});

test('every modelled activity is at least as slow as its nominal mean', () => {
  const rows = hubCadenceRows(1);
  assert.equal(rows.length, Object.keys(THINK_RANGES).length);
  for (const row of rows) {
    assert.ok(row.effectiveS >= row.nominalS, row.activity);
    assert.ok(row.slowdown >= 1, row.activity);
  }
  const browse = rows.find((row) => row.activity === 'browse tick');
  assert.deepEqual(browse, { activity: 'browse tick', nominalS: 16.5, effectiveS: 23.8, slowdown: 1.44 });
  const turn = rows.find((row) => row.activity === 'speaking turn');
  assert.equal(turn.effectiveS, 15);
});

test('the think scale shrinks the nominal pause but never below one poll', () => {
  const smoke = hubCadenceRows(0.15).find((row) => row.activity === 'browse tick');
  assert.equal(smoke.effectiveS, 15);
  assert.ok(smoke.slowdown > 5);
});

test('THINK_RANGES lists exactly the ranges flows.js uses', () => {
  const flows = readFileSync(new URL('./flows.js', import.meta.url), 'utf8');
  const used = new Set([...flows.matchAll(/\bthink\(sess, (\d+), (\d+)\)/g)].map((match) => `${match[1]},${match[2]}`));
  const modelled = new Set(Object.values(THINK_RANGES).map(([min, max]) => `${min},${max}`));
  assert.ok(used.size >= 8, 'the think() calls were not found');
  assert.deepEqual([...used].sort(), [...modelled].sort());
});
