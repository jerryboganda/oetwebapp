import { afterEach, describe, expect, it, vi } from 'vitest';

import { fetchPlacementSpeakingTasks, fetchPlacementWritingTasks } from '@/lib/api/placement';

/**
 * The GEPA engine serializes SpeakingTask / WritingTask directly with serde,
 * so identity fields stay snake_case (task_id, task_type) while the timing
 * fields are renamed camelCase (prepSeconds, maxSpeakSeconds,
 * timeLimitSeconds) and word guidance is a nested { min, max } object.
 * The client once read prep_seconds / speaking_seconds / min_words / minutes,
 * which never exist on that wire — every Speaking task lost its planning
 * window and per-task response cap, and Writing lost its time and word hints.
 */

function stubJson(payload: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue(
      new Response(JSON.stringify(payload), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    ),
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('speaking task wire mapping', () => {
  it('reads the engine camelCase planning and response windows', async () => {
    stubJson([
      { task_id: 'SPK-B1B2-ER1', task_type: 'extended_response', route: 'B1-B2', prompt: 'Tell me about…', prepSeconds: 30, maxSpeakSeconds: 90 },
    ]);

    const [task] = await fetchPlacementSpeakingTasks('ses_1');

    expect(task).toMatchObject({ taskId: 'SPK-B1B2-ER1', taskType: 'extended_response', prepSeconds: 30, speakingSeconds: 90 });
  });

  it('leaves the windows undefined (not zero) when the engine sends none', async () => {
    stubJson([{ task_id: 'SPK-1', task_type: 'oral_reading', route: 'PreA1-A1', prompt: 'Read this.' }]);

    const [task] = await fetchPlacementSpeakingTasks('ses_1');

    expect(task.prepSeconds).toBeUndefined();
    expect(task.speakingSeconds).toBeUndefined();
  });

  it('still accepts the snake_case spellings', async () => {
    stubJson([{ task_id: 'SPK-2', task_type: 'functional', route: 'A2-B1', prompt: 'p', prep_seconds: 15, max_speak_seconds: 30 }]);

    const [task] = await fetchPlacementSpeakingTasks('ses_1');

    expect(task).toMatchObject({ prepSeconds: 15, speakingSeconds: 30 });
  });
});

describe('writing task wire mapping', () => {
  it('converts timeLimitSeconds to minutes and reads the nested word guidance', async () => {
    stubJson([
      { task_id: 'WRT-B1B2-1', task_type: 'functional', route: 'B1-B2', prompt: 'Write an email.', timeLimitSeconds: 420, word_guidance: { min: 40, max: 70 } },
    ]);

    const [task] = await fetchPlacementWritingTasks('ses_1');

    expect(task).toMatchObject({ taskId: 'WRT-B1B2-1', minutes: 7, minWords: 40 });
  });

  it('never shows a 0-minute hint for a very short limit', async () => {
    stubJson([{ task_id: 'WRT-1', task_type: 'diagnostic', route: 'PreA1-A1', prompt: 'p', timeLimitSeconds: 20 }]);

    const [task] = await fetchPlacementWritingTasks('ses_1');

    expect(task.minutes).toBe(1);
    expect(task.minWords).toBeUndefined();
  });
});
