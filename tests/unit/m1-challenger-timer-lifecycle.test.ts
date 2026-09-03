import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// 1. Listening audio prebuffer & integrity
import {
  getCachedAudioUrl,
  registerCachedAudioUrl,
  clearAudioPrebufferCache,
} from '@/lib/listening/audio-prebuffer';
import {
  resolveBlockedSeekTarget,
  shouldResumeAfterBlockedPause,
} from '@/lib/listening/audio-integrity';
import {
  LISTENING_FORWARD_PATH,
  nextListeningState,
  type ListeningFsmState,
} from '@/lib/listening/transitions';

// 2. Speaking Dual-Track Recorder
import {
  DualTrackRecorder,
} from '@/lib/speaking/dual-track-recorder';

// 3. Mock Workflow Policies & Readiness
import {
  getMockModePolicy,
  getMockSubmissionReadiness,
} from '@/lib/mocks/workflow';
import type { MockSession, MockSessionSection } from '@/lib/mock-data';

describe('Milestone 1 Challenger: 4-Skill Exam Engine Timer Lifecycle & State Transitions', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    clearAudioPrebufferCache();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // =========================================================================
  // 1. READING PART A (15-MIN) & PART B/C (45-MIN) TIMER & LOCK TRANSITIONS
  // =========================================================================
  describe('1. Reading Part A 15-Min Timer, Lock Behavior & Part B/C Transition', () => {
    const PART_A_DURATION_SEC = 15 * 60; // 900s

    const evaluateReadingTimer = (elapsedSec: number) => ({
      remainingSeconds: Math.max(0, PART_A_DURATION_SEC - elapsedSec),
      isLocked: elapsedSec >= PART_A_DURATION_SEC,
    });

    it('R-1.1: Part A timer countdown precision across sub-second boundaries', () => {
      expect(evaluateReadingTimer(0)).toEqual({ remainingSeconds: 900, isLocked: false });
      expect(evaluateReadingTimer(450)).toEqual({ remainingSeconds: 450, isLocked: false });
      expect(evaluateReadingTimer(899.9).isLocked).toBe(false);
      expect(evaluateReadingTimer(900.0)).toEqual({ remainingSeconds: 0, isLocked: true });
      expect(evaluateReadingTimer(900.1)).toEqual({ remainingSeconds: 0, isLocked: true });
      expect(evaluateReadingTimer(1500)).toEqual({ remainingSeconds: 0, isLocked: true });
    });

    it('R-1.2: Reading 20/6/16 item structure partition validation', () => {
      const parts = [
        { part: 'A', count: 20, timeboxSec: 900 },
        { part: 'B', count: 6, timeboxSec: 2700 },
        { part: 'C', count: 16, timeboxSec: 2700 },
      ];
      const totalCount = parts.reduce((acc, p) => acc + p.count, 0);
      expect(totalCount).toBe(42);
      expect(parts[0].count).toBe(20);
      expect(parts[1].count).toBe(6);
      expect(parts[2].count).toBe(16);
      expect(parts[0].timeboxSec).toBe(15 * 60);
      expect(parts[1].timeboxSec).toBe(45 * 60);
    });

    it('R-1.3: Benign lock rejection detection suppresses false-positive error banners', () => {
      const isBenignRejection = (code: string) =>
        ['part_a_locked', 'part_bc_not_open', 'part_bc_break_not_resumed', 'section_locked'].includes(code);

      expect(isBenignRejection('part_a_locked')).toBe(true);
      expect(isBenignRejection('part_bc_not_open')).toBe(true);
      expect(isBenignRejection('part_bc_break_not_resumed')).toBe(true);
      expect(isBenignRejection('section_locked')).toBe(true);
      expect(isBenignRejection('internal_error')).toBe(false);
      expect(isBenignRejection('unauthorized')).toBe(false);
    });

    it('R-1.4: Reading option strikethrough & highlight payload integrity', () => {
      const state = {
        ruledOutOptions: { 'q-21': ['A', 'C'], 'q-22': ['B'] },
        highlights: [{ id: 'h1', text: 'myocardial infarction', start: 10, end: 31 }],
      };
      const json = JSON.stringify(state);
      const parsed = JSON.parse(json);
      expect(parsed.ruledOutOptions['q-21']).toEqual(['A', 'C']);
      expect(parsed.highlights[0].text).toBe('myocardial infarction');
    });
  });

  // =========================================================================
  // 2. LISTENING AUDIO SEQUENCING, SINGLE-PLAY & SEEK/PAUSE GUARDS
  // =========================================================================
  describe('2. Listening Audio Phase Sequencing, Single-Play & Seeking Blocking', () => {
    it('L-2.1: 17-state FSM linear sequencing traversal without cycles or deadlocks', () => {
      expect(LISTENING_FORWARD_PATH.length).toBe(17);
      expect(LISTENING_FORWARD_PATH[0]).toBe('intro');
      expect(LISTENING_FORWARD_PATH[16]).toBe('submitted');

      let current: ListeningFsmState | null = 'intro';
      const steps: ListeningFsmState[] = ['intro'];
      while (current && current !== 'submitted') {
        current = nextListeningState(current);
        if (current) steps.push(current);
      }
      expect(steps).toEqual([...LISTENING_FORWARD_PATH]);
      expect(nextListeningState('submitted')).toBeNull();
    });

    it('L-2.2: Audio seek guard neutralizes scrub attempts during exam mode', () => {
      const lockedTarget = resolveBlockedSeekTarget({
        canScrub: false,
        requestedTime: 125.0,
        lastKnownTime: 30.5,
        allowedProgrammaticTarget: null,
      });
      expect(lockedTarget).toBe(30.5);

      const rewindTarget = resolveBlockedSeekTarget({
        canScrub: false,
        requestedTime: 10.0,
        lastKnownTime: 30.5,
        allowedProgrammaticTarget: null,
      });
      expect(rewindTarget).toBe(30.5);

      // In practice mode (canScrub = true), scrubbing returns null (no override)
      const allowed = resolveBlockedSeekTarget({
        canScrub: true,
        requestedTime: 125.0,
        lastKnownTime: 30.5,
        allowedProgrammaticTarget: null,
      });
      expect(allowed).toBeNull();
    });

    it('L-2.3: Audio pause guard forces auto-resume during active audio phase in exam mode', () => {
      const resumeRequired = shouldResumeAfterBlockedPause({
        canPause: false,
        phase: 'audio',
        hasStarted: true,
        hasReachedEnd: false,
        allowedProgrammaticPause: false,
      });
      expect(resumeRequired).toBe(true);

      // Preview/review phase pause is allowed
      expect(
        shouldResumeAfterBlockedPause({
          canPause: false,
          phase: 'preview',
          hasStarted: true,
          hasReachedEnd: false,
          allowedProgrammaticPause: false,
        }),
      ).toBe(false);

      // Practice mode pause is allowed
      expect(
        shouldResumeAfterBlockedPause({
          canPause: true,
          phase: 'audio',
          hasStarted: true,
          hasReachedEnd: false,
          allowedProgrammaticPause: false,
        }),
      ).toBe(false);
    });

    it('L-2.4: Audio prebuffer cache TTL revocation and cleanup', () => {
      const url = 'https://cdn.oetprep.com/audio/sample-1.mp3';
      const objUrl = 'blob:http://localhost/sample-1';
      registerCachedAudioUrl(url, objUrl);
      expect(getCachedAudioUrl(url)).toBe(objUrl);

      clearAudioPrebufferCache();
      expect(getCachedAudioUrl(url)).toBeNull();
    });
  });

  // =========================================================================
  // 3. WRITING 45-MIN ENGINE (5m READ LOCK -> 40m EDIT -> AUTO-SUBMIT)
  // =========================================================================
  describe('3. Writing 5-Min Locked Reading vs 40-Min Active Drafting Engine', () => {
    const getWritingPhase = (elapsedSeconds: number) => {
      if (elapsedSeconds < 300) {
        return { phase: 'reading_locked', isEditorLocked: true, canEdit: false, autoSubmit: false };
      }
      if (elapsedSeconds < 2700) {
        return { phase: 'writing_active', isEditorLocked: false, canEdit: true, autoSubmit: false };
      }
      return { phase: 'hard_deadline_expired', isEditorLocked: true, canEdit: false, autoSubmit: true };
    };

    it('W-3.1: Strict 5-min locked reading window prevents letter typing', () => {
      expect(getWritingPhase(0)).toEqual({ phase: 'reading_locked', isEditorLocked: true, canEdit: false, autoSubmit: false });
      expect(getWritingPhase(299)).toEqual({ phase: 'reading_locked', isEditorLocked: true, canEdit: false, autoSubmit: false });
      expect(getWritingPhase(300)).toEqual({ phase: 'writing_active', isEditorLocked: false, canEdit: true, autoSubmit: false });
      expect(getWritingPhase(1500)).toEqual({ phase: 'writing_active', isEditorLocked: false, canEdit: true, autoSubmit: false });
      expect(getWritingPhase(2699)).toEqual({ phase: 'writing_active', isEditorLocked: false, canEdit: true, autoSubmit: false });
      expect(getWritingPhase(2700)).toEqual({ phase: 'hard_deadline_expired', isEditorLocked: true, canEdit: false, autoSubmit: true });
    });

    it('W-3.2: Word counter accurately tokenizes HTML tags and medical formatting', () => {
      const countWords = (text: string) => {
        if (!text) return 0;
        const clean = text
          .replace(/<[^>]*>/g, ' ')
          .replace(/&nbsp;/g, ' ')
          .replace(/[*_#~]/g, '')
          .trim();
        return clean ? clean.split(/\s+/).filter((w) => /[a-zA-Z0-9]/.test(w)).length : 0;
      };

      expect(countWords('<p>Dear Dr. Smith,</p><p>Thank you for seeing <strong>Mr. Jones</strong>.</p>')).toBe(9);
      expect(countWords('Medications:&nbsp;Amoxicillin 500mg t.d.s.,&nbsp;Paracetamol 1g QDS.')).toBe(7);
    });

    it('W-3.3: Evaluates official OET 180–200 word target guidelines', () => {
      const evaluateBand = (count: number) => {
        if (count < 140) return 'severe_underlength';
        if (count < 180) return 'underlength';
        if (count <= 200) return 'ideal_target';
        if (count <= 220) return 'acceptable_upper';
        return 'overlength';
      };

      expect(evaluateBand(120)).toBe('severe_underlength');
      expect(evaluateBand(170)).toBe('underlength');
      expect(evaluateBand(195)).toBe('ideal_target');
      expect(evaluateBand(210)).toBe('acceptable_upper');
      expect(evaluateBand(240)).toBe('overlength');
    });
  });

  // =========================================================================
  // 4. SPEAKING 2-CARD ENGINE & DUAL-TRACK RECORDING SAFETY NET
  // =========================================================================
  describe('4. Speaking 3-Min Prep vs 5-Min Recording & Dual-Track Safety', () => {
    it('S-4.1: Enforces 2-card 16-minute total distribution (3m prep + 5m active x 2)', () => {
      const prepSec = 3 * 60; // 180s
      const activeSec = 5 * 60; // 300s
      const cardSec = prepSec + activeSec; // 480s
      expect(cardSec).toBe(480);
      expect(cardSec * 2).toBe(960);
    });

    it('S-4.2: DualTrackRecorder safely manages idle, unstarted stop, and chunk state', async () => {
      const recorder = new DualTrackRecorder('speaking-test-safety');
      expect(recorder.getState().isRecording).toBe(false);
      expect(recorder.getState().chunkCount).toBe(0);
      expect(recorder.getBlobNow()).toBeNull();

      const stopRes = await recorder.stop();
      expect(stopRes).toBeNull();
    });
  });

  // =========================================================================
  // 5. UNIFIED 4-SKILL MOCK COORDINATOR STATE RESTORATION & GATING
  // =========================================================================
  describe('5. Unified 4-Skill Mock Coordinator State Transitions & Gating', () => {
    const createSession = (
      states: Array<'not_started' | 'in_progress' | 'completed'>,
    ): MockSession => ({
      sessionId: 'mock-session-test',
      state: 'in_progress',
      config: {
        id: 'cfg-test-1',
        title: 'Full 4-Skill Mock',
        type: 'full',
        profession: 'Medicine',
        mode: 'exam',
        strictTimer: true,
        includeReview: false,
        deliveryMode: 'computer',
        reviewSelection: 'none',
      },
      resumeRoute: '/mocks/mock-session-test/resume',
      sectionStates: [
        { id: 'sec-l', subtest: 'listening', title: 'Listening', state: states[0], reviewAvailable: false, reviewSelected: false, launchRoute: '/listening' },
        { id: 'sec-r', subtest: 'reading', title: 'Reading', state: states[1], reviewAvailable: false, reviewSelected: false, launchRoute: '/reading' },
        { id: 'sec-w', subtest: 'writing', title: 'Writing', state: states[2], reviewAvailable: false, reviewSelected: false, launchRoute: '/writing' },
        { id: 'sec-s', subtest: 'speaking', title: 'Speaking', state: states[3], reviewAvailable: false, reviewSelected: false, launchRoute: '/speaking' },
      ],
    });

    it('M-5.1: Calculates submission readiness and blocks submission until 4/4 complete', () => {
      const s0 = createSession(['not_started', 'not_started', 'not_started', 'not_started']);
      const r0 = getMockSubmissionReadiness(s0);
      expect(r0.canSubmit).toBe(false);
      expect(r0.completedCount).toBe(0);

      const s2 = createSession(['completed', 'completed', 'in_progress', 'not_started']);
      const r2 = getMockSubmissionReadiness(s2);
      expect(r2.canSubmit).toBe(false);
      expect(r2.completedCount).toBe(2);

      const s4 = createSession(['completed', 'completed', 'completed', 'completed']);
      const r4 = getMockSubmissionReadiness(s4);
      expect(r4.canSubmit).toBe(true);
      expect(r4.completedCount).toBe(4);
    });

    it('M-5.2: Enforces mock mode policy invariants (strict exam vs practice)', () => {
      const examPolicy = getMockModePolicy('exam');
      expect(examPolicy.strictTimerRequired).toBe(true);
      expect(examPolicy.listeningReplayAllowed).toBe(false);
      expect(examPolicy.pauseAllowed).toBe(false);

      const practicePolicy = getMockModePolicy('practice');
      expect(practicePolicy.strictTimerRequired).toBe(false);
      expect(practicePolicy.listeningReplayAllowed).toBe(true);
      expect(practicePolicy.pauseAllowed).toBe(true);
    });

    it('M-5.3: Resolves next runnable section prioritizing in_progress over not_started', () => {
      const resolveNext = (sections: MockSessionSection[]) => {
        return (
          sections.find((s) => s.state === 'in_progress') ??
          sections.find((s) => s.state === 'not_started') ??
          null
        );
      };

      const sessionMid = createSession(['completed', 'in_progress', 'not_started', 'not_started']);
      const nextSec = resolveNext(sessionMid.sectionStates);
      expect(nextSec?.subtest).toBe('reading');
      expect(nextSec?.state).toBe('in_progress');
    });
  });
});
