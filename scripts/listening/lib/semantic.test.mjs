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

test('destination head: a short prep window is flagged (half prep = fail, two thirds = review)', () => {
  const segs = [seg(0.2, 8, 'Extract two questions 37 to 42')];
  assert.equal(level(checkDestinationHead('A2', [seg(0.2, 8, 'Extract two questions 13 to 24')], [{ start: 9, end: 24.4 }]), 'prep_window'), 'fail'); // 15.4 s of 30 s
  assert.equal(level(checkDestinationHead('C2', segs, [{ start: 9, end: 54.4 }]), 'prep_window'), 'fail');   // 45.4 s of 90 s
  assert.equal(level(checkDestinationHead('C2', segs, [{ start: 9, end: 74 }]), 'prep_window'), 'review');   // 65 s
  assert.equal(level(checkDestinationHead('C2', segs, [{ start: 9, end: 99 }]), 'prep_window'), 'pass');
});

test('prep window is judged against the original recording; click-split pauses are merged', () => {
  const segs = [seg(0.2, 8, 'Extract two questions 13 to 24')];
  // a pause split by 10 ms clicks (Nova 12 source): 29.25 s in total, not 7.9 s
  const split = [{ start: 9, end: 16.9 }, { start: 16.91, end: 22.5 }, { start: 22.51, end: 29.2 }, { start: 29.2, end: 38.3 }];
  assert.equal(level(checkDestinationHead('A2', segs, split, null), 'prep_window'), 'pass');
  // the source itself only has a 60.5 s pause (Nova 20): 60.5 s is faithful, 45 s is a truncation
  const c = [seg(0.1, 6, 'Now look at Extract 2. Questions 37 to 42')];
  assert.equal(level(checkDestinationHead('C2', c, [{ start: 8, end: 68.5 }], 60.5), 'prep_window'), 'pass');
  assert.equal(level(checkDestinationHead('C2', c, [{ start: 8, end: 44 }], 60.5), 'prep_window'), 'fail');
  // source 30 s but only 15.4 s left (Atlas 6/7): fail
  assert.equal(level(checkDestinationHead('A2', segs, [{ start: 9, end: 24.4 }], 30.2), 'prep_window'), 'fail');
});

test('destination head: the question range ALONE ("...questions 13 to 24...") does not count as the cue', () => {
  // Atlas ST15 A2: the boundary clipped mid-"You hear a urologist talking to a patient named Mark Jenkins",
  // leaving only the surname + question range at the head — must fail, not pass on the range mention alone.
  const r = checkDestinationHead('A2', [seg(0, 7, 'Jenkins. For questions 13 to 24 complete the notes with a word or short phrase.')], [{ start: 8, end: 38 }]);
  assert.equal(level(r, 'head_cue'), 'fail');
  assert.match(r.find((c) => c.id === 'head_cue').detail, /question-range mention/);
  // but the range PLUS the real cue together still pass
  const ok = checkDestinationHead('A2', [seg(0, 7, 'Extract two. Questions 13 to 24. You hear a urologist talking to a patient.')], [{ start: 8, end: 38 }]);
  assert.equal(level(ok, 'head_cue'), 'pass');
});

test('destination head: Part C speaker-led intro ("you hear a NAME called X, ROLE, giving/discussing Y") is recognized', () => {
  const c1 = [seg(0, 5, 'You hear a clinical dietitian called Rebecca Hudson giving a presentation to a group of healthcare providers.')];
  assert.equal(level(checkDestinationHead('C2', c1, [{ start: 8, end: 98 }], 90), 'head_cue'), 'pass');
  const c2 = [seg(0, 5, 'You hear a specialist in health and nutrition called Dr. Gregor McGregor discussing the ideal healthy diet for humans.')];
  assert.equal(level(checkDestinationHead('C2', c2, [{ start: 7, end: 68 }], 60.6), 'head_cue'), 'pass');
});

test('destination head: ASR mishearing "Extract" as "Act"/"Track" still finds the cue via the question range', () => {
  assert.equal(level(checkDestinationHead('A2', [seg(0, 6, 'Track 2, questions 13 to 24.')], [{ start: 8, end: 40 }]), 'head_cue'), 'pass');
  assert.equal(level(checkDestinationHead('A2', [seg(0, 6, 'Act 2, Questions 13-24.')], [{ start: 8, end: 40 }]), 'head_cue'), 'pass');
});

test('source tail: the next extract introduction left at the end of the previous section fails', () => {
  assert.equal(level(checkSourceTail([seg(500, 540, 'That is the end. Now look at the notes for extract two.')], 581), 'next_intro_in_tail'), 'fail');
  assert.equal(level(checkSourceTail([seg(500, 540, 'Okay, well, I will look at your notes again.')], 581), 'next_intro_in_tail'), 'pass');
  // Atlas 12-15 style: no "Extract Two", just "You will hear part of a consultation between ..." at the tail
  assert.equal(level(checkSourceTail([seg(226, 232, 'You will hear part of a consultation between a GP and a patient called Mr Martin.')], 238), 'next_intro_in_tail'), 'fail');
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
  assert.equal(level(checkTail([], 300, -12), 'abrupt_end'), 'review'); // loud end and no transcript
  assert.equal(level(checkTail([], 300, -12, [seg(290, 299.8, 'See you next time.')]), 'abrupt_end'), 'pass'); // complete sentence
  assert.equal(level(checkTail([], 300, -12, [seg(290, 299.8, 'Now turn over and look at the')]), 'abrupt_end'), 'review'); // cut mid-sentence
  assert.equal(level(checkTail([], 300, -12, [seg(290, 299, 'Well, that is all.'), seg(299, 300, 'You')]), 'abrupt_end'), 'pass'); // trailing hallucination ignored
  assert.equal(level(checkTimer(253, 645), 'timer_vs_audio'), 'fail');
  assert.equal(level(checkTimer(650, 645), 'timer_vs_audio'), 'pass');
  assert.equal(level(checkTimer(646, 645), 'timer_vs_audio'), 'review');
  assert.equal(overall([{ level: 'pass' }, { level: 'review' }]), 'review');
  assert.equal(overall([{ level: 'fail' }, { level: 'review' }]), 'fail');
});
