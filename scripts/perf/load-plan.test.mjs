import assert from 'node:assert/strict';
import test from 'node:test';
import { MAX_LEARNERS_PER_HOSTED_LEG, fromEnv, planRun, toOutputs } from './load-plan.mjs';

const base = {
  profile: 'steady', legs: '4', apiUrl: 'https://api.staging.example', webUrl: 'https://app.staging.example',
  confirmNonProduction: true,
};

test('the 1,000-learner steady run fits four hosted legs of 250', () => {
  const plan = planRun(base);
  assert.equal(plan.totalLearners, 1000);
  assert.equal(plan.learnersPerLeg, 250);
  assert.deepEqual(plan.matrix, [0, 1, 2, 3]);
  assert.deepEqual(plan.accounts, { learners: 1000, experts: 0 });
  assert.equal(plan.durationMinutes, 70); // 250 s ramp + 120 s settle + 60 min steady + 180 s cooldown, rounded up
  assert.deepEqual(plan.warnings, []);
});

test('the 1,500-learner overload run needs at least five legs and counts the surge', () => {
  assert.throws(() => planRun({ ...base, profile: 'overload', legs: '4' }), /at least 5 legs/);
  const plan = planRun({ ...base, profile: 'overload', legs: '6' });
  assert.equal(plan.totalLearners, 1500);
  assert.equal(plan.learnersPerLeg, 250);
  assert.equal(plan.accounts.learners, 1500);
});

test('one leg cannot drive 1,000 learners and the error says what to do', () => {
  assert.throws(() => planRun({ ...base, legs: '1' }), (error) => /3\d\d|1000 learners over 1 leg/.test(error.message) && /GitHub-hosted runner drives at most 300/.test(error.message));
  assert.throws(() => planRun({ ...base, legs: '2' }), /at least 4 legs/);
  assert.equal(MAX_LEARNERS_PER_HOSTED_LEG, 300);
});

test('allow_oversubscribe lets an oversized leg through with a warning', () => {
  const plan = planRun({ ...base, legs: '2', allowOversubscribe: 'true' });
  assert.equal(plan.learnersPerLeg, 500);
  assert.match(plan.warnings.join('\n'), /oversubscribed/);
});

test('production is refused in every field, and non-production must be confirmed', () => {
  assert.throws(() => planRun({ ...base, apiUrl: 'https://api.oetwithdrhesham.co.uk' }), /production host/);
  assert.throws(() => planRun({ ...base, webUrl: 'https://app.oetwithdrhesham.co.uk' }), /production host/);
  assert.throws(() => planRun({ ...base, apiUrl: 'https://185.252.233.186' }), /production host/);
  assert.throws(() => planRun({ ...base, confirmNonProduction: false }), /confirm_non_production/);
  assert.throws(() => planRun({ ...base, confirmNonProduction: undefined }), /confirm_non_production/);
});

test('https is required except loopback for the smoke profile', () => {
  assert.throws(() => planRun({ ...base, apiUrl: 'http://api.staging.example' }), /https/);
  assert.throws(() => planRun({ ...base, profile: 'steady', apiUrl: 'http://127.0.0.1:5198' }), /https/);
  const smoke = planRun({ profile: 'smoke', legs: '1', apiUrl: 'http://127.0.0.1:5198', confirmNonProduction: 'true' });
  assert.equal(smoke.apiUrl, 'http://127.0.0.1:5198');
  assert.equal(smoke.totalLearners, 20);
  assert.match(smoke.warnings.join('\n'), /web_url is empty/);
});

test('profile and leg validation', () => {
  assert.throws(() => planRun({ ...base, profile: 'nope' }), /profile must be one of/);
  assert.throws(() => planRun({ ...base, legs: '0' }), /legs must be/);
  assert.throws(() => planRun({ ...base, legs: '7' }), /legs must be/);
  assert.throws(() => planRun({ ...base, legs: 'x' }), /legs must be/);
  assert.throws(() => planRun({ ...base, profile: 'smoke', legs: '2' }), /smoke profile runs on one leg/);
  assert.throws(() => planRun({ ...base, apiUrl: '' }), /api_url is required/);
});

test('overrides: learners, steady minutes and paired rooms flow into the plan', () => {
  const plan = planRun({ ...base, learners: '400', steadyMinutes: '10', legs: '2', pairedRooms: '5' });
  assert.equal(plan.totalLearners, 400);
  assert.equal(plan.durationMinutes, Math.ceil((400 * 0.5 + 120 + 600 + 180) / 60));
  assert.equal(plan.accounts.experts, 5);
  // more paired rooms than room learners exist is capped
  assert.equal(planRun({ ...base, learners: '40', legs: '1', pairedRooms: '99' }).accounts.experts, 2);
});

test('outputs are single-line key=value pairs for GITHUB_OUTPUT', () => {
  const lines = toOutputs(planRun(base));
  assert.ok(lines.includes('legs=[0,1,2,3]'));
  assert.ok(lines.includes('accounts_learners=1000'));
  for (const line of lines) assert.ok(!line.includes('\n'));
});

test('fromEnv maps the INPUT_ variables the workflow passes', () => {
  const input = fromEnv({
    INPUT_PROFILE: 'steady', INPUT_LEGS: '4', INPUT_API_URL: 'https://a.test', INPUT_WEB_URL: 'https://w.test',
    INPUT_CONFIRM_NON_PRODUCTION: 'true', INPUT_LEARNERS: '', INPUT_PAIRED_ROOMS: '3',
  });
  assert.equal(input.profile, 'steady');
  assert.equal(input.pairedRooms, '3');
  assert.equal(planRun(input).accounts.experts, 3);
});
