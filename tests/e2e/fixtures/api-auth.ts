import { expect, type APIRequestContext } from '@playwright/test';
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

type WritingAttemptResponse = {
  attemptId: string;
  draftVersion?: number;
};

type WritingSubmitResponse = {
  evaluationId: string;
};

type ReviewRequestResponse = {
  reviewRequestId: string;
};

type AdminCreditAdjustmentResponse = {
  userId: string;
  creditBalance: number;
};

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
  const current = await fetchAdminUserDetailApi(request, userId) as { creditBalance?: number };
  const currentBalance = current.creditBalance ?? 0;

  if (currentBalance >= minimumBalance) {
    return currentBalance;
  }

  const headers = await authHeadersForRole(request, 'admin');
  const response = await request.post(`${apiBaseURL}/v1/admin/users/${encodeURIComponent(userId)}/credits`, {
    headers,
    data: {
      amount: minimumBalance - currentBalance,
      reason: 'QA Playwright deep-flow credit top-up',
    },
  });

  await expectOkResponse(response, `Expected learner credit top-up to succeed for ${userId}`);
  const updated = await response.json() as AdminCreditAdjustmentResponse;
  return updated.creditBalance;
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
  await ensureLearnerCredits(request);
  const learnerHeaders = await authHeadersForRole(request, 'learner');
  const reviewResponse = await request.post(`${apiBaseURL}/v1/reviews/requests`, {
    headers: learnerHeaders,
    data: {
      attemptId: options.attemptId,
      subtest: options.subtest,
      turnaroundOption: 'standard',
      focusAreas: options.focusAreas,
      learnerNotes: options.learnerNotes,
      paymentSource: 'credits',
      idempotencyKey: `qa-review-request-${options.subtest}-${Date.now()}`,
    },
  });

  await expectOkResponse(reviewResponse, `Expected disposable ${options.subtest} review request creation to succeed`);
  const review = await reviewResponse.json() as ReviewRequestResponse;

  const expertHeaders = await authHeadersForRole(request, 'expert');
  const claimResponse = await request.post(`${apiBaseURL}/v1/expert/queue/${encodeURIComponent(review.reviewRequestId)}/claim`, {
    headers: expertHeaders,
  });

  await expectOkResponse(claimResponse, `Expected expert claim for disposable ${options.subtest} review request to succeed`);

  return {
    reviewRequestId: review.reviewRequestId,
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
    focusAreas: ['fluency', 'clinicalCommunication'],
    learnerNotes: 'QA disposable expert speaking review request created for automated expert workflow coverage.',
  });
}
