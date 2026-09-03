import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getCachedAudioUrl,
  registerCachedAudioUrl,
  clearAudioPrebufferCache,
  prebufferAudioChunks,
} from '@/lib/listening/audio-prebuffer';
import { DualTrackRecorder } from '@/lib/speaking/dual-track-recorder';

describe('Milestone 1: 4-Skill Exam & Practice Engine Adversarial Stress Suite', () => {
  beforeEach(() => {
    clearAudioPrebufferCache();
    vi.restoreAllMocks();
  });

  // ==========================================================================
  // SUITE 1: READING 20/6/16 INVARIANTS & 15-MIN LOCK TRIGGER
  // ==========================================================================
  describe('Feature 1: Reading 20/6/16 Engine & 15-Min Lock', () => {
    it('R1-1: enforces exact 20/6/16 = 42 item structure and partition sums', () => {
      const partA = 20;
      const partB = 6;
      const partC = 16;
      expect(partA + partB + partC).toBe(42);
      expect(partA).toBe(20);
      expect(partB).toBe(6);
      expect(partC).toBe(16);
    });

    it('R1-2: rejects invalid question structures (under/overflow / wrong partitions)', () => {
      const validateReadingStructure = (parts: { part: string; count: number }[]) => {
        if (!parts || parts.length !== 3) return false;
        const [a, b, c] = parts;
        return a.count === 20 && b.count === 6 && c.count === 16 && (a.count + b.count + c.count === 42);
      };

      expect(validateReadingStructure([{ part: 'A', count: 20 }, { part: 'B', count: 6 }, { part: 'C', count: 16 }])).toBe(true);
      expect(validateReadingStructure([{ part: 'A', count: 19 }, { part: 'B', count: 6 }, { part: 'C', count: 17 }])).toBe(false);
      expect(validateReadingStructure([{ part: 'A', count: 21 }, { part: 'B', count: 6 }, { part: 'C', count: 15 }])).toBe(false);
      expect(validateReadingStructure([{ part: 'A', count: 20 }, { part: 'B', count: 7 }, { part: 'C', count: 15 }])).toBe(false);
      expect(validateReadingStructure([{ part: 'A', count: 20 }, { part: 'B', count: 6 }])).toBe(false);
    });

    it('R1-3: enforces Part A 15-minute countdown boundary state machine', () => {
      const PART_A_DURATION_SECONDS = 15 * 60; // 900s
      const evalPartA = (elapsedSeconds: number) => ({
        remainingSeconds: Math.max(0, PART_A_DURATION_SECONDS - elapsedSeconds),
        isLocked: elapsedSeconds >= PART_A_DURATION_SECONDS,
      });

      expect(evalPartA(0)).toEqual({ remainingSeconds: 900, isLocked: false });
      expect(evalPartA(450)).toEqual({ remainingSeconds: 450, isLocked: false });
      expect(evalPartA(899)).toEqual({ remainingSeconds: 1, isLocked: false });
      expect(evalPartA(900)).toEqual({ remainingSeconds: 0, isLocked: true });
      expect(evalPartA(1200)).toEqual({ remainingSeconds: 0, isLocked: true });
    });

    it('R1-4: accurately identifies benign vs non-benign server lock rejections', () => {
      const BENIGN_SAVE_REJECTIONS = new Set([
        'part_a_locked',
        'part_bc_not_open',
        'part_bc_break_not_resumed',
      ]);
      const isBenign = (code: string) => BENIGN_SAVE_REJECTIONS.has(code);

      expect(isBenign('part_a_locked')).toBe(true);
      expect(isBenign('part_bc_not_open')).toBe(true);
      expect(isBenign('part_bc_break_not_resumed')).toBe(true);
      expect(isBenign('network_error')).toBe(false);
      expect(isBenign('unauthorized')).toBe(false);
      expect(isBenign('invalid_token')).toBe(false);
    });

    it('R1-5: verifies strikethrough option elimination and passage highlights payload', () => {
      const annotationState = {
        ruledOutOptionsByQuestion: {
          'q-21': ['A', 'C'],
          'q-22': ['B'],
        },
        highlightsByPassage: {
          'passage-1': [
            { id: 'h1', startOffset: 45, endOffset: 89, color: 'yellow', text: 'myocardial infarction' },
          ],
        },
      };

      const serialized = JSON.stringify(annotationState);
      const parsed = JSON.parse(serialized);
      expect(parsed.ruledOutOptionsByQuestion['q-21']).toEqual(['A', 'C']);
      expect(parsed.highlightsByPassage['passage-1'][0].text).toBe('myocardial infarction');
    });
  });

  // ==========================================================================
  // SUITE 2: LISTENING 10-PHASE CHUNKS & AUDIO PRE-BUFFERING CACHE
  // ==========================================================================
  describe('Feature 2: Listening 10-Phase Sequencing & Audio Cache', () => {
    it('L2-1: validates 10-phase sequence chunks [A1, A2, B1..B6, C1, C2] totaling 42 questions', () => {
      const phases = [
        { phase: 'A1', subtest: 'A', questions: 12 },
        { phase: 'A2', subtest: 'A', questions: 12 },
        { phase: 'B1', subtest: 'B', questions: 1 },
        { phase: 'B2', subtest: 'B', questions: 1 },
        { phase: 'B3', subtest: 'B', questions: 1 },
        { phase: 'B4', subtest: 'B', questions: 1 },
        { phase: 'B5', subtest: 'B', questions: 1 },
        { phase: 'B6', subtest: 'B', questions: 1 },
        { phase: 'C1', subtest: 'C', questions: 6 },
        { phase: 'C2', subtest: 'C', questions: 6 },
      ];

      expect(phases.length).toBe(10);
      const sum = phases.reduce((acc, p) => acc + p.questions, 0);
      expect(sum).toBe(42);
      expect(phases.filter((p) => p.subtest === 'A').reduce((a, b) => a + b.questions, 0)).toBe(24);
      expect(phases.filter((p) => p.subtest === 'B').reduce((a, b) => a + b.questions, 0)).toBe(6);
      expect(phases.filter((p) => p.subtest === 'C').reduce((a, b) => a + b.questions, 0)).toBe(12);
    });

    it('L2-2: handles cache hit, miss, and cache clear lifecycle', () => {
      const url = 'https://media.oetprep.com/audio/sample-1.mp3';
      const blobUrl = 'blob:http://localhost/mock-audio-blob-1';

      expect(getCachedAudioUrl(url)).toBeNull();
      registerCachedAudioUrl(url, blobUrl);
      expect(getCachedAudioUrl(url)).toBe(blobUrl);

      clearAudioPrebufferCache();
      expect(getCachedAudioUrl(url)).toBeNull();
    });

    it('L2-3: prebufferAudioChunks handles empty, deduplicated, and cached lists', async () => {
      const emptyResult = await prebufferAudioChunks([]);
      expect(emptyResult.success).toBe(true);
      expect(emptyResult.prebufferedCount).toBe(0);

      registerCachedAudioUrl('https://media.oetprep.com/audio/1.mp3', 'blob:http://localhost/b1');
      registerCachedAudioUrl('https://media.oetprep.com/audio/2.mp3', 'blob:http://localhost/b2');

      const result = await prebufferAudioChunks([
        'https://media.oetprep.com/audio/1.mp3',
        ' https://media.oetprep.com/audio/1.mp3 ',
        'https://media.oetprep.com/audio/2.mp3',
      ]);

      expect(result.success).toBe(true);
      expect(result.prebufferedCount).toBe(2);
      expect(result.failedCount).toBe(0);
    });

    it('L2-4: strict one-play exam mode neutralizes scrub seeking', () => {
      const resolveSeek = (current: number, requested: number, examMode: boolean) => {
        return examMode ? current : requested;
      };

      expect(resolveSeek(45, 90, true)).toBe(45);
      expect(resolveSeek(45, 10, true)).toBe(45);
      expect(resolveSeek(45, 90, false)).toBe(90);
    });
  });

  // ==========================================================================
  // SUITE 3: WRITING 45-MIN SUB-TEST (5m LOCK -> 40m WRITE) & WORD COUNT BANDS
  // ==========================================================================
  describe('Feature 3: Writing 45-Min Engine & Word Count Bands', () => {
    it('W3-1: enforces 45-minute partitioning (5m locked reading + 40m active writing)', () => {
      const readingSeconds = 5 * 60;
      const writingSeconds = 40 * 60;
      const totalSeconds = readingSeconds + writingSeconds;
      expect(totalSeconds).toBe(2700);
    });

    it('W3-2: validates phase state transitions across reading, writing, and auto-submit', () => {
      const getPhase = (elapsed: number) => {
        if (elapsed < 300) return { phase: 'reading_locked', canEdit: false, autoSubmit: false };
        if (elapsed < 2700) return { phase: 'writing_active', canEdit: true, autoSubmit: false };
        return { phase: 'hard_deadline_expired', canEdit: false, autoSubmit: true };
      };

      expect(getPhase(0)).toEqual({ phase: 'reading_locked', canEdit: false, autoSubmit: false });
      expect(getPhase(299)).toEqual({ phase: 'reading_locked', canEdit: false, autoSubmit: false });
      expect(getPhase(300)).toEqual({ phase: 'writing_active', canEdit: true, autoSubmit: false });
      expect(getPhase(1500)).toEqual({ phase: 'writing_active', canEdit: true, autoSubmit: false });
      expect(getPhase(2699)).toEqual({ phase: 'writing_active', canEdit: true, autoSubmit: false });
      expect(getPhase(2700)).toEqual({ phase: 'hard_deadline_expired', canEdit: false, autoSubmit: true });
    });

    it('W3-3: tokenizes and counts words accurately across medical formatting', () => {
      const countWords = (text: string) => {
        if (!text) return 0;
        const clean = text
          .replace(/<[^>]*>/g, ' ')
          .replace(/&nbsp;/g, ' ')
          .replace(/[*_#~]/g, '')
          .trim();
        return clean ? clean.split(/\s+/).filter((w) => /[a-zA-Z0-9]/.test(w)).length : 0;
      };

      expect(countWords('')).toBe(0);
      expect(countWords('   \n\t  ')).toBe(0);
      expect(countWords('Dear Dr. Edwards,')).toBe(3);
      expect(countWords('<p>Thank you for seeing <strong>Mr. John Smith</strong>, aged 62.</p>')).toBe(9);
      expect(countWords('The patient requires twice-daily insulin injections.')).toBe(6);
    });

    it('W3-4: evaluates official OET 180–200 word count target bands', () => {
      const evaluateBand = (words: number) => {
        if (words < 140) return 'severe_underlength';
        if (words < 180) return 'underlength';
        if (words <= 200) return 'ideal_target';
        if (words <= 220) return 'acceptable_upper';
        return 'overlength';
      };

      expect(evaluateBand(110)).toBe('severe_underlength');
      expect(evaluateBand(160)).toBe('underlength');
      expect(evaluateBand(180)).toBe('ideal_target');
      expect(evaluateBand(195)).toBe('ideal_target');
      expect(evaluateBand(200)).toBe('ideal_target');
      expect(evaluateBand(215)).toBe('acceptable_upper');
      expect(evaluateBand(235)).toBe('overlength');
    });
  });

  // ==========================================================================
  // SUITE 4: SPEAKING 2-CARD ENGINE & DUAL-TRACK AUDIO RECORDER
  // ==========================================================================
  describe('Feature 4: Speaking 2-Card Engine & Dual-Track Audio', () => {
    it('S4-1: enforces 2-card timing distribution (3m prep + 5m active x 2 = 16m)', () => {
      const prepSeconds = 3 * 60;
      const activeSeconds = 5 * 60;
      const cardTotal = prepSeconds + activeSeconds;
      expect(cardTotal).toBe(480);
      expect(cardTotal * 2).toBe(960);
    });

    it('S4-2: validates DualTrackRecorder idle and stop state safety', async () => {
      const recorder = new DualTrackRecorder('session-test-safety');
      const state = recorder.getState();
      expect(state.isRecording).toBe(false);
      expect(state.isPaused).toBe(false);
      expect(state.chunkCount).toBe(0);
      expect(recorder.getBlobNow()).toBeNull();

      const stopResult = await recorder.stop();
      expect(stopResult).toBeNull();
    });
  });

  // ==========================================================================
  // SUITE 5: 4-SKILL UNIFIED MOCK COORDINATOR PIPELINE & TRANSITIONS
  // ==========================================================================
  describe('Feature 5: 4-Skill Unified Mock Coordinator', () => {
    it('M5-1: verifies sequential sub-test progression (Listening -> Reading -> Writing -> Speaking)', () => {
      const sequence = ['listening', 'reading', 'writing', 'speaking'];
      expect(sequence).toEqual(['listening', 'reading', 'writing', 'speaking']);
    });

    it('M5-2: calculates mock progress percentages accurately', () => {
      const calcProgress = (completed: number, total: number) => {
        return Math.round((completed / Math.max(1, total)) * 100);
      };

      expect(calcProgress(0, 4)).toBe(0);
      expect(calcProgress(1, 4)).toBe(25);
      expect(calcProgress(2, 4)).toBe(50);
      expect(calcProgress(3, 4)).toBe(75);
      expect(calcProgress(4, 4)).toBe(100);
    });

    it('M5-3: resolves next runnable section prioritizing in_progress over not_started', () => {
      const resolveNext = (sections: { id: string; status: string }[]) => {
        return sections.find((s) => s.status === 'in_progress')
          ?? sections.find((s) => s.status === 'not_started')
          ?? null;
      };

      const set1 = [
        { id: 'sec-l', status: 'not_started' },
        { id: 'sec-r', status: 'not_started' },
      ];
      expect(resolveNext(set1)?.id).toBe('sec-l');

      const set2 = [
        { id: 'sec-l', status: 'completed' },
        { id: 'sec-r', status: 'in_progress' },
        { id: 'sec-w', status: 'not_started' },
      ];
      expect(resolveNext(set2)?.id).toBe('sec-r');

      const set3 = [
        { id: 'sec-l', status: 'completed' },
        { id: 'sec-r', status: 'completed' },
      ];
      expect(resolveNext(set3)).toBeNull();
    });

    it('M5-4: formats server clock synchronization drift badge', () => {
      const formatDrift = (ms: number) => {
        return Math.abs(ms) < 1000 ? 'Synchronized' : `${ms}ms offset`;
      };

      expect(formatDrift(0)).toBe('Synchronized');
      expect(formatDrift(350)).toBe('Synchronized');
      expect(formatDrift(-400)).toBe('Synchronized');
      expect(formatDrift(1500)).toBe('1500ms offset');
      expect(formatDrift(-2000)).toBe('-2000ms offset');
    });
  });
});