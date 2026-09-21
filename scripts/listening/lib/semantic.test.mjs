// Run on GitHub Actions: node --test scripts/listening/lib/semantic.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { checkDestinationHead, checkPair, checkSourceTail, checkTail, checkTimer, containment, overall } from './semantic.mjs';

const dialogue = (n, seed) => Array.from({ length: n }, (_, i) => `${seed} word${i} about ${seed}${i % 7} patient symptom${i * 3}`).join('. ');
const seg = (start, end, text) => ({ start, end, text });
const level = (checks, id) => checks.find((c) => c.id === id)?.level;

test('destination head: cue at the start with a full prep window passes', () => {
  const segs = [seg(0.3, 6, 'Now look at the notes for Extract Two.'), seg(6, 20, 'Extract two questions 13 to 24 you hear a consultation')];
  const r = checkDestinationHead('A2', segs, [{ start: 21, end: 51 }]);
  assert.equal(level(r, 'head_cue'), 'pass');
  assert.equal(level(r, 'prep_window'), 'pass');
});

test('destination head: the ST9 straddle (no cue, 15 s of silence then dialogue) fails', () => {
  const r = checkDestinationHead('A2', [seg(15, 30, 'Morning Mr Jackson')], [{ start: 0, end: 14.9 }]);
  assert.equal(level(r, 'head_cue'), 'fail');
});

test('destination head: a short prep window is flagged', () => {
  const segs = [seg(0.2, 8, 'Extract two questions 13 to 24')];
  assert.equal(level(checkDestinationHead('A2', segs, [{ start: 9, end: 20 }]), 'prep_window'), 'fail');
  assert.equal(level(checkDestinationHead('C2', segs, [{ start: 9, end: 60 }]), 'prep_window'), 'review');
});

test('source tail: an Extract Two introduction left at the end of the previous section fails', () => {
  assert.equal(level(checkSourceTail([seg(500, 540, 'That is the end. Now look at the notes for extract two.')], 581), 'next_intro_in_tail'), 'fail');
  assert.equal(level(checkSourceTail([seg(500, 540, 'Okay, well, I will look at your notes again.')], 581), 'next_intro_in_tail'), 'pass');
});

test('pair: same asset, and the ST9 pattern (destination speech already inside the source), both fail', () => {
  const d = dialogue(40, 'jackson');
  const a1 = { assetId: 'x', dur: 580, segs: [seg(0, 250, dialogue(30, 'first')), seg(290, 530, d)] };
  const a2 = { assetId: 'y', dur: 264, segs: [seg(16, 246, d)] };
  const r = checkPair(a1, a2);
  assert.equal(level(r, 'same_asset'), 'pass');
  assert.equal(level(r, 'duplicate_content'), 'fail');
  assert.equal(level(checkPair({ ...a1, assetId: 'z' }, { ...a2, assetId: 'z' }), 'same_asset'), 'fail');
});

test('pair: genuinely different consultations pass', () => {
  const a1 = { assetId: 'a', dur: 300, segs: [seg(0, 280, dialogue(40, 'alpha'))] };
  const a2 = { assetId: 'b', dur: 300, segs: [seg(0, 280, dialogue(40, 'omega'))] };
  const r = checkPair(a1, a2);
  assert.equal(level(r, 'duplicate_content'), 'pass');
  assert.equal(level(r, 'previous_speech_at_head'), 'pass');
  assert.ok(containment(dialogue(40, 'alpha'), dialogue(40, 'omega')) < 0.15);
});

test('tail and timer', () => {
  assert.equal(level(checkTail([{ start: 500, end: 620 }], 620, -90), 'tail_silence'), 'review');
  assert.equal(level(checkTail([], 300, -12), 'abrupt_end'), 'review');
  assert.equal(level(checkTimer(253, 645), 'timer_vs_audio'), 'fail');
  assert.equal(level(checkTimer(650, 645), 'timer_vs_audio'), 'pass');
  assert.equal(level(checkTimer(646, 645), 'timer_vs_audio'), 'review');
  assert.equal(overall([{ level: 'pass' }, { level: 'review' }]), 'review');
  assert.equal(overall([{ level: 'fail' }, { level: 'review' }]), 'fail');
});
