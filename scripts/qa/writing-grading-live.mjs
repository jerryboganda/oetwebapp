import assert from 'node:assert/strict';

const base = 'https://api.oetwithdrhesham.co.uk';

async function request(path, token, method = 'GET', body) {
  const response = await fetch(`${base}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      'X-OET-Client-Platform': 'web',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    signal: AbortSignal.timeout(30000),
  });
  const result = await response.json().catch(() => ({}));
  if (!response.ok) {
    const code = result.errorCode ?? result.code ?? result.error?.code ?? 'request_failed';
    throw new Error(`${method} ${path}: HTTP ${response.status} (${code})`);
  }
  return result;
}

async function signIn(email, password) {
  assert.ok(email && password, 'CI credentials are required');
  const session = await request('/v1/auth/sign-in', null, 'POST', { email, password, rememberMe: false });
  assert.ok(session.accessToken, 'An authenticated session is required');
  return session.accessToken;
}

const admin = await signIn(process.env.ADMIN_EMAIL, process.env.ADMIN_PASSWORD);
const learner = await signIn(process.env.QA_EMAIL, process.env.QA_PASSWORD);
const profile = await request('/v1/writing/v2/profile', learner);
assert.ok(profile.userId && profile.profession, 'The QA learner needs an existing Writing profile');
const operations = await request('/v1/admin/ai/operations?featureCode=writing.grade&pageSize=200', admin);
const candidates = [...new Set(operations.rows
  .filter(row => row.userId === profile.userId && row.state === 'FailedTerminal')
  .map(row => row.resourceId))].slice(0, 5);
let submission;
for (const id of candidates) {
  const candidate = await request(`/v1/writing/submissions/${encodeURIComponent(id)}`, learner);
  if (candidate.status === 'failed' && candidate.mode !== 'mock') {
    submission = candidate;
    break;
  }
}
const scenario = submission ? null : await request(`/v1/writing/scenarios/random?profession=${encodeURIComponent(profile.profession)}`, learner);
assert.ok(submission || scenario?.id, 'An eligible QA scenario is required');
const usagePath = `/v1/admin/ai/usage?featureCode=writing.grade&userId=${encodeURIComponent(profile.userId)}&pageSize=100`;
const before = await request(usagePath, admin);
const previousUsage = new Set(before.rows.map(row => row.id));
const previous = await request('/v1/admin/ai/writing-provider', admin);
assert.ok(['auto', 'claude', 'codex'].includes(previous.mode), 'The original provider mode must be known');

try {
  await request('/v1/admin/ai/writing-provider', admin, 'PUT', { mode: 'codex' });
  const selected = await request('/v1/admin/ai/writing-provider', admin);
  assert.equal(selected.currentPrimary.provider, 'writing-codex-sub');
  if (submission) {
    const resumed = await request(`/v1/writing/submissions/${submission.id}/retry-grade`, learner, 'POST', {});
    assert.equal(resumed.id, submission.id);
    submission = resumed;
    console.log('QA_RESUMED_SAME_SUBMISSION', submission.id);
  } else {
    const letter = 'Dear Doctor,\n\nI am writing to request your assessment and ongoing management of this patient. Please review the clinical information in the supplied case notes and advise on appropriate follow-up. Thank you for your assistance.\n\nYours faithfully,\nDoctor';
    submission = await request('/v1/writing/submissions', learner, 'POST', {
      scenarioId: scenario.id, mode: 'practice', letterContent: letter,
      wordCount: letter.trim().split(/\s+/).length, timeSpentSeconds: 120,
      inputSource: 'typed', simulationMode: null,
      idempotencyKey: `writing-incident-qa-${process.env.GITHUB_RUN_ID}`,
    });
    console.log('QA_CREATED_SUBMISSION', submission.id);
  }

  const deadline = Date.now() + 8 * 60 * 1000;
  while (submission.status !== 'graded') {
    assert.notEqual(submission.status, 'failed', 'The live grading worker failed');
    assert.ok(Date.now() < deadline, 'The live grading deadline was exceeded');
    await new Promise(resolve => setTimeout(resolve, 5000));
    submission = await request(`/v1/writing/submissions/${submission.id}`, learner);
  }
  const grade = await request(`/v1/writing/submissions/${submission.id}/grade`, learner);
  const report = await request(`/v1/writing/submissions/${submission.id}/assessment-v11`, learner);
  assert.equal(grade.submissionId, submission.id);
  assert.equal(report.submissionId, submission.id);
  assert.equal(Object.keys(grade.perCriterion).length, 6);
  assert.equal(report.criteria.length, 6);
  assert.equal(report.candidateReportVisible, true);
  assert.ok(Number.isFinite(grade.estimatedBand));
  const after = await request(usagePath, admin);
  const newUsage = after.rows.filter(row => !previousUsage.has(row.id));
  assert.ok(newUsage.some(row => row.providerId === 'writing-codex-sub' && row.outcome === 'Success'));
  assert.ok(newUsage.every(row => row.providerId === 'writing-codex-sub'), 'A non-subscription provider was called');

  const replay = await request(`/v1/writing/submissions/${submission.id}/retry-grade`, learner, 'POST', {});
  assert.equal(replay.id, submission.id);
  assert.equal(replay.status, 'graded');
  const replayGrade = await request(`/v1/writing/submissions/${submission.id}/grade`, learner);
  assert.equal(replayGrade.id, grade.id);
  const replayUsage = await request(usagePath, admin);
  assert.deepEqual(replayUsage.rows.map(row => row.id), after.rows.map(row => row.id));
  console.log('QA_GRADED_REPORT_AND_IDEMPOTENT_RETRY', JSON.stringify({
    submissionId: submission.id, gradeId: grade.id, reportId: report.id,
    criteria: report.criteria.length, provider: 'writing-codex-sub',
  }));
} finally {
  const current = await request('/v1/admin/ai/writing-provider', admin);
  if (current.mode === 'codex') {
    await request('/v1/admin/ai/writing-provider', admin, 'PUT', { mode: previous.mode });
    const restored = await request('/v1/admin/ai/writing-provider', admin);
    assert.equal(restored.mode, previous.mode);
    console.log('QA_PROVIDER_MODE_RESTORED', restored.mode);
  } else {
    console.log('QA_PROVIDER_MODE_CHANGED_EXTERNALLY_NOT_OVERWRITTEN', current.mode);
  }
}