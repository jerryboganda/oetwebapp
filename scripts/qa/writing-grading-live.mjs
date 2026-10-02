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
      // One concise per-criterion list (WAI-09 UI-3) replaced the v1.1 duplicate list.
      await page.getByTestId('criteria-list').waitFor({ state: 'visible' });
      assert.equal(await page.getByTestId('criteria-list').locator(':scope > li').count(), 6);
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

// RULE MAX-ALWAYS-ON (owner, 2 Oct 2026): the former "subscription-only" live QA flipped the Writing provider
// mode to Codex to grade a letter. Forcing any provider over the Claude Max route is forbidden and the admin API
// refuses it (400 max_subscription_always_on), so that flow is gone. Failover levels are proven by the fault-flag
// runs of writing-prod-qa.yml instead (Max is always attempted first).
console.error('This script only supports VERIFY_PRODUCTION_REPORT=true (the read-only report check).');
process.exit(1);
