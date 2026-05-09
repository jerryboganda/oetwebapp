import { expect, type APIRequestContext } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { closeSync, mkdirSync, openSync, unlinkSync, writeFileSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import type { SeededRole } from './auth';
import { bootstrapSessionForRole } from './auth-bootstrap';

const apiBaseURL = (process.env.NEXT_PUBLIC_API_BASE_URL ?? 'http://localhost:5198').replace(/\/$/, '');

type AuthSessionResponse = {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  refreshTokenExpiresAt: string;
  currentUser: {
    userId: string;
    email: string;
    role: string;
    displayName: string;
  };
};

type ReviewRequestResponse = {
  reviewRequestId: string;
};

type AdminCreditAdjustmentResponse = {
  userId: string;
  creditBalance: number;
};

function sleep(milliseconds: number) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

const LOCK_DIR = join(process.cwd(), 'playwright', '.locks');
const LOCK_TTL_MS = 60_000;

/**
 * Cross-process exclusive lock backed by an O_EXCL lockfile. We use this to
 * serialize the disposable-review create+claim flow across the parallel
 * Playwright projects (chromium-expert/firefox-expert/webkit-expert) that
 * all write to the same seeded learner wallet and the same shared seeded
 * attempts (wa-001 / sa-001). Without this, parallel SaveChanges calls on
 * the wallet row produce EF "An error occurred while saving the entity
 * changes" 500s that the inner backoff cannot reliably untangle.
 *
 * Only one fixture call holds the lock at a time, so workload is small.
 * Stale lockfiles older than LOCK_TTL_MS are reaped to recover from a worker
 * that crashed without releasing.
 */
async function acquireLock(name: string): Promise<() => void> {
  try {
    mkdirSync(LOCK_DIR, { recursive: true });
  } catch {
    // Directory may already exist or be racing with another worker.
  }

  const lockPath = join(LOCK_DIR, `${name}.lock`);
  const startTime = Date.now();

  for (;;) {
    try {
      const fd = openSync(lockPath, 'wx');
      try {
        writeFileSync(fd, String(process.pid));
      } finally {
        closeSync(fd);
      }
      return () => {
        try {
          unlinkSync(lockPath);
        } catch {
          // Already removed.
        }
      };
    } catch (error) {
      const code = (error as NodeJS.ErrnoException).code;
      if (code !== 'EEXIST') {
        throw error;
      }

      try {
        const stat = statSync(lockPath);
        if (Date.now() - stat.mtimeMs > LOCK_TTL_MS) {
          try {
            unlinkSync(lockPath);
          } catch {
            // Another worker may have just released.
          }
          continue;
        }
      } catch {
        // Lockfile vanished between EEXIST and stat — retry immediately.
        continue;
      }

      if (Date.now() - startTime > 120_000) {
        throw new Error(`Timed out waiting for fixture lock ${name}.`);
      }

      await sleep(100 + Math.floor(Math.random() * 150));
    }
  }
}

// Reference unused readers to silence TS unused-import warnings if they fire
// on partial tooling configurations. The functions are re-exported for
// potential future use.
void readFileSync;

function isOptimisticConcurrencyFailure(status: number, body: string) {
  if (status !== 500) {
    return false;
  }

  // EF Core surfaces three flavors of the wallet-row contention we hit when
  // multiple expert smoke tests deduct credits from the same seeded learner
  // in parallel: the explicit "expected to affect 1 row(s)" optimistic
  // concurrency message, an "optimistic concurrency" prefix, and the more
  // generic "An error occurred while saving the entity changes" wrapper that
  // hides the inner exception in production-mode error handlers. Treat all
  // three as retryable so the inner backoff loop can let the colliding
  // worker re-read state and try again.
  return /expected to affect 1 row\(s\)|optimistic concurrency|saving the entity changes/i.test(body);
}

async function readResponseBody(response: Awaited<ReturnType<APIRequestContext['get']>>) {
  try {
    return await response.text();
  } catch {
    return '<no response body>';
  }
}

async function expectOkResponse(
  response: Awaited<ReturnType<APIRequestContext['get']>>,
  message: string,
) {
  if (response.ok()) {
    return;
  }

  const body = await readResponseBody(response);
  expect(response.ok(), `${message}\nStatus: ${response.status()}\nBody: ${body}`).toBeTruthy();
}

export async function signInApi(request: APIRequestContext, role: SeededRole) {
  return bootstrapSessionForRole(request, role) as Promise<AuthSessionResponse>;
}

export async function authHeadersForRole(request: APIRequestContext, role: SeededRole) {
  const session = await signInApi(request, role);
  return {
    Authorization: `Bearer ${session.accessToken}`,
    'Content-Type': 'application/json',
  };
}

export async function fetchAdminUserDetailApi(request: APIRequestContext, userId: string) {
  const headers = await authHeadersForRole(request, 'admin');
  const response = await request.get(`${apiBaseURL}/v1/admin/users/${encodeURIComponent(userId)}`, { headers });

  await expectOkResponse(response, `Expected admin user detail fetch to succeed for ${userId}`);
  return response.json();
}

export async function ensureLearnerCredits(
  request: APIRequestContext,
  minimumBalance = 6,
  userId = 'mock-user-001',
) {
  const headers = await authHeadersForRole(request, 'admin');

  for (let attempt = 0; attempt < 3; attempt += 1) {
    const current = await fetchAdminUserDetailApi(request, userId) as { creditBalance?: number };
    const currentBalance = current.creditBalance ?? 0;

    if (currentBalance >= minimumBalance) {
      return currentBalance;
    }

    const response = await request.post(`${apiBaseURL}/v1/admin/users/${encodeURIComponent(userId)}/credits`, {
      headers,
      data: {
        amount: minimumBalance - currentBalance,
        reason: 'QA Playwright deep-flow credit top-up',
      },
    });

    if (response.ok()) {
      const updated = await response.json() as AdminCreditAdjustmentResponse;
      return updated.creditBalance;
    }

    const body = await readResponseBody(response);
    if (attempt < 2 && isOptimisticConcurrencyFailure(response.status(), body)) {
      await sleep(250 * (attempt + 1));
      continue;
    }

    expect(
      response.ok(),
      `Expected learner credit top-up to succeed for ${userId}\nStatus: ${response.status()}\nBody: ${body}`,
    ).toBeTruthy();
  }

  const final = await fetchAdminUserDetailApi(request, userId) as { creditBalance?: number };
  return final.creditBalance ?? 0;
}

async function findActiveLearnerReviewRequestId(
  request: APIRequestContext,
  learnerHeaders: Record<string, string>,
  attemptId: string,
  subtest: 'writing' | 'speaking',
): Promise<string | null> {
  const response = await request.get(`${apiBaseURL}/v1/reviews/`, { headers: learnerHeaders });
  if (!response.ok()) {
    return null;
  }
  const payload = await response.json() as {
    items?: Array<{ reviewRequestId: string; attemptId: string; subtest: string; state: string }>;
  };
  const inactiveStates = new Set(['completed', 'failed', 'cancelled']);
  const match = (payload.items ?? []).find(
    (item) =>
      item.attemptId === attemptId
      && item.subtest === subtest
      && !inactiveStates.has((item.state ?? '').toLowerCase()),
  );
  return match?.reviewRequestId ?? null;
}

async function cancelReviewRequestAsAdmin(
  request: APIRequestContext,
  reviewRequestId: string,
) {
  const adminHeaders = await authHeadersForRole(request, 'admin');
  await request.post(`${apiBaseURL}/v1/admin/review-ops/${encodeURIComponent(reviewRequestId)}/cancel`, {
    headers: adminHeaders,
    data: { reason: 'QA Playwright disposable review reset' },
  });
}

// When a fixture reuses (or recreates) a shared seeded review across runs, the
// expert draft from the previous test run can persist on the server. The
// review-completion specs assert empty initial state (e.g. clicking Submit
// triggers "please complete all rubric scores" + "please provide a final
// overall comment" toasts). Reset the draft to empty so each run starts clean.
async function resetExpertReviewDraft(
  request: APIRequestContext,
  expertHeaders: Record<string, string>,
  reviewRequestId: string,
) {
  try {
    await request.put(`${apiBaseURL}/v1/expert/reviews/${encodeURIComponent(reviewRequestId)}/draft`, {
      headers: expertHeaders,
      data: {
        scores: {},
        criterionComments: {},
        finalComment: '',
        anchoredComments: [],
        timestampComments: [],
        scratchpad: '',
        checklistItems: [],
        version: null,
      },
    });
  } catch {
    // Non-fatal: if reset fails the test will surface its own assertion error.
  }
}

function cancelActiveReviewsViaPsql(attemptId: string, subtest: 'writing' | 'speaking') {
  const psqlPath = process.env.OET_PSQL_PATH ?? 'C:\\Program Files\\PostgreSQL\\17\\bin\\psql.exe';
  const sql = `UPDATE "ReviewRequests" SET "State" = 7, "CompletedAt" = NOW() WHERE "AttemptId" = '${attemptId.replace(/'/g, "''")}' AND "SubtestCode" = '${subtest}' AND "State" NOT IN (5, 6, 7);`;
  try {
    execFileSync(
      psqlPath,
      ['-h', 'localhost', '-U', 'postgres', '-d', 'oet_learner_dev', '-c', sql],
      { env: { ...process.env, PGPASSWORD: 'postgres' }, stdio: 'pipe' },
    );
  } catch {
    // Falls back to admin API path; failure here is non-fatal.
  }
}

async function createClaimedExpertReviewRequest(
  request: APIRequestContext,
  options: {
    attemptId: string;
    subtest: 'writing' | 'speaking';
    focusAreas: string[];
    learnerNotes: string;
  },
) {
  // Cross-process serialization: see acquireLock for rationale. The lock is
  // keyed on the seeded learner because all expert-review fixtures share the
  // same wallet (mock-user-001), and the wallet row contention is the actual
  // source of EF SaveChanges conflicts when writing+speaking variants run
  // concurrently. Different learners (future fixtures) would use their own
  // lock and remain parallel.
  const releaseLock = await acquireLock(`expert-review-mock-user-001`);
  try {
    return await createClaimedExpertReviewRequestLocked(request, options);
  } finally {
    releaseLock();
  }
}

async function createClaimedExpertReviewRequestLocked(
  request: APIRequestContext,
  options: {
    attemptId: string;
    subtest: 'writing' | 'speaking';
    focusAreas: string[];
    learnerNotes: string;
  },
) {
  // Reuse-or-create strategy (option C from the operational ticket):
  //
  // The smoke matrix runs chromium/firefox/webkit expert projects in parallel,
  // and each project's detail-smoke spec calls this helper against the same
  // shared seeded attempts (wa-001 / sa-001). Creating a brand-new review
  // every time produces a 409 review_already_active race once the first worker
  // wins. Cancelling-then-recreating mid-run lets workers cancel each other's
  // live reviews and breaks the slower worker's test.
  //
  // Instead: if an active review already exists for this attempt+subtest, we
  // (re-)claim it as the seeded expert and reuse the same reviewRequestId.
  // The expert claim endpoint is idempotent for the currently-assigned
  // reviewer, so all parallel workers converge on the same review without
  // mutating DB state more than once. Only when no active review exists do we
  // fall back to the original create-then-claim path.
  const learnerHeaders = await authHeadersForRole(request, 'learner');

  const existingActiveReviewId = await findActiveLearnerReviewRequestId(
    request,
    learnerHeaders,
    options.attemptId,
    options.subtest,
  );
  if (existingActiveReviewId) {
    const expertHeadersForReuse = await authHeadersForRole(request, 'expert');
    const reuseClaimResponse = await request.post(
      `${apiBaseURL}/v1/expert/queue/${encodeURIComponent(existingActiveReviewId)}/claim`,
      { headers: expertHeadersForReuse },
    );
    await expectOkResponse(
      reuseClaimResponse,
      `Expected expert to (re-)claim existing active ${options.subtest} review request ${existingActiveReviewId}`,
    );
    await resetExpertReviewDraft(request, expertHeadersForReuse, existingActiveReviewId);
    return {
      reviewRequestId: existingActiveReviewId,
      attemptId: options.attemptId,
    };
  }

  await ensureLearnerCredits(request);
  // Preemptively clear any leftover non-active review noise so a stale row
  // (e.g. Submitted from a crashed prior run) cannot trip the create call.
  cancelActiveReviewsViaPsql(options.attemptId, options.subtest);

  let review: ReviewRequestResponse | null = null;
  let lastFailureBody = '<no response body>';
  let lastFailureStatus = 0;

  for (let outerAttempt = 0; outerAttempt < 2; outerAttempt += 1) {
    let reviewResponse: Awaited<ReturnType<APIRequestContext['post']>> | null = null;

    for (let attempt = 0; attempt < 3; attempt += 1) {
      reviewResponse = await request.post(`${apiBaseURL}/v1/reviews/requests`, {
        headers: learnerHeaders,
        data: {
          attemptId: options.attemptId,
          subtest: options.subtest,
          turnaroundOption: 'standard',
          focusAreas: options.focusAreas,
          learnerNotes: options.learnerNotes,
          paymentSource: 'credits',
          idempotencyKey: `qa-review-request-${options.subtest}-${Date.now()}-${outerAttempt}-${attempt}`,
        },
      });

      if (reviewResponse.ok()) {
        break;
      }

      lastFailureBody = await readResponseBody(reviewResponse);
      lastFailureStatus = reviewResponse.status();
      if (attempt < 2 && isOptimisticConcurrencyFailure(reviewResponse.status(), lastFailureBody)) {
        await sleep(250 * (attempt + 1));
        continue;
      }

      break;
    }

    if (reviewResponse?.ok()) {
      review = await reviewResponse.json() as ReviewRequestResponse;
      break;
    }

    if (
      outerAttempt === 0
      && lastFailureStatus === 409
      && /"code"\s*:\s*"review_already_active"/.test(lastFailureBody)
    ) {
      // Lost the create race against another parallel worker on the same
      // shared seeded attempt. The winning worker now owns an active review
      // for this attempt+subtest — do NOT cancel it (that would yank it out
      // from under the winner's still-running test). Instead, look it up
      // and reuse it: the expert claim endpoint is idempotent for the
      // currently-assigned reviewer, so both workers can converge on the
      // same review id without mutating DB state further.
      const existingId = await findActiveLearnerReviewRequestId(
        request,
        learnerHeaders,
        options.attemptId,
        options.subtest,
      );
      if (existingId) {
        const expertHeadersForReuse = await authHeadersForRole(request, 'expert');
        const reuseClaimResponse = await request.post(
          `${apiBaseURL}/v1/expert/queue/${encodeURIComponent(existingId)}/claim`,
          { headers: expertHeadersForReuse },
        );
        await expectOkResponse(
          reuseClaimResponse,
          `Expected expert to (re-)claim raced ${options.subtest} review request ${existingId}`,
        );
        await resetExpertReviewDraft(request, expertHeadersForReuse, existingId);
        return {
          reviewRequestId: existingId,
          attemptId: options.attemptId,
        };
      }
      continue;
    }

    break;
  }

  expect(
    review,
    `Expected disposable ${options.subtest} review request creation to succeed\nStatus: ${lastFailureStatus}\nBody: ${lastFailureBody}`,
  ).toBeTruthy();

  const expertHeaders = await authHeadersForRole(request, 'expert');
  const claimResponse = await request.post(`${apiBaseURL}/v1/expert/queue/${encodeURIComponent(review!.reviewRequestId)}/claim`, {
    headers: expertHeaders,
  });

  await expectOkResponse(claimResponse, `Expected expert claim for disposable ${options.subtest} review request to succeed`);

  await resetExpertReviewDraft(request, expertHeaders, review!.reviewRequestId);

  return {
    reviewRequestId: review!.reviewRequestId,
    attemptId: options.attemptId,
  };
}

export async function createDisposableWritingReviewRequest(request: APIRequestContext) {
  return createClaimedExpertReviewRequest(request, {
    attemptId: 'wa-001',
    subtest: 'writing',
    focusAreas: ['content', 'language'],
    learnerNotes: 'QA disposable expert writing review request created for automated expert workflow coverage.',
  });
}

export async function createDisposableSpeakingReviewRequest(request: APIRequestContext) {
  return createClaimedExpertReviewRequest(request, {
    attemptId: 'sa-001',
    subtest: 'speaking',
    focusAreas: ['fluency', 'relationshipBuilding'],
    learnerNotes: 'QA disposable expert speaking review request created for automated expert workflow coverage.',
  });
}
