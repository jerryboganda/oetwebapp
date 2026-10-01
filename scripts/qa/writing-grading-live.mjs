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

async function verifyProductionReport() {
  const { chromium } = await import('playwright');
  const app = 'https://app.oetwithdrhesham.co.uk';
  const reportSubmissionId = process.env.REPORT_SUBMISSION_ID;
  assert.match(reportSubmissionId ?? '', /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i);
  assert.ok(process.env.QA_EMAIL && process.env.QA_PASSWORD, 'Production learner credentials are required');
  for (const endpoint of [`${app}/api/health`, `${base}/health/ready`, `${base}/health/live`]) {
    const response = await fetch(endpoint, { signal: AbortSignal.timeout(30000) });
    assert.equal(response.status, 200, `Production health failed on ${new URL(endpoint).pathname}`);
  }
  const browser = await chromium.launch();
  try {
    const context = await browser.newContext({ locale: 'en-GB' });
    const blockedWrites = [];
    await context.addInitScript(({ origin, deviceId }) => {
      if (window.location.origin === origin) localStorage.setItem('oet_device_id', deviceId);
    }, { origin: app, deviceId: '4c8726d2-b537-4dea-9d49-daf0a2fb80e1' });
    await context.route('**/v1/writing/**', async route => {
      if (!['GET', 'HEAD', 'OPTIONS'].includes(route.request().method())) {
        blockedWrites.push(new URL(route.request().url()).pathname);
        await route.abort();
        return;
      }
      await route.continue();
    });
    const page = await context.newPage();
    page.setDefaultTimeout(30000);
    const authRequests = [];
    page.on('request', request => {
      if (request.method() === 'POST' && new URL(request.url()).pathname.includes('sign-in')) {
        authRequests.push({ path: new URL(request.url()).pathname, method: request.method() });
      }
    });
    await page.goto(`${app}/sign-in`, { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => {
      const formElement = document.querySelector('form');
      return formElement && Object.keys(formElement).some(propertyName =>
        propertyName.startsWith('__reactProps$') && typeof formElement[propertyName]?.onSubmit === 'function');
    });
    await page.locator('input[type="email"]').fill(process.env.QA_EMAIL);
    await page.locator('input[type="password"]').fill(process.env.QA_PASSWORD);
    const signInRead = page.waitForResponse(response =>
      new URL(response.url()).pathname.endsWith('/sign-in')
      && response.request().method() === 'POST');
    await page.locator('form button[type="submit"]').click();
    const signInResponse = await signInRead.catch(error => {
      console.log('QA_LIVE_BROWSER_AUTH_REQUESTS', JSON.stringify(authRequests));
      throw error;
    });
    const signInResult = await signInResponse.json().catch(() => ({}));
    const signInCode = signInResult.errorCode ?? signInResult.code ?? signInResult.error?.code ?? 'none';
    console.log('QA_LIVE_BROWSER_SIGN_IN', JSON.stringify({
      path: new URL(signInResponse.url()).pathname, status: signInResponse.status(), code: signInCode,
    }));
    assert.equal(signInResponse.status(), 200, `Production browser sign-in failed (${signInCode})`);
    assert.ok(signInResult.accessToken, 'Production browser sign-in requires a challenge before report access');
    await page.waitForURL(url => url.origin === app && !url.pathname.includes('/sign-in'), { waitUntil: 'domcontentloaded' });
    let savedGradeId;
    let savedReportId;
    for (const viewport of [{ width: 1366, height: 900 }, { width: 390, height: 844 }]) {
      await page.setViewportSize(viewport);
      const gradeRead = page.waitForResponse(response =>
        new URL(response.url()).pathname.endsWith(`/v1/writing/submissions/${reportSubmissionId}/grade`)
        && response.request().method() === 'GET');
      const reportRead = page.waitForResponse(response =>
        new URL(response.url()).pathname.endsWith(`/v1/writing/submissions/${reportSubmissionId}/assessment-v11`)
        && response.request().method() === 'GET');
      if (savedGradeId) {
        await page.reload({ waitUntil: 'domcontentloaded' });
      } else {
        await page.goto(`${app}/writing/submissions/${reportSubmissionId}/grading`, { waitUntil: 'domcontentloaded' });
      }
      await page.waitForURL(url => url.pathname === `/writing/submissions/${reportSubmissionId}/results`, { waitUntil: 'domcontentloaded' });
      const [gradeResponse, reportResponse] = await Promise.all([gradeRead, reportRead]);
      assert.equal(gradeResponse.status(), 200, 'Production grade read failed');
      assert.equal(reportResponse.status(), 200, 'Production assessment read failed');
      const grade = await gradeResponse.json();
      const report = await reportResponse.json();
      assert.equal(grade.submissionId, reportSubmissionId);
      assert.equal(report.submissionId, reportSubmissionId);
      assert.equal(report.status, 'CandidateReady');
      assert.equal(report.candidateReportVisible, true);
      assert.equal(report.candidateNumericScoreEnabled, true);
      assert.equal(report.criteria.length, 6);
      assert.equal(Object.keys(grade.perCriterion).length, 6);
      assert.ok(Number.isFinite(report.estimatedPracticeScore));
      if (savedGradeId) {
        assert.equal(grade.id, savedGradeId, 'Refresh changed the saved grade');
        assert.equal(report.id, savedReportId, 'Refresh changed the saved report');
      }
      savedGradeId = grade.id;
      savedReportId = report.id;
      await page.getByTestId('ai-estimated-score').waitFor({ state: 'visible' });
      assert.equal((await page.getByTestId('ai-estimated-score').textContent()).trim(), `${report.estimatedPracticeScore}/500`);
      await page.getByTestId('assessment-criteria-list').waitFor({ state: 'visible' });
      assert.equal(await page.getByTestId('criteria-list').locator(':scope > li').count(), 6);
      assert.equal(await page.getByTestId('assessment-criteria-list').locator(':scope > article').count(), 6);
      assert.equal(blockedWrites.length, 0, 'A Writing mutation was attempted during read-only QA');
      console.log('QA_LIVE_BROWSER_REPORT', JSON.stringify({
        submissionId: reportSubmissionId, gradeId: savedGradeId, reportId: savedReportId,
        criteria: 6, viewport, persistedAfterRefresh: viewport.width === 390, writingMutations: 0,
      }));
    }
  } finally {
    await browser.close();
  }
}

if (process.env.VERIFY_PRODUCTION_REPORT === 'true') {
  await verifyProductionReport();
  process.exit(0);
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
const retryId = process.env.RETRY_SUBMISSION_ID;
if (retryId) {
  assert.match(retryId, /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i);
  assert.notEqual(retryId, source.id, 'The original submission must not be retried by QA');
  submission = await request(`/v1/writing/submissions/${retryId}`, learner);
  assert.equal(submission.status, 'failed', 'Only the existing failed QA attempt may be resumed');
  assert.equal(submission.scenarioId, source.scenarioId);
  assert.equal(submission.contentHash, source.contentHash);
}
const usagePath = `/v1/admin/ai/usage?featureCode=writing.grade&userId=${encodeURIComponent(source.userId)}&pageSize=100`;
const before = await request(usagePath, admin);
const previousUsage = new Set(before.rows.map(row => row.id));
const previous = await request('/v1/admin/ai/writing-provider', admin);
assert.ok(['auto', 'claude', 'codex'].includes(previous.mode), 'The original provider mode must be known');

try {
  await request('/v1/admin/ai/writing-provider', admin, 'PUT', { mode: 'codex' });
  const selected = await request('/v1/admin/ai/writing-provider', admin);
  assert.equal(selected.mode, 'codex');
  if (retryId) {
    submission = await request(`/v1/writing/submissions/${retryId}/retry-grade`, learner, 'POST', {});
    assert.equal(submission.id, retryId);
    console.log('QA_RESUMED_SAME_SUBMISSION', submission.id);
  } else {
    submission = await request('/v1/writing/submissions', learner, 'POST', {
      scenarioId: source.scenarioId, mode: 'practice', letterContent: source.letterContent,
      wordCount: source.wordCount, timeSpentSeconds: source.timeSpentSeconds,
      inputSource: 'typed', simulationMode: null,
      idempotencyKey: `writing-incident-qa-${process.env.GITHUB_RUN_ID}`,
    });
    console.log('QA_CREATED_SUBMISSION', submission.id);
  }
  assert.notEqual(submission.id, source.id, 'QA must leave the original submission untouched');

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