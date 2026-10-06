import assert from 'node:assert/strict';
import test from 'node:test';
import {
  buildTimeline, countRole, describeTimeline, globalIndex, legShare, normalizeParams, personaOf, planLeg,
  roleOf, roomOrdinalOf, signinSpacingSeconds,
} from './profiles.mjs';

const params = (profile, overrides = {}) => normalizeParams(profile, overrides);

test('the 20-learner role cycle yields exactly 850 / 100 / 50 of 1,000 learners', () => {
  assert.equal(countRole(1000, 'learner'), 850);
  assert.equal(countRole(1000, 'speaker'), 100);
  assert.equal(countRole(1000, 'room'), 50);
  assert.equal(roomOrdinalOf(19), 0);
  assert.equal(roomOrdinalOf(1999), 99);
  assert.equal(roleOf(19), 'room');
  assert.equal(roleOf(17), 'speaker');
  assert.equal(roleOf(18), 'speaker');
  assert.equal(roleOf(0), 'learner');
});

test('a room learner without a provisioned room browses instead', () => {
  assert.equal(roleOf(19, 0), 'learner');
  assert.equal(roleOf(19, 1), 'room');
  assert.equal(roleOf(39, 1), 'learner');
  assert.equal(countRole(1000, 'room', 10), 10);
});

test('personas split the 850 browsing learners 200 / 150 / 200 / 300', () => {
  const counts = { reader: 0, listener: 0, writer: 0, browser: 0 };
  for (let g = 0; g < 1000; g += 1) if (roleOf(g) === 'learner') counts[personaOf(g)] += 1;
  assert.deepEqual(counts, { reader: 200, listener: 150, writer: 200, browser: 300 });
});

test('legs partition the global learner indexes densely with no collisions', () => {
  for (const [total, legs] of [[1000, 4], [1000, 6], [1500, 6], [20, 1], [7, 3], [3, 5]]) {
    const seen = new Set();
    let sum = 0;
    for (let leg = 0; leg < legs; leg += 1) {
      const share = legShare(total, legs, leg);
      sum += share;
      for (let local = 0; local < share; local += 1) {
        const g = globalIndex(local, legs, leg);
        assert.ok(g >= 0 && g < total, `g ${g} out of range for total ${total}`);
        assert.ok(!seen.has(g), `duplicate learner ${g}`);
        seen.add(g);
      }
    }
    assert.equal(sum, total);
    assert.equal(seen.size, total);
  }
});

test('legShare rejects invalid arguments', () => {
  assert.throws(() => legShare(-1, 1, 0), RangeError);
  assert.throws(() => legShare(10, 0, 0), RangeError);
  assert.throws(() => legShare(10, 2, 2), RangeError);
});

test('normalizeParams validates and rejects bad input', () => {
  assert.throws(() => normalizeParams('nope'), /unknown profile/);
  assert.throws(() => params('steady', { legCount: 2, legIndex: 2 }), /legIndex/);
  assert.throws(() => params('capacity', { stageFractions: [] }), /stageFractions/);
  assert.throws(() => params('capacity', { stageFractions: [0, 1] }), /stageFractions/);
  assert.throws(() => params('steady', { learners: 0 }), /learners/);
  assert.equal(params('steady').learners, 1000);
  assert.equal(params('steady').steadyMinutes, 60);
  assert.equal(params('overload').surgeLearners, 500);
});

test('steady: 4 legs at 60 sign-ins/min/leg ramp 1,000 learners in 250 s and hold 60 minutes', () => {
  const p = params('steady', { legCount: 4, legIndex: 0 });
  assert.equal(signinSpacingSeconds(p), 0.25);
  const t = buildTimeline(p);
  assert.equal(t.totalLearners, 1000);
  assert.equal(t.durationS, 250 + 120 + 3600 + 180);
  assert.equal(t.startS(0), 0);
  assert.equal(t.startS(999), 249.75);
  // one leg signs in once per second (60 / min), which is what keeps one source IP under 100 / min
  assert.equal(t.startS(4) - t.startS(0), 1);
  assert.equal(t.phaseAt(100).phase, 'warmup');
  assert.equal(t.phaseAt(370).phase, 'steady');
  assert.equal(t.phaseAt(3969.9).phase, 'steady');
  assert.equal(t.phaseAt(3970).phase, 'cooldown');
  assert.ok(t.endS(999) >= 3970 && t.endS(999) <= t.durationS);
  assert.equal(t.isSurge(999), false);
});

test('capacity: stages 100 / 300 / 600 / 1000 ramp in order and are measured only while holding', () => {
  const t = buildTimeline(params('capacity', { legCount: 1 }));
  assert.deepEqual(t.stages.map((s) => s.target), [100, 300, 600, 1000]);
  assert.deepEqual(t.stages.map((s) => [s.steadyFrom, s.steadyTo]), [
    [160, 760], [1020, 1620], [1980, 2580], [3040, 3640],
  ]);
  assert.equal(t.durationS, 3640 + 180);
  assert.equal(t.startS(0), 0);
  assert.equal(t.startS(100), 760);
  assert.equal(t.startS(299), 959);
  assert.equal(t.startS(300), 1620);
  assert.deepEqual(t.phaseAt(200), { phase: 'steady', stage: 100 });
  assert.equal(t.phaseAt(800).phase, 'ramp');
  assert.deepEqual(t.phaseAt(1100), { phase: 'steady', stage: 300 });
  assert.equal(t.phaseAt(3700).phase, 'cooldown');
});

test('overload: 1,000 base learners, a 500-learner surge, then recovery', () => {
  const p = params('overload', { legCount: 1 });
  const t = buildTimeline(p);
  assert.equal(t.totalLearners, 1500);
  assert.equal(t.baseLearners, 1000);
  assert.equal(t.surgeFromS, 1720);
  assert.equal(t.startS(999), 999);
  assert.equal(t.startS(1000), 1720);
  assert.equal(t.startS(1499), 2219);
  assert.equal(t.phaseAt(1500).phase, 'steady');
  assert.equal(t.phaseAt(1720).phase, 'overload');
  assert.equal(t.phaseAt(3419).phase, 'overload');
  assert.equal(t.phaseAt(3420).phase, 'subside');
  assert.equal(t.phaseAt(3540).phase, 'recovery');
  assert.equal(t.phaseAt(4139).phase, 'recovery');
  assert.equal(t.phaseAt(4140).phase, 'cooldown');
  assert.equal(t.endS(1000), 3420);
  assert.ok(t.endS(1499) < 3540, 'surge learners leave before recovery is measured');
  assert.ok(t.endS(0) >= 4140);
  assert.equal(t.durationS, 4140 + 180);
  assert.equal(t.isSurge(999), false);
  assert.equal(t.isSurge(1000), true);
});

test('smoke: a short single-leg run still has a steady window', () => {
  const t = buildTimeline(params('smoke'));
  assert.equal(t.totalLearners, 20);
  assert.equal(t.phaseAt(35).phase, 'steady');
  assert.equal(t.phaseAt(145).phase, 'cooldown');
  assert.equal(t.durationS, 160);
});

test('planLeg splits roles, personas and experts exactly across legs', () => {
  const p0 = params('steady', { legCount: 4, legIndex: 0 });
  const t = buildTimeline(p0);
  const totals = { learners: 0, speaker: 0, room: 0, learner: 0, experts: 0 };
  for (let leg = 0; leg < 4; leg += 1) {
    const plan = planLeg(t, params('steady', { legCount: 4, legIndex: leg }), Number.POSITIVE_INFINITY, 50);
    totals.learners += plan.learners;
    totals.speaker += plan.counts.speaker;
    totals.room += plan.counts.room;
    totals.learner += plan.counts.learner;
    totals.experts += plan.experts;
  }
  assert.deepEqual(totals, { learners: 1000, speaker: 100, room: 50, learner: 850, experts: 50 });
});

test('only manifest-paired rooms get an expert; the rest run learner-side only', () => {
  const t = buildTimeline(params('steady', { legCount: 2, legIndex: 0 }));
  const a = planLeg(t, params('steady', { legCount: 2, legIndex: 0 }), Number.POSITIVE_INFINITY, 7);
  const b = planLeg(t, params('steady', { legCount: 2, legIndex: 1 }), Number.POSITIVE_INFINITY, 7);
  assert.equal(a.counts.room + b.counts.room, 50);
  assert.equal(a.experts + b.experts, 7);
  assert.equal(a.pairedRooms, 7);
  // no manifest: nobody is paired
  assert.equal(planLeg(t, params('steady', { legCount: 2, legIndex: 0 })).experts, 0);
});

test('a room cap turns the surplus room learners into browsing learners', () => {
  const t = buildTimeline(params('steady', { legCount: 1, legIndex: 0 }));
  const plan = planLeg(t, params('steady'), 10, 100);
  assert.equal(plan.counts.room, 10);
  assert.equal(plan.pairedRooms, 10);
  assert.equal(plan.counts.learner, 850 + 40);
});

test('describeTimeline is plain JSON', () => {
  const d = describeTimeline(buildTimeline(params('overload')));
  assert.equal(JSON.parse(JSON.stringify(d)).profile, 'overload');
  assert.ok(d.phases.some((phase) => phase.name === 'overload'));
});
