import assert from 'node:assert/strict';

const base = 'https://api.oetwithdrhesham.co.uk';

async function request(path, token, method = 'GET', body) {
  const response = await fetch(`${base}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      'X-OET-Client-Platform': 'web',
      'X-OET-Device-Id': '4c8726d2-b537-4dea-9d49-daf0a2fb80e1',
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
const submissionId = process.env.TARGET_SUBMISSION_ID;
assert.match(submissionId ?? '', /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i);
const source = await request(`/v1/writing/submissions/${submissionId}`, learner);
assert.ok(source.userId && source.scenarioId, 'An owned persisted source submission is required');
assert.ok(source.letterContent.trim(), 'A nonblank original letter is required');
assert.notEqual(source.mode, 'mock', 'Mock Writing must remain human-marked');
let submission;
const usagePath = `/v1/admin/ai/usage?featureCode=writing.grade&userId=${encodeURIComponent(source.userId)}&pageSize=100`;
const before = await request(usagePath, admin);
const previousUsage = new Set(before.rows.map(row => row.id));
const previous = await request('/v1/admin/ai/writing-provider', admin);
assert.ok(['auto', 'claude', 'codex'].includes(previous.mode), 'The original provider mode must be known');

try {
  await request('/v1/admin/ai/writing-provider', admin, 'PUT', { mode: 'codex' });
  const selected = await request('/v1/admin/ai/writing-provider', admin);
  assert.equal(selected.mode, 'codex');
  submission = await request('/v1/writing/submissions', learner, 'POST', {
    scenarioId: source.scenarioId, mode: 'practice', letterContent: source.letterContent,
    wordCount: source.wordCount, timeSpentSeconds: source.timeSpentSeconds,
    inputSource: 'typed', simulationMode: null,
    idempotencyKey: `writing-incident-qa-${process.env.GITHUB_RUN_ID}`,
  });
  assert.notEqual(submission.id, source.id, 'QA must leave the original submission untouched');
  console.log('QA_CREATED_SUBMISSION', submission.id);

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