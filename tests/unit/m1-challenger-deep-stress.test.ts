import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// 1. Speaking Dual-Track Audio Recording
import {
  DualTrackRecorder,
  retrieveStoredRecording,
  clearStoredRecording,
  type DualTrackRecordingResult,
} from '@/lib/speaking/dual-track-recorder';

// 2. Listening Audio Pre-buffering & Integrity
import {
  getCachedAudioUrl,
  registerCachedAudioUrl,
  clearAudioPrebufferCache,
  prebufferAudioChunks,
  type AudioPrebufferProgress,
} from '@/lib/listening/audio-prebuffer';
import {
  resolveBlockedSeekTarget,
  shouldResumeAfterBlockedPause,
} from '@/lib/listening/audio-integrity';
import {
  LISTENING_FSM_STATES,
  LISTENING_FORWARD_PATH,
  nextListeningState,
  listeningPartFor,
  isAudioState,
  isReviewState,
  isPreviewState,
  listeningPositionForState,
  listeningStateForPosition,
  listeningWindowSeconds,
  type ListeningFsmState,
} from '@/lib/listening/transitions';

// 3. Offline Answer Reconciliation & Sync
import {
  reconcileOfflineAnswer,
  type OfflineAnswerPayload,
} from '@/lib/mobile/offline-answer-reconciliation';
import {
  queueOfflineAttempt,
  getPendingAttempts,
  markAttemptSynced,
  markAttemptConflict,
  setOfflineEncryptionKey,
  clearOfflineEncryptionKey,
  cacheContent,
  getCachedContent,
  clearExpiredContent,
  isOnline,
} from '@/lib/mobile/offline-sync';

describe('Milestone 1 Challenger 2: Deep Empirical Resilience & Audio Integrity Suite', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    clearAudioPrebufferCache();
    clearOfflineEncryptionKey();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // =========================================================================
  // 1. SPEAKING DUAL-TRACK AUDIO RECORDING FALLBACK & LOCAL STORAGE SAFETY
  // =========================================================================
  describe('1. Speaking Dual-Track Audio Fallback & Safety Net', () => {
    it('S-1.1: Multi-chunk continuous recording aggregates bytes and duration without data loss', async () => {
      const sessionId = 'stress-speaking-session-1';
      const recorder = new DualTrackRecorder(sessionId);

      // Mock MediaRecorder
      let capturedOnStop: (() => void) | null = null;
      let capturedOnData: ((e: { data: Blob }) => void) | null = null;

      class MockMediaRecorder {
        state = 'inactive';
        ondataavailable: ((e: { data: Blob }) => void) | null = null;
        onstop: (() => void) | null = null;

        constructor(_stream: MediaStream, _options?: unknown) {
          capturedOnStop = () => {
            if (this.onstop) this.onstop();
          };
          capturedOnData = (e) => {
            if (this.ondataavailable) this.ondataavailable(e);
          };
        }

        start(_timeslice?: number) {
          this.state = 'recording';
        }
        pause() {
          this.state = 'paused';
        }
        resume() {
          this.state = 'recording';
        }
        stop() {
          this.state = 'inactive';
          if (capturedOnStop) capturedOnStop();
        }
        static isTypeSupported(_mime: string) {
          return true;
        }
      }

      (global as unknown as { MediaRecorder: typeof MockMediaRecorder }).MediaRecorder =
        MockMediaRecorder;

      const fakeStream = {} as MediaStream;
      recorder.start(fakeStream);

      expect(recorder.getState().isRecording).toBe(true);

      // Stream 50 audio frames (each 1024 bytes)
      const chunkBytes = 1024;
      for (let i = 0; i < 50; i++) {
        const fakeChunk = new Blob([new Uint8Array(chunkBytes)], { type: 'audio/webm' });
        (capturedOnData as unknown as (e: { data: Blob }) => void)?.({ data: fakeChunk });
      }

      const interimBlob = recorder.getBlobNow();
      expect(interimBlob).not.toBeNull();
      expect(recorder.getState().chunkCount).toBe(50);
      expect(recorder.getState().totalBytes).toBe(50 * chunkBytes);

      const stopResult = await recorder.stop();
      expect(stopResult).not.toBeNull();
      expect(stopResult?.sizeBytes).toBe(50 * chunkBytes);
      expect(stopResult?.blob.size).toBe(50 * chunkBytes);
      expect(recorder.getState().isRecording).toBe(false);
    });

    it('S-1.2: Rapid pause/resume stress cycles maintain state consistency', () => {
      const sessionId = 'stress-speaking-pause-resume';
      const recorder = new DualTrackRecorder(sessionId);

      class MockMediaRecorder {
        state = 'inactive';
        ondataavailable: ((e: { data: Blob }) => void) | null = null;
        onstop: (() => void) | null = null;
        constructor(_stream: MediaStream, _options?: unknown) {}
        start() { this.state = 'recording'; }
        pause() { this.state = 'paused'; }
        resume() { this.state = 'recording'; }
        stop() { this.state = 'inactive'; }
        static isTypeSupported() { return true; }
      }
      (global as unknown as { MediaRecorder: typeof MockMediaRecorder }).MediaRecorder = MockMediaRecorder;

      recorder.start({} as MediaStream);
      expect(recorder.getState().isRecording).toBe(true);
      expect(recorder.getState().isPaused).toBe(false);

      // Fire 50 rapid pause/resume cycles
      for (let i = 0; i < 50; i++) {
        recorder.pause();
        expect(recorder.getState().isPaused).toBe(true);
        recorder.resume();
        expect(recorder.getState().isPaused).toBe(false);
      }

      expect(recorder.getState().isRecording).toBe(true);
    });

    it('S-1.3: Handles zero-size blob events by ignoring empty frames', () => {
      const sessionId = 'stress-speaking-empty-frames';
      const recorder = new DualTrackRecorder(sessionId);

      let onData: ((e: { data: Blob }) => void) | null = null;
      class MockMediaRecorder {
        state = 'inactive';
        ondataavailable: ((e: { data: Blob }) => void) | null = null;
        constructor() {
          onData = (e) => { if (this.ondataavailable) this.ondataavailable(e); };
        }
        start() { this.state = 'recording'; }
        stop() { this.state = 'inactive'; }
        static isTypeSupported() { return true; }
      }
      (global as unknown as { MediaRecorder: typeof MockMediaRecorder }).MediaRecorder = MockMediaRecorder;

      recorder.start({} as MediaStream);

      // Send 10 empty blobs (size = 0)
      for (let i = 0; i < 10; i++) {
        (onData as unknown as (e: { data: Blob }) => void)?.({ data: new Blob([], { type: 'audio/webm' }) });
      }

      // Chunk count and totalBytes should remain 0
      expect(recorder.getState().chunkCount).toBe(0);
      expect(recorder.getState().totalBytes).toBe(0);
      expect(recorder.getBlobNow()).toBeNull();
    });

    it('S-1.4: IndexedDB storage and retrieval degradation when IDB is unavailable', async () => {
      // In node environment without window.indexedDB, retrieveStoredRecording and clearStoredRecording return gracefully
      const res = await retrieveStoredRecording('missing-idb-session');
      expect(res).toBeNull();

      await expect(clearStoredRecording('missing-idb-session')).resolves.toBeUndefined();
    });
  });

  // =========================================================================
  // 2. LISTENING AUDIO CHUNK PRE-BUFFERING & CORRUPTION RECOVERY
  // =========================================================================
  describe('2. Listening Audio Pre-Buffering & Corruption Recovery', () => {
    it('L-2.1: Pre-buffers 10-phase exam audio assets with batching and error resiliency', async () => {
      const audioChunks = [
        'https://cdn.oetprep.com/audio/A1_consultation.mp3',
        'https://cdn.oetprep.com/audio/A2_consultation.mp3',
        'https://cdn.oetprep.com/audio/B1_extract.mp3',
        'https://cdn.oetprep.com/audio/B2_extract.mp3',
        'https://cdn.oetprep.com/audio/B3_extract.mp3',
        'https://cdn.oetprep.com/audio/B4_extract.mp3',
        'https://cdn.oetprep.com/audio/B5_extract.mp3',
        'https://cdn.oetprep.com/audio/B6_extract.mp3',
        'https://cdn.oetprep.com/audio/C1_presentation.mp3',
        'https://cdn.oetprep.com/audio/C2_presentation.mp3',
      ];

      const originalFetch = global.fetch;
      const fakeBlob = new Blob(['sample-audio-data-bytes'], { type: 'audio/mpeg' });
      URL.createObjectURL = vi.fn((b: Blob) => `blob:http://localhost/audio-chunk-${Math.random()}`);

      global.fetch = vi.fn().mockImplementation((url: string) => {
        // Simulate transient network corruption on B3 and C1
        if (url.includes('B3_extract') || url.includes('C1_presentation')) {
          return Promise.resolve({
            ok: false,
            status: 503,
            statusText: 'Service Unavailable',
          } as Response);
        }
        return Promise.resolve({
          ok: true,
          status: 200,
          blob: () => Promise.resolve(fakeBlob),
        } as unknown as Response);
      });

      const progressSteps: AudioPrebufferProgress[] = [];
      const result = await prebufferAudioChunks(audioChunks, (p) => progressSteps.push({ ...p }), 3);

      global.fetch = originalFetch;

      expect(result.success).toBe(false);
      expect(result.prebufferedCount).toBe(8);
      expect(result.failedCount).toBe(2);
      expect(result.cachedUrls.size).toBe(8);
      expect(result.warnings.length).toBe(2);

      // Verify that progress steps were monotonic
      for (let i = 1; i < progressSteps.length; i++) {
        expect(progressSteps[i].loaded + progressSteps[i].failed).toBeGreaterThanOrEqual(
          progressSteps[i - 1].loaded + progressSteps[i - 1].failed,
        );
      }
      expect(progressSteps[progressSteps.length - 1].percent).toBe(100);
    });

    it('L-2.2: Audio seek guard strictly prevents scrubbing during exam mode', () => {
      // In exam mode (canScrub = false), any attempt to seek returns the last known playback position
      const lastKnown = 42.5;
      const blocked1 = resolveBlockedSeekTarget({
        canScrub: false,
        requestedTime: 120.0,
        lastKnownTime: lastKnown,
        allowedProgrammaticTarget: null,
      });
      expect(blocked1).toBe(42.5);

      const blocked2 = resolveBlockedSeekTarget({
        canScrub: false,
        requestedTime: 10.0,
        lastKnownTime: lastKnown,
        allowedProgrammaticTarget: null,
      });
      expect(blocked2).toBe(42.5);

      // In practice mode (canScrub = true), scrubbing is allowed (returns null = no lock override)
      const allowed = resolveBlockedSeekTarget({
        canScrub: true,
        requestedTime: 120.0,
        lastKnownTime: lastKnown,
        allowedProgrammaticTarget: null,
      });
      expect(allowed).toBeNull();
    });

    it('L-2.3: Audio pause guard forces auto-resume when candidate attempts pausing in active exam audio', () => {
      // Candidate presses spacebar or clicks pause in active exam audio
      const shouldResume = shouldResumeAfterBlockedPause({
        canPause: false,
        phase: 'audio',
        hasStarted: true,
        hasReachedEnd: false,
        allowedProgrammaticPause: false,
      });
      expect(shouldResume).toBe(true);

      // If phase is preview or review, pause is not blocked
      const inPreview = shouldResumeAfterBlockedPause({
        canPause: false,
        phase: 'preview',
        hasStarted: true,
        hasReachedEnd: false,
        allowedProgrammaticPause: false,
      });
      expect(inPreview).toBe(false);

      // In practice mode (canPause = true), pause is allowed
      const practicePause = shouldResumeAfterBlockedPause({
        canPause: true,
        phase: 'audio',
        hasStarted: true,
        hasReachedEnd: false,
        allowedProgrammaticPause: false,
      });
      expect(practicePause).toBe(false);
    });
  });

  // =========================================================================
  // 3. DEBOUNCED AUTOSAVE & OFFLINE ANSWER RECONCILIATION
  // =========================================================================
  describe('3. Debounced Autosave & Offline Answer Reconciliation', () => {
    it('O-3.1: 3-way offline reconciliation handles safe replay, already-synced, and conflict cases', () => {
      const queuedAnswer: OfflineAnswerPayload = {
        questionId: 'reading-part-a-q5',
        value: 'paracetamol 1g PO QDS',
        baseValue: 'paracetamol 500mg',
      };

      // Case 1: Server still has baseValue -> safe to replay
      const resSafe = reconcileOfflineAnswer('paracetamol 500mg', queuedAnswer);
      expect(resSafe).toBe('safe-to-replay');

      // Case 2: Server already received this value (network ACK dropped) -> already synced
      const resSynced = reconcileOfflineAnswer('paracetamol 1g PO QDS', queuedAnswer);
      expect(resSynced).toBe('already-synced');

      // Case 3: Server was modified from another device / authoritative admin -> conflict, server wins
      const resConflict = reconcileOfflineAnswer('ibuprofen 400mg', queuedAnswer);
      expect(resConflict).toBe('conflict');

      // Case 4: Server has null/empty but baseValue was empty -> safe to replay
      const queuedFromBlank: OfflineAnswerPayload = {
        questionId: 'q-1',
        value: 'initial draft',
        baseValue: null,
      };
      expect(reconcileOfflineAnswer(null, queuedFromBlank)).toBe('safe-to-replay');
      expect(reconcileOfflineAnswer(undefined, queuedFromBlank)).toBe('safe-to-replay');
    });

    it('O-3.2: Offline answer encryption security invariant: fails closed when key is missing', async () => {
      clearOfflineEncryptionKey();

      await expect(
        queueOfflineAttempt('listening-answer', 'session-100', {
          questionId: 'q-23',
          value: 'hypoglycaemia',
        }),
      ).rejects.toThrow(/Offline answer encryption is unavailable/i);
    });

    it('O-3.3: High-speed typing simulation coalesces keystrokes and preserves final payload', async () => {
      // Simulating a debounce accumulator
      let debouncedPayload: string | null = null;
      let saveCount = 0;

      const debounceTimerMs = 50;
      let timer: NodeJS.Timeout | null = null;

      const triggerAutosave = (text: string) => {
        if (timer) clearTimeout(timer);
        timer = setTimeout(() => {
          debouncedPayload = text;
          saveCount++;
        }, debounceTimerMs);
      };

      // Burst of 100 keystrokes within 20ms
      const fullText = 'The patient was admitted with acute severe chest pain radiating to left jaw.';
      for (let i = 1; i <= fullText.length; i++) {
        triggerAutosave(fullText.slice(0, i));
      }

      // Wait for debounce timer to settle
      await new Promise((r) => setTimeout(r, 80));

      expect(saveCount).toBe(1);
      expect(debouncedPayload).toBe(fullText);
    });
  });

  // =========================================================================
  // 4. BOUNDARY STRESS ON HIGH-SPEED INPUTS & RAPID PHASE SWITCHING
  // =========================================================================
  describe('4. Rapid Phase Switching & FSM State Machine Integrity', () => {
    it('FSM-4.1: Traverses full 17-state linear CBT Listening FSM path without deadlocks', () => {
      expect(LISTENING_FORWARD_PATH).toHaveLength(17);
      expect(LISTENING_FORWARD_PATH[0]).toBe('intro');
      expect(LISTENING_FORWARD_PATH[LISTENING_FORWARD_PATH.length - 1]).toBe('submitted');

      let currentState: ListeningFsmState | null = 'intro';
      const visitedStates: ListeningFsmState[] = ['intro'];

      while (currentState && currentState !== 'submitted') {
        currentState = nextListeningState(currentState);
        if (currentState) {
          visitedStates.push(currentState);
        }
      }

      expect(visitedStates).toEqual([...LISTENING_FORWARD_PATH]);
      expect(nextListeningState('submitted')).toBeNull();
    });

    it('FSM-4.2: Maps every Listening state to correct subtest part, player phase, and position', () => {
      for (const state of LISTENING_FSM_STATES) {
        const part = listeningPartFor(state);
        const pos = listeningPositionForState(state);

        if (state.startsWith('a1_')) {
          expect(part).toBe('A1');
          expect(pos?.section).toBe('A1');
        } else if (state.startsWith('a2_')) {
          expect(part).toBe('A2');
          expect(pos?.section).toBe('A2');
        } else if (state.startsWith('b_')) {
          expect(part).toBe('B');
          expect(pos?.section).toBe('B');
        } else if (state.startsWith('c1_')) {
          expect(part).toBe('C1');
          expect(pos?.section).toBe('C1');
        } else if (state.startsWith('c2_')) {
          expect(part).toBe('C2');
          expect(pos?.section).toBe('C2');
        }

        if (isAudioState(state)) {
          expect(state.endsWith('_audio')).toBe(true);
        }
        if (isPreviewState(state)) {
          expect(state.endsWith('_preview') || state === 'b_intro').toBe(true);
        }
        if (isReviewState(state)) {
          expect(state.endsWith('_review') || state === 'c2_final_review').toBe(true);
        }
      }
    });

    it('FSM-4.3: Bidirectional mapping parity between (section, phase) and FSM state', () => {
      const checkPairs: Array<[ListeningFsmState, { section: string; phase: string }]> = [
        ['a1_preview', { section: 'A1', phase: 'preview' }],
        ['a1_audio', { section: 'A1', phase: 'audio' }],
        ['a1_review', { section: 'A1', phase: 'review' }],
        ['a2_preview', { section: 'A2', phase: 'preview' }],
        ['a2_audio', { section: 'A2', phase: 'audio' }],
        ['a2_review', { section: 'A2', phase: 'review' }],
        ['b_intro', { section: 'B', phase: 'preview' }],
        ['b_audio', { section: 'B', phase: 'audio' }],
        ['c1_preview', { section: 'C1', phase: 'preview' }],
        ['c1_audio', { section: 'C1', phase: 'audio' }],
        ['c1_review', { section: 'C1', phase: 'review' }],
        ['c2_preview', { section: 'C2', phase: 'preview' }],
        ['c2_audio', { section: 'C2', phase: 'audio' }],
        ['c2_review', { section: 'C2', phase: 'review' }],
      ];

      for (const [state, pos] of checkPairs) {
        const computedPos = listeningPositionForState(state);
        expect(computedPos).toEqual(pos);

        const computedState = listeningStateForPosition(pos.section as any, pos.phase as any);
        expect(computedState).toBe(state);
      }
    });

    it('FSM-4.4: listeningWindowSeconds handles edge cases, negative, null, and Infinity safely', () => {
      expect(listeningWindowSeconds(null)).toBe(0);
      expect(listeningWindowSeconds(undefined)).toBe(0);
      expect(listeningWindowSeconds(NaN)).toBe(0);
      expect(listeningWindowSeconds(Infinity)).toBe(0);
      expect(listeningWindowSeconds(-5000)).toBe(0);
      expect(listeningWindowSeconds(0)).toBe(0);
      expect(listeningWindowSeconds(100)).toBe(1);   // Ceiled to 1 second
      expect(listeningWindowSeconds(1000)).toBe(1);
      expect(listeningWindowSeconds(1001)).toBe(2);  // Ceiled to 2 seconds
      expect(listeningWindowSeconds(900000)).toBe(900); // 15 minutes = 900s
    });
  });
});
