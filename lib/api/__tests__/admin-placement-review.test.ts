import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('@/lib/auth-client', () => ({
  ensureFreshAccessToken: vi.fn().mockResolvedValue('test-token'),
}));

import { fetchPlacementReviewQueue, fetchPlacementReviewSession } from '../admin-placement';

/**
 * Wire contract for the placement review console (engine snake_case, proxied
 * unchanged by the OET API): queue rows carry the task they flag, and a review
 * session names its rubric traits and every submitted task of the session.
 */

const fetchMock = vi.fn();

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('fetchPlacementReviewQueue', () => {
  it('maps the task each queue row is about, or null from an older engine', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse([
        { session_id: 's1', flag_type: 'pending_review', module: 'SPK', details: 'd', timestamp: 't', task_id: 'SPK-B1B2-ER2' },
        { session_id: 's2', flag_type: 'pending_review', module: 'WRT', details: 'd', timestamp: 't' },
      ]),
    );

    const rows = await fetchPlacementReviewQueue();

    expect(rows.map((row) => row.taskId)).toEqual(['SPK-B1B2-ER2', null]);
    expect(rows[0]).toMatchObject({ sessionId: 's1', flagType: 'pending_review', module: 'SPK' });
  });
});

describe('fetchPlacementReviewSession', () => {
  const wire = {
    session_id: 's1',
    task_id: 'SPK-B1B2-ER2',
    module: 'SPK',
    task_type: 'extended_response',
    task_prompt: 'Tell me about your work.',
    candidate_audio_url: '/api/review/sessions/s1/audio/SPK-B1B2-ER2',
    candidate_draft: null,
    transcript: 'I work in a clinic.',
    at_lower: {},
    at_upper: {},
    ai_rationale: null,
    rating_id: 'rat-9',
    rater: 'pending',
    flags: ['pending_review'],
    traits: ['intelligibility', 'fluency', 'grammar', 'vocabulary', 'communication'],
    tasks: [
      { task_id: 'SPK-B1B2-ER1', module: 'SPK', task_type: 'extended_response', status: 'human_scored', has_recording: true },
      { task_id: 'SPK-B1B2-ER2', module: 'SPK', task_type: 'extended_response', status: 'pending_review', has_recording: false },
    ],
  };

  it('asks for the picked task and maps traits, rating and the session task list', async () => {
    fetchMock.mockResolvedValue(jsonResponse(wire));

    const session = await fetchPlacementReviewSession('s1', 'SPK-B1B2-ER2');

    expect(String(fetchMock.mock.calls[0][0])).toContain('/v1/admin/placement/review/s1?taskId=SPK-B1B2-ER2');
    expect(session).toMatchObject({
      sessionId: 's1',
      taskId: 'SPK-B1B2-ER2',
      module: 'SPK',
      taskType: 'extended_response',
      ratingId: 'rat-9',
      rater: 'pending',
      traits: ['intelligibility', 'fluency', 'grammar', 'vocabulary', 'communication'],
    });
    expect(session.tasks).toEqual([
      { taskId: 'SPK-B1B2-ER1', module: 'SPK', taskType: 'extended_response', status: 'human_scored', hasRecording: true },
      { taskId: 'SPK-B1B2-ER2', module: 'SPK', taskType: 'extended_response', status: 'pending_review', hasRecording: false },
    ]);
  });

  it('sends no task filter when none is picked, leaving the engine to choose', async () => {
    fetchMock.mockResolvedValue(jsonResponse(wire));

    await fetchPlacementReviewSession('s1');

    const url = String(fetchMock.mock.calls[0][0]);
    expect(url).toContain('/v1/admin/placement/review/s1');
    expect(url).not.toContain('taskId');
  });

  it('tolerates an older engine that sends no traits, tasks or rating fields', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ task_id: 'WRT-B1B2-1', flags: [] }));

    const session = await fetchPlacementReviewSession('s1');

    expect(session).toMatchObject({ taskId: 'WRT-B1B2-1', module: '', taskType: '', ratingId: '', rater: '', traits: [], tasks: [] });
  });
});
