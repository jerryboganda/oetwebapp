import { applyReviewGate, parseVerdict } from './reviewgate.js';
let f = 0;
const ok = (n, c, x) => { if (c) console.log('PASS: ' + n); else { f++; console.log('FAIL: ' + n + (x ? ' :: ' + x : '')); } };
const W = { kind: 'write' };
const R = { kind: 'read' };
const base = { role: 'implement', status: 'SUCCESS', summary: 'did it', filesChanged: ['a.js'], evidence: [], tests: [], blockers: [] };
let calls = 0;
const pass = async () => { calls++; return { summary: 'VERDICT: PASS' }; };
const fail = async () => { calls++; return { summary: 'VERDICT: FAIL: null deref on empty list' }; };
const boom = async () => { calls++; throw new Error('quota exhausted'); };
const off = { role: 'implement', workspace: 'X', reviewGate: { enabled: false } };
const on = { role: 'implement', workspace: 'X', reviewGate: { enabled: true } };
const open = { role: 'implement', workspace: 'X', reviewGate: { enabled: true, failClosed: false } };

(async () => {
    let r;
    r = await applyReviewGate(base, off, W, null, pass);
    ok('gate disabled returns the identical object (no copy)', r === base);
    ok('gate disabled never invokes the reviewer', calls === 0, 'calls=' + calls);

    calls = 0; r = await applyReviewGate(base, on, W, 'WT', pass);
    ok('PASS keeps status SUCCESS', r.status === 'SUCCESS', r.status);
    ok('PASS verdict is recorded', r.reviewVerdict && r.reviewVerdict.verdict === 'PASS');
    ok('reviewer invoked exactly once', calls === 1, 'calls=' + calls);
    ok('worktree path is carried into the verdict', r.reviewVerdict.worktreePath === 'WT');

    calls = 0; r = await applyReviewGate(base, on, W, null, fail);
    ok('FAIL with failClosed becomes BLOCKED', r.status === 'BLOCKED', r.status);
    ok('blocker REVIEW_GATE_FAILED present', (r.blockers || []).indexOf('REVIEW_GATE_FAILED') >= 0);
    ok('blocker quotes the reviewer reasoning', String(r.summary).indexOf('null deref') >= 0, r.summary);
    ok('exactly one invocation on FAIL (no recursion)', calls === 1, 'calls=' + calls);

    r = await applyReviewGate(base, open, W, null, fail);
    ok('FAIL with failClosed=false stays SUCCESS', r.status === 'SUCCESS', r.status);
    ok('FAIL still recorded when not failing closed', r.reviewVerdict.verdict === 'FAIL');

    r = await applyReviewGate(base, on, W, null, boom);
    ok('reviewer throw with failClosed becomes BLOCKED', r.status === 'BLOCKED', r.status);
    ok('blocker REVIEW_GATE_REVIEWER_FAILED present', (r.blockers || []).indexOf('REVIEW_GATE_REVIEWER_FAILED') >= 0);
    r = await applyReviewGate(base, open, W, null, boom);
    ok('reviewer throw with failClosed=false stays SUCCESS', r.status === 'SUCCESS', r.status);
    ok('throw is recorded as UNKNOWN', r.reviewVerdict.verdict === 'UNKNOWN');

    calls = 0; r = await applyReviewGate({ role: 'explore', status: 'SUCCESS' }, { role: 'explore', workspace: 'X', reviewGate: { enabled: true } }, R, null, pass);
    ok('read-kind role is never gated', calls === 0, 'calls=' + calls);
    ok('read-kind result untouched', r.role === 'explore');

    calls = 0; r = await applyReviewGate({ role: 'review', status: 'SUCCESS' }, { role: 'review', workspace: 'X', reviewGate: { enabled: true, reviewerRole: 'review' } }, W, null, pass);
    ok('reviewer role is never itself gated', calls === 0, 'calls=' + calls);

    calls = 0; r = await applyReviewGate({ role: 'implement', status: 'ERROR' }, on, W, null, pass);
    ok('non-SUCCESS result is not gated', calls === 0, 'calls=' + calls);

    ok('parseVerdict reads FAIL', parseVerdict('junk VERDICT: FAIL: because').verdict === 'FAIL');
    ok('parseVerdict reads PASS', parseVerdict('VERDICT: PASS').verdict === 'PASS');
    ok('parseVerdict returns UNKNOWN when absent', parseVerdict('nothing here').verdict === 'UNKNOWN');

    console.log(f === 0 ? '=== ALL REVIEW-GATE SELF-TESTS PASSED ===' : '=== ' + f + ' ASSERTION(S) FAILED ===');
    process.exit(f ? 1 : 0);
})();

