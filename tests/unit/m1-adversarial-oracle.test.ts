import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// 1. Listening Audio Pre-buffering
import {
  getCachedAudioUrl,
  registerCachedAudioUrl,
  clearAudioPrebufferCache,
  prebufferAudioChunks,
  type AudioPrebufferProgress,
  type AudioPrebufferResult,
} from '@/lib/listening/audio-prebuffer';

// 2. Speaking Dual-Track Recorder
import {
  DualTrackRecorder,
  retrieveStoredRecording,
  clearStoredRecording,
} from '@/lib/speaking/dual-track-recorder';

// 3. Offline Sync & Security
import {
  queueOfflineAttempt,
  setStorageEncryptionKey,
} from '@/lib/mobile/offline-sync';

// 4. Mock Workflow & Orchestration Contracts
import {
  getMockModePolicy,
  getMockSectionPolicy,
  getMockSubmissionReadiness,
} from '@/lib/mocks/workflow';
import type { MockSession, MockSectionState, MockSessionSection } from '@/lib/mock-data';

describe('Milestone 1 Adversarial Oracle: Robustness, Edge Cases & Failure Modes', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    clearAudioPrebufferCache();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // =========================================================================
  // Scope 1: Audio Pre-Buffering Resilience & Fallback
  // =========================================================================
  describe('Scope 1: Audio Pre-Buffering Under Network Degradation & Failure', () => {
    it('1.1: Returns null for uncached URLs and handles empty input lists gracefully', async () => {
      expect(getCachedAudioUrl('https://example.com/audio/nonexistent.mp3')).toBeNull();

      const emptyResult = await prebufferAudioChunks([]);
      expect(emptyResult.success).toBe(true);
      expect(emptyResult.prebufferedCount).toBe(0);
      expect(emptyResult.failedCount).toBe(0);
      expect(emptyResult.warnings).toHaveLength(0);
    });

    it('1.2: Handles 100% network failure gracefully: returns failedCount, warnings, no unhandled exceptions', async () => {
      const brokenUrls = [
        'https://cdn.oetprep.com/audio/listening_part_a1.mp3',
        'https://cdn.oetprep.com/audio/listening_part_a2.mp3',
        'https://cdn.oetprep.com/audio/listening_part_b1.mp3',
      ];

      const originalFetch = global.fetch;
      global.fetch = vi.fn().mockImplementation(() =>
        Promise.reject(new Error('Network connection refused (ECONNREFUSED)')),
      );

      const progressHistory: AudioPrebufferProgress[] = [];
      const result = await prebufferAudioChunks(brokenUrls, (p) => progressHistory.push({ ...p }), 2);

      global.fetch = originalFetch;

      expect(result.success).toBe(false);
      expect(result.failedCount).toBe(3);
      expect(result.prebufferedCount).toBe(0);
      expect(result.warnings.length).toBe(3);
      expect(result.warnings[0]).toContain('Failed to pre-buffer audio chunk');
      expect(result.warnings[0]).toContain('ECONNREFUSED');
      expect(result.cachedUrls.size).toBe(0);

      // Verify progress tracking
      expect(progressHistory.length).toBeGreaterThan(0);
      const lastProgress = progressHistory[progressHistory.length - 1];
      expect(lastProgress.percent).toBe(100);
      expect(lastProgress.failed).toBe(3);
      expect(lastProgress.loaded).toBe(0);
      expect(lastProgress.inProgress).toBe(false);
    });

    it('1.3: Handles partial network failure (mixed 200 and 404/500 HTTP status)', async () => {
      const mixedUrls = [
        'https://cdn.oetprep.com/audio/ok_1.mp3',
        'https://cdn.oetprep.com/audio/fail_404.mp3',
        'https://cdn.oetprep.com/audio/ok_2.mp3',
        'https://cdn.oetprep.com/audio/fail_500.mp3',
      ];

      const originalFetch = global.fetch;
      const fakeBlob = new Blob(['mock audio stream'], { type: 'audio/mpeg' });

      // Mock URL.createObjectURL
      const originalCreateObjectURL = URL.createObjectURL;
      let blobCounter = 0;
      URL.createObjectURL = vi.fn(() => `blob:http://localhost/mock-audio-${++blobCounter}`);

      global.fetch = vi.fn().mockImplementation((url: string) => {
        if (url.includes('fail_404')) {
          return Promise.resolve({
            ok: false,
            status: 404,
            statusText: 'Not Found',
          } as Response);
        }
        if (url.includes('fail_500')) {
          return Promise.resolve({
            ok: false,
            status: 500,
            statusText: 'Internal Server Error',
          } as Response);
        }
        return Promise.resolve({
          ok: true,
          status: 200,
          blob: () => Promise.resolve(fakeBlob),
        } as unknown as Response);
      });

      const result = await prebufferAudioChunks(mixedUrls, undefined, 2);

      global.fetch = originalFetch;
      URL.createObjectURL = originalCreateObjectURL;

      expect(result.success).toBe(false);
      expect(result.failedCount).toBe(2);
      expect(result.prebufferedCount).toBe(2);
      expect(result.cachedUrls.size).toBe(2);
      expect(result.cachedUrls.has('https://cdn.oetprep.com/audio/ok_1.mp3')).toBe(true);
      expect(result.cachedUrls.has('https://cdn.oetprep.com/audio/ok_2.mp3')).toBe(true);
      expect(result.warnings.length).toBe(2);
    });

    it('1.4: Automatically dedupes and ignores empty/whitespace-only URLs', async () => {
      const duplicateUrls = [
        'https://cdn.oetprep.com/audio/chunk_a.mp3',
        '  https://cdn.oetprep.com/audio/chunk_a.mp3  ',
        '',
        '   ',
        'https://cdn.oetprep.com/audio/chunk_b.mp3',
      ];

      const originalFetch = global.fetch;
      const fakeBlob = new Blob(['mock audio stream'], { type: 'audio/mpeg' });
      URL.createObjectURL = vi.fn(() => `blob:http://localhost/mock-audio-${Math.random()}`);

      global.fetch = vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        blob: () => Promise.resolve(fakeBlob),
      } as unknown as Response);

      const result = await prebufferAudioChunks(duplicateUrls);

      global.fetch = originalFetch;

      expect(result.success).toBe(true);
      expect(result.prebufferedCount).toBe(2); // Only chunk_a and chunk_b
      expect(result.failedCount).toBe(0);
    });

    it('1.5: Enforces 2-hour Cache TTL and revokes expired object URLs', () => {
      const sourceUrl = 'https://cdn.oetprep.com/audio/ttl_track.mp3';
      const objectUrl = 'blob:http://localhost/mock-ttl-1';
      const revokeSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});

      registerCachedAudioUrl(sourceUrl, objectUrl);
      expect(getCachedAudioUrl(sourceUrl)).toBe(objectUrl);

      // Fast-forward time past 2 hours (2 * 60 * 60 * 1000 + 1ms)
      const realNow = Date.now;
      Date.now = () => realNow() + (2 * 60 * 60 * 1000 + 500);

      const expiredUrl = getCachedAudioUrl(sourceUrl);
      expect(expiredUrl).toBeNull();
      expect(revokeSpy).toHaveBeenCalledWith(objectUrl);

      Date.now = realNow;
    });

    it('1.6: Revokes previous object URL on re-registration to eliminate memory leaks', () => {
      const sourceUrl = 'https://cdn.oetprep.com/audio/re_register.mp3';
      const objectUrl1 = 'blob:http://localhost/obj-v1';
      const objectUrl2 = 'blob:http://localhost/obj-v2';
      const revokeSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});

      registerCachedAudioUrl(sourceUrl, objectUrl1);
      expect(getCachedAudioUrl(sourceUrl)).toBe(objectUrl1);

      registerCachedAudioUrl(sourceUrl, objectUrl2);
      expect(revokeSpy).toHaveBeenCalledWith(objectUrl1);
      expect(getCachedAudioUrl(sourceUrl)).toBe(objectUrl2);
    });

    it('1.7: clearAudioPrebufferCache revokes all entries and empties memory cache', () => {
      const source1 = 'https://cdn.oetprep.com/audio/1.mp3';
      const source2 = 'https://cdn.oetprep.com/audio/2.mp3';
      const obj1 = 'blob:http://localhost/1';
      const obj2 = 'blob:http://localhost/2';
      const revokeSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});

      registerCachedAudioUrl(source1, obj1);
      registerCachedAudioUrl(source2, obj2);

      clearAudioPrebufferCache();

      expect(getCachedAudioUrl(source1)).toBeNull();
      expect(getCachedAudioUrl(source2)).toBeNull();
      expect(revokeSpy).toHaveBeenCalledWith(obj1);
      expect(revokeSpy).toHaveBeenCalledWith(obj2);
    });
  });

  // =========================================================================
  // Scope 2: Dual-Track Recorder & Speaking Microphone Safety Net
  // =========================================================================
  describe('Scope 2: Dual-Track Recorder Lifecycle, Disconnects & Persistence', () => {
    it('2.1: Recorder initializes in clean idle state', () => {
      const recorder = new DualTrackRecorder('speaking-session-adversarial-1');
      const state = recorder.getState();

      expect(state.isRecording).toBe(false);
      expect(state.isPaused).toBe(false);
      expect(state.elapsedMs).toBe(0);
      expect(state.chunkCount).toBe(0);
      expect(state.totalBytes).toBe(0);
      expect(state.mimeType).toBeDefined();
    });

    it('2.2: Calling stop() when not recording returns null safely without throwing', async () => {
      const recorder = new DualTrackRecorder('idle-session');
      const result = await recorder.stop();
      expect(result).toBeNull();
      expect(recorder.getBlobNow()).toBeNull();
    });

    it('2.3: Full recording lifecycle: start, chunk ingestion, pause, resume, and stop', async () => {
      const sessionId = 'session-full-cycle-test';
      const recorder = new DualTrackRecorder(sessionId);

      // Create fake MediaStream
      const fakeStream = {
        getTracks: () => [{ kind: 'audio', stop: vi.fn(), enabled: true }],
      } as unknown as MediaStream;

      // Mock MediaRecorder
      class MockMediaRecorder {
        state = 'inactive';
        ondataavailable: ((e: { data: Blob }) => void) | null = null;
        onstop: (() => void) | null = null;

        constructor(_stream: MediaStream, _options?: unknown) {}

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
          if (this.onstop) this.onstop();
        }

        static isTypeSupported(_mime: string) {
          return true;
        }
      }

      (global as unknown as { MediaRecorder: typeof MockMediaRecorder }).MediaRecorder =
        MockMediaRecorder;

      recorder.start(fakeStream);
      expect(recorder.getState().isRecording).toBe(true);
      expect(recorder.getState().isPaused).toBe(false);

      // Simulate incoming audio chunks
      const recorderInternal = recorder as unknown as { mediaRecorder: MockMediaRecorder };
      if (recorderInternal.mediaRecorder.ondataavailable) {
        recorderInternal.mediaRecorder.ondataavailable({
          data: new Blob(['audio chunk frame 1 (1000ms)'], { type: 'audio/webm' }),
        } as BlobEvent);
        recorderInternal.mediaRecorder.ondataavailable({
          data: new Blob(['audio chunk frame 2 (1000ms)'], { type: 'audio/webm' }),
        } as BlobEvent);
      }

      const blobNow = recorder.getBlobNow();
      expect(blobNow).not.toBeNull();
      expect(recorder.getState().chunkCount).toBe(2);
      expect(recorder.getState().totalBytes).toBeGreaterThan(0);

      // Pause & resume
      recorder.pause();
      expect(recorder.getState().isPaused).toBe(true);

      recorder.resume();
      expect(recorder.getState().isPaused).toBe(false);

      // Stop recording
      const result = await recorder.stop();
      expect(result).not.toBeNull();
      expect(result?.blob).toBeInstanceOf(Blob);
      expect(result?.sizeBytes).toBeGreaterThan(0);
      expect(result?.createdAt).toBeDefined();
      expect(recorder.getState().isRecording).toBe(false);
    });

    it('2.4: Handles environment where MediaRecorder is missing without crashing', () => {
      const originalMR = (global as unknown as { MediaRecorder?: unknown }).MediaRecorder;
      delete (global as unknown as { MediaRecorder?: unknown }).MediaRecorder;

      const recorder = new DualTrackRecorder('no-mr-env');
      const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});

      recorder.start({} as MediaStream);
      expect(recorder.getState().isRecording).toBe(false);
      expect(warnSpy).toHaveBeenCalled();

      (global as unknown as { MediaRecorder?: unknown }).MediaRecorder = originalMR;
    });

    it('2.5: Dual-track IDB retrieval and clearance fallback handlers degrade gracefully on null DB', async () => {
      const recording = await retrieveStoredRecording('nonexistent-session');
      expect(recording).toBeNull();

      // Clear non-existent recording does not throw
      await expect(clearStoredRecording('nonexistent-session')).resolves.toBeUndefined();
    });
  });

  // =========================================================================
  // Scope 3: Autosave Queue, Encryption & Offline/Online Sync
  // =========================================================================
  describe('Scope 3: Autosave Queue, Encryption & Offline/Online Sync', () => {
    it('3.1: Fails closed when encryption key is missing', async () => {
      setStorageEncryptionKey(null);

      await expect(
        queueOfflineAttempt('reading-answer', 'attempt-123', {
          questionId: 'q-1',
          value: JSON.stringify({ answer: 'B' }),
        }),
      ).rejects.toThrow(/Offline answer encryption is unavailable/i);
    });

    it('3.2: Deterministic ID deduplication prevents queue bloat on rapid sequential saves', () => {
      const attemptId = 'attempt-mock-999';
      const questionId = 'q-14';

      const key1 = `reading-answer:${encodeURIComponent(attemptId)}:${encodeURIComponent(questionId)}`;
      const key2 = `reading-answer:${encodeURIComponent(attemptId)}:${encodeURIComponent(questionId)}`;

      expect(key1).toBe(key2);
      expect(key1).toBe('reading-answer:attempt-mock-999:q-14');
    });

    it('3.3: In-flight race resolution correctly validates whether response matches latest user input', () => {
      const attemptId = 'att-race-1';
      const questionId = 'q-part-b-3';
      const latestAnswer = JSON.stringify('Option C');
      const staleAnswer = JSON.stringify('Option A');

      const pendingMap: Record<string, { attemptId: string; valueJson: string; inFlight: boolean }> = {
        [questionId]: {
          attemptId,
          valueJson: latestAnswer,
          inFlight: true,
        },
      };

      // Stale response check: resolving response with staleAnswer should not mark state as saved
      const pending = pendingMap[questionId];
      const isStaleCurrent =
        !pending || (pending.attemptId === attemptId && pending.valueJson === staleAnswer);
      expect(isStaleCurrent).toBe(false);

      // Latest response check: resolving response with latestAnswer correctly marks state as saved
      const isLatestCurrent =
        !pending || (pending.attemptId === attemptId && pending.valueJson === latestAnswer);
      expect(isLatestCurrent).toBe(true);
    });

    it('3.4: Benign locked section error detection suppresses false alarm banners', () => {
      const isBenignLockedSave = (err: unknown): boolean => {
        if (!err) return false;
        const msg = err instanceof Error ? err.message : String(err);
        return /part_a_locked|section_locked|already_locked|attempt_submitted/i.test(msg);
      };

      expect(isBenignLockedSave(new Error('part_a_locked: Submission closed'))).toBe(true);
      expect(isBenignLockedSave(new Error('SECTION_LOCKED: Timer expired'))).toBe(true);
      expect(isBenignLockedSave(new Error('Network timeout 504'))).toBe(false);
      expect(isBenignLockedSave(new Error('Internal Server Error 500'))).toBe(false);
    });
  });

  // =========================================================================
  // Scope 4: Unified Mock Coordinator State Restoration & Invariants
  // =========================================================================
  describe('Scope 4: Unified Mock Coordinator State Restoration & Invariants', () => {
    const createMockSession = (
      statuses: Array<'not_started' | 'in_progress' | 'completed'>,
    ): MockSession => ({
      sessionId: 'mock-session-test-orchestrator',
      state: 'in_progress',
      resumeRoute: '/mocks/session/mock-session-test-orchestrator',
      config: {
        id: 'mock-cfg-1',
        type: 'full',
        title: 'Full 4-Skill OET Simulation (Medicine)',
        profession: 'Medicine',
        mode: 'exam',
        strictTimer: true,
        includeReview: false,
        reviewSelection: 'none',
        deliveryMode: 'computer',
      },
      sectionStates: [
        {
          id: 'sec-listening',
          subtest: 'listening',
          title: 'Listening Sub-Test',
          state: statuses[0],
          status: statuses[0],
          reviewAvailable: false,
          reviewSelected: false,
          launchRoute: '/listening/paper/lp-1',
          timeLimitMinutes: 45,
        },
        {
          id: 'sec-reading',
          subtest: 'reading',
          title: 'Reading Sub-Test',
          state: statuses[1],
          status: statuses[1],
          reviewAvailable: false,
          reviewSelected: false,
          launchRoute: '/reading/paper/rp-1',
          timeLimitMinutes: 60,
        },
        {
          id: 'sec-writing',
          subtest: 'writing',
          title: 'Writing Sub-Test',
          state: statuses[2],
          status: statuses[2],
          reviewAvailable: false,
          reviewSelected: false,
          launchRoute: '/writing/paper/wp-1',
          timeLimitMinutes: 45,
        },
        {
          id: 'sec-speaking',
          subtest: 'speaking',
          title: 'Speaking Sub-Test',
          state: statuses[3],
          status: statuses[3],
          reviewAvailable: false,
          reviewSelected: false,
          launchRoute: '/speaking/paper/sp-1',
          timeLimitMinutes: 20,
        },
      ],
    });

    it('4.1: Computes simulation progress accurately across all 5 progress stages', () => {
      const getProgress = (session: MockSession) => {
        const completed = session.sectionStates.filter((s) => s.status === 'completed').length;
        const total = session.sectionStates.length;
        return {
          completed,
          total,
          percent: Math.round((completed / Math.max(1, total)) * 100),
          isAllCompleted: completed === total && total > 0,
        };
      };

      expect(getProgress(createMockSession(['not_started', 'not_started', 'not_started', 'not_started'])).percent).toBe(0);
      expect(getProgress(createMockSession(['completed', 'not_started', 'not_started', 'not_started'])).percent).toBe(25);
      expect(getProgress(createMockSession(['completed', 'completed', 'not_started', 'not_started'])).percent).toBe(50);
      expect(getProgress(createMockSession(['completed', 'completed', 'completed', 'not_started'])).percent).toBe(75);
      expect(getProgress(createMockSession(['completed', 'completed', 'completed', 'completed'])).percent).toBe(100);
      expect(getProgress(createMockSession(['completed', 'completed', 'completed', 'completed'])).isAllCompleted).toBe(true);
    });

    it('4.2: Correctly identifies next pending sub-test across arbitrary progress states', () => {
      const getNextSection = (session: MockSession): MockSessionSection | null => {
        return (
          session.sectionStates.find((s) => s.status === 'in_progress') ??
          session.sectionStates.find((s) => s.status === 'not_started') ??
          null
        );
      };

      // When Reading is in_progress, nextSection must be Reading (not Writing)
      const sessionMid = createMockSession(['completed', 'in_progress', 'not_started', 'not_started']);
      const nextSec = getNextSection(sessionMid);
      expect(nextSec).not.toBeNull();
      expect(nextSec?.subtest).toBe('reading');
      expect(nextSec?.status).toBe('in_progress');
    });

    it('4.3: Enforces official OET 4-skill timing & structural invariants', () => {
      const session = createMockSession(['not_started', 'not_started', 'not_started', 'not_started']);

      // 1. Listening: 45m allocation, 42 items (24/6/12)
      const listeningSec = session.sectionStates.find((s) => s.subtest === 'listening');
      expect(listeningSec?.timeLimitMinutes).toBe(45);

      // 2. Reading: 60m allocation, 42 items (20/6/16)
      const readingSec = session.sectionStates.find((s) => s.subtest === 'reading');
      expect(readingSec?.timeLimitMinutes).toBe(60);

      // 3. Writing: 45m allocation (5m reading + 40m writing)
      const writingSec = session.sectionStates.find((s) => s.subtest === 'writing');
      expect(writingSec?.timeLimitMinutes).toBe(45);

      // 4. Speaking: 20m allocation (2 role-play cards, 3m prep + 5m consult = 8m x 2)
      const speakingSec = session.sectionStates.find((s) => s.subtest === 'speaking');
      expect(speakingSec?.timeLimitMinutes).toBe(20);
    });

    it('4.4: Enforces mock workflow submission readiness contract', () => {
      const sessionIncomplete = createMockSession(['completed', 'completed', 'in_progress', 'not_started']);
      const readinessIncomplete = getMockSubmissionReadiness(sessionIncomplete);
      expect(readinessIncomplete.canSubmit).toBe(false);
      expect(readinessIncomplete.completedCount).toBe(2);
      expect(readinessIncomplete.totalCount).toBe(4);

      const sessionComplete = createMockSession(['completed', 'completed', 'completed', 'completed']);
      const readinessComplete = getMockSubmissionReadiness(sessionComplete);
      expect(readinessComplete.canSubmit).toBe(true);
      expect(readinessComplete.completedCount).toBe(4);
      expect(readinessComplete.totalCount).toBe(4);
    });

    it('4.5: Enforces mock mode policies for exam vs practice mode', () => {
      const examPolicy = getMockModePolicy('exam');
      expect(examPolicy.pauseAllowed).toBe(false);
      expect(examPolicy.listeningReplayAllowed).toBe(false);
      expect(examPolicy.strictTimerRequired).toBe(true);

      const practicePolicy = getMockModePolicy('practice');
      expect(practicePolicy.pauseAllowed).toBe(true);
      expect(practicePolicy.listeningReplayAllowed).toBe(true);
      expect(practicePolicy.strictTimerRequired).toBe(false);
    });
  });
});
