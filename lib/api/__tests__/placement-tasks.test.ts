import { afterEach, describe, expect, it, vi } from 'vitest';

import {
  fetchPlacementSpeakingTasks,
  fetchPlacementWritingTasks,
  speakingPromptMaxPlays,
  speakingTaskExpectsAudio,
} from '@/lib/api/placement';

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

describe('speaking prompt audio wire mapping', () => {
  it('reads the engine audio object and the candidate-safety fields', async () => {
    stubJson([
      {
        task_id: 'SPK-B1B2-SR',
        task_type: 'sentence_reconstruction',
        route: 'B1-B2',
        prompt: 'Listen, then repeat the sentence.',
        candidate_sees_text: false,
        delivery: 'audio_only_repeat',
        audio: { storagePath: '/api/media/audio/SPK-B1B2-SR.mp3', durationSec: 4.2 },
        prepSeconds: 0,
        maxSpeakSeconds: 20,
      },
    ]);

    const [task] = await fetchPlacementSpeakingTasks('ses_1');

    expect(task).toMatchObject({
      candidateSeesText: false,
      delivery: 'audio_only_repeat',
      audio: { storagePath: '/api/media/audio/SPK-B1B2-SR.mp3', durationSec: 4.2 },
    });
  });

  it('still accepts snake_case spellings of the audio object', async () => {
    stubJson([
      {
        task_id: 'SPK-1',
        task_type: 'retell_summarise',
        route: 'A2-B1',
        prompt: 'Tell me what you remember.',
        candidateSeesText: true,
        delivery: 'audio_then_speak',
        audio: { storage_path: '/api/media/audio/SPK-1.mp3', duration_sec: 9 },
      },
    ]);

    const [task] = await fetchPlacementSpeakingTasks('ses_1');

    expect(task.candidateSeesText).toBe(true);
    expect(task.audio).toEqual({ storagePath: '/api/media/audio/SPK-1.mp3', durationSec: 9 });
  });

  it('maps a missing, null or empty audio to null - never a phantom clip', async () => {
    stubJson([
      { task_id: 'A', task_type: 'oral_reading', prompt: 'p' },
      { task_id: 'B', task_type: 'oral_reading', prompt: 'p', audio: null },
      { task_id: 'C', task_type: 'oral_reading', prompt: 'p', audio: {} },
      { task_id: 'D', task_type: 'oral_reading', prompt: 'p', audio: { storagePath: '', durationSec: 3 } },
    ]);

    const tasks = await fetchPlacementSpeakingTasks('ses_1');

    expect(tasks.map((task) => task.audio)).toEqual([null, null, null, null]);
    expect(tasks[0].candidateSeesText).toBeUndefined();
    expect(tasks[0].delivery).toBe('');
  });
});

describe('speaking prompt audio rules', () => {
  it('lets sentence reconstruction play once and every other audio task twice', () => {
    expect(speakingPromptMaxPlays('sentence_reconstruction')).toBe(1);
    for (const taskType of ['retell_summarise', 'simulated_interaction', 'functional_situation', 'extended_response']) {
      expect(speakingPromptMaxPlays(taskType)).toBe(2);
    }
  });

  it('expects audio when the candidate cannot read the task or delivery is audio-first', () => {
    expect(speakingTaskExpectsAudio({ candidateSeesText: false, delivery: 'audio_only_repeat' })).toBe(true);
    expect(speakingTaskExpectsAudio({ candidateSeesText: true, delivery: 'audio_then_speak' })).toBe(true);
    expect(speakingTaskExpectsAudio({ candidateSeesText: false, delivery: '' })).toBe(true);
    // Simulated interaction: the candidate also sees text, but must hear the interlocutor.
    expect(speakingTaskExpectsAudio({ candidateSeesText: true, delivery: 'interlocutor_audio_then_speak' })).toBe(true);
  });

  it('does not expect audio for text-delivered tasks', () => {
    expect(speakingTaskExpectsAudio({ candidateSeesText: true, delivery: 'text_read_aloud' })).toBe(false);
    expect(speakingTaskExpectsAudio({ candidateSeesText: true, delivery: 'text_prompt' })).toBe(false);
    // An older engine sends neither field: nothing is assumed to be audio.
    expect(speakingTaskExpectsAudio({ candidateSeesText: undefined, delivery: '' })).toBe(false);
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
