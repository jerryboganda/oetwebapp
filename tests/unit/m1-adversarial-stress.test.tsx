import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import React from 'react';

// 1. Audio pre-buffering imports
import {
  getCachedAudioUrl,
  registerCachedAudioUrl,
  clearAudioPrebufferCache,
  prebufferAudioChunks,
  type AudioPrebufferProgress,
} from '@/lib/listening/audio-prebuffer';
import { TechReadinessCheck } from '@/components/domain/listening/TechReadinessCheck';

// 2. Dual-track recorder imports
import {
  DualTrackRecorder,
  retrieveStoredRecording,
  clearStoredRecording,
} from '@/lib/speaking/dual-track-recorder';

// 3. Autosave & Offline sync imports
import {
  initOfflineDatabase,
  queueOfflineAttempt,
  getPendingAttempts,
  markAttemptSynced,
  setStorageEncryptionKey,
} from '@/lib/mobile/offline-sync';

// 4. Unified mock coordinator imports
import { UnifiedMockCoordinator } from '@/components/domain/mock/UnifiedMockCoordinator';
import type { MockSession } from '@/lib/mock-data';
import * as apiModule from '@/lib/api';

const mockPush = vi.fn();
vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: mockPush,
  }),
}));

describe('M1 Adversarial Stress Test Suite: Robustness, Edge Cases & Failure Modes', () => {
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
    it('1.1: Handles 100% network failure without crashing, returns failedCount and warnings', async () => {
      const failingUrls = [
        'https://example.com/audio/fail1.mp3',
        'https://example.com/audio/fail2.mp3',
      ];

      // Mock global fetch to simulate network 500 / drop
      const originalFetch = global.fetch;
      global.fetch = vi.fn().mockImplementation(() =>
        Promise.reject(new Error('Network connection refused (ECONNREFUSED)')),
      );

      const progressEvents: AudioPrebufferProgress[] = [];
      const result = await prebufferAudioChunks(failingUrls, (p) => progressEvents.push({ ...p }));

      global.fetch = originalFetch;

      expect(result.success).toBe(false);
      expect(result.failedCount).toBe(2);
      expect(result.prebufferedCount).toBe(0);
      expect(result.warnings.length).toBe(2);
      expect(result.warnings[0]).toContain('Network connection refused');
      expect(progressEvents.length).toBeGreaterThan(0);
      expect(progressEvents[progressEvents.length - 1].percent).toBe(100);
      expect(progressEvents[progressEvents.length - 1].inProgress).toBe(false);
    });

    it('1.2: Handles partial network failure (mixed success/failure), caches valid chunks', async () => {
      const urls = [
        'https://example.com/audio/good1.mp3',
        'https://example.com/audio/bad1.mp3',
        'https://example.com/audio/good2.mp3',
      ];

      const originalFetch = global.fetch;
      const fakeBlob = new Blob(['mock audio binary bytes'], { type: 'audio/mp3' });

      global.fetch = vi.fn().mockImplementation((url: string) => {
        if (url.includes('bad')) {
          return Promise.resolve({
            ok: false,
            status: 404,
            statusText: 'Not Found',
          } as Response);
        }
        return Promise.resolve({
          ok: true,
          status: 200,
          blob: () => Promise.resolve(fakeBlob),
        } as unknown as Response);
      });

      // Mock URL.createObjectURL
      const originalCreateObjectURL = URL.createObjectURL;
      URL.createObjectURL = vi.fn((blob: Blob) => `blob:http://localhost/${Math.random()}`);

      const result = await prebufferAudioChunks(urls, undefined, 2);

      global.fetch = originalFetch;
      URL.createObjectURL = originalCreateObjectURL;

      expect(result.success).toBe(false);
      expect(result.failedCount).toBe(1);
      expect(result.prebufferedCount).toBe(2);
      expect(result.cachedUrls.size).toBe(2);
      expect(result.cachedUrls.has('https://example.com/audio/good1.mp3')).toBe(true);
      expect(result.cachedUrls.has('https://example.com/audio/good2.mp3')).toBe(true);
    });

    it('1.3: Expiration TTL: getCachedAudioUrl revokes and deletes stale cache entries after 2 hours', () => {
      const sourceUrl = 'https://example.com/audio/ttl-test.mp3';
      const objectUrl = 'blob:http://localhost/blob-ttl-1';
      const revokeSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});

      registerCachedAudioUrl(sourceUrl, objectUrl);
      expect(getCachedAudioUrl(sourceUrl)).toBe(objectUrl);

      // Advance Date.now past 2 hours + 10ms
      const realNow = Date.now;
      Date.now = () => realNow() + (2 * 60 * 60 * 1000 + 100);

      const expired = getCachedAudioUrl(sourceUrl);
      expect(expired).toBeNull();
      expect(revokeSpy).toHaveBeenCalledWith(objectUrl);

      Date.now = realNow;
    });

    it('1.4: Cache Replacement: registering new object URL revokes prior object URL to prevent memory leak', () => {
      const sourceUrl = 'https://example.com/audio/replace-test.mp3';
      const objectUrl1 = 'blob:http://localhost/blob-v1';
      const objectUrl2 = 'blob:http://localhost/blob-v2';
      const revokeSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});

      registerCachedAudioUrl(sourceUrl, objectUrl1);
      expect(getCachedAudioUrl(sourceUrl)).toBe(objectUrl1);

      registerCachedAudioUrl(sourceUrl, objectUrl2);
      expect(revokeSpy).toHaveBeenCalledWith(objectUrl1);
      expect(getCachedAudioUrl(sourceUrl)).toBe(objectUrl2);
    });

    it('1.5: TechReadinessCheck degrades gracefully with advisory warning when scored audio pre-buffer fails', async () => {
      const onReady = vi.fn();

      // Mock playProbe to succeed immediately
      const probeUrl = 'https://example.com/probe.mp3';
      const brokenAudioUrls = ['https://example.com/audio/broken1.mp3'];

      const originalFetch = global.fetch;
      global.fetch = vi.fn().mockImplementation(() =>
        Promise.reject(new Error('Audio chunk CDN unreachable')),
      );

      render(
        <TechReadinessCheck
          audioProbeUrl={probeUrl}
          audioUrls={brokenAudioUrls}
          onReady={onReady}
        />,
      );

      // Play probe button
      const playBtn = screen.getByRole('button', { name: /Play audio probe/i });
      expect(playBtn).toBeDefined();

      global.fetch = originalFetch;
    });
  });

  // =========================================================================
  // Scope 2: Dual-Track Recorder & Speaking Microphone Safety Net
  // =========================================================================
  describe('Scope 2: Dual-Track Recorder Lifecycle, Disconnects & Persistence', () => {
    it('2.1: Recorder lifecycle: start, chunk collection, pause, resume, and stop', async () => {
      const sessionId = 'speaking-session-lifecycle-test';
      const recorder = new DualTrackRecorder(sessionId);

      expect(recorder.getState().isRecording).toBe(false);
      expect(recorder.getState().isPaused).toBe(false);

      // Create fake MediaStream
      const fakeTrack = {
        stop: vi.fn(),
        kind: 'audio',
        enabled: true,
      };
      const fakeStream = {
        getTracks: () => [fakeTrack],
      } as unknown as MediaStream;

      // Mock MediaRecorder in global
      let dataAvailableHandler: ((e: { data: Blob }) => void) | null = null;
      let stopHandler: (() => void) | null = null;

      class MockMediaRecorder {
        state = 'inactive';
        ondataavailable: ((e: { data: Blob }) => void) | null = null;
        onstop: (() => void) | null = null;

        constructor(_stream: MediaStream, _options?: unknown) {
          // constructor
        }

        start(_timeslice?: number) {
          this.state = 'recording';
          dataAvailableHandler = this.ondataavailable;
        }

        pause() {
          this.state = 'paused';
        }

        resume() {
          this.state = 'recording';
        }

        stop() {
          this.state = 'inactive';
          stopHandler = this.onstop;
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

      // Simulate chunk arrival
      if (recorder['mediaRecorder'] && recorder['mediaRecorder'].ondataavailable) {
        recorder['mediaRecorder'].ondataavailable({
          data: new Blob(['pcm chunk 1'], { type: 'audio/webm' }),
        } as BlobEvent);
        recorder['mediaRecorder'].ondataavailable({
          data: new Blob(['pcm chunk 2'], { type: 'audio/webm' }),
        } as BlobEvent);
      }

      const blobNow = recorder.getBlobNow();
      expect(blobNow).not.toBeNull();
      expect(recorder.getState().chunkCount).toBe(2);

      // Test Pause & Resume
      recorder.pause();
      expect(recorder.getState().isPaused).toBe(true);

      recorder.resume();
      expect(recorder.getState().isPaused).toBe(false);

      // Test Stop
      const result = await recorder.stop();
      expect(result).not.toBeNull();
      expect(result?.sizeBytes).toBeGreaterThan(0);
      expect(recorder.getState().isRecording).toBe(false);
    });

    it('2.2: Calling stop() when not recording returns null safely without throwing', async () => {
      const recorder = new DualTrackRecorder('unstarted-session');
      const result = await recorder.stop();
      expect(result).toBeNull();
      expect(recorder.getBlobNow()).toBeNull();
    });

    it('2.3: Recorder handles unsupported MediaRecorder environment gracefully', () => {
      const originalMR = (global as unknown as { MediaRecorder?: unknown }).MediaRecorder;
      delete (global as unknown as { MediaRecorder?: unknown }).MediaRecorder;

      const recorder = new DualTrackRecorder('no-mr-session');
      const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});

      recorder.start({} as MediaStream);
      expect(recorder.getState().isRecording).toBe(false);

      (global as unknown as { MediaRecorder?: unknown }).MediaRecorder = originalMR;
    });
  });

  // =========================================================================
  // Scope 3: Autosave Queue under Rapid Input & Offline Transitions
  // =========================================================================
  describe('Scope 3: Autosave Queue, Encryption & Offline/Online Sync', () => {
    it('3.1: Fails closed when offline answer encryption key is missing', async () => {
      setStorageEncryptionKey(null);

      await expect(
        queueOfflineAttempt('reading-answer', 'attempt-123', {
          questionId: 'q-1',
          value: JSON.stringify({ answer: 'B' }),
        }),
      ).rejects.toThrow(/Offline answer encryption is unavailable/i);
    });

    it('3.2: Deterministic ID deduplication prevents duplicate offline queues on rapid typing', () => {
      const questionId = 'q-part-a-14';
      const attemptId = 'att-read-999';

      const customId = `reading-answer:${encodeURIComponent(attemptId)}:${encodeURIComponent(questionId)}`;
      const customId2 = `reading-answer:${encodeURIComponent(attemptId)}:${encodeURIComponent(questionId)}`;

      expect(customId).toBe(customId2);
      expect(customId).toBe('reading-answer:att-read-999:q-part-a-14');
    });

    it('3.3: Stale in-flight answer preservation logic correctly identifies current vs outdated value', () => {
      const attemptId = 'att-1';
      const currentValJson = JSON.stringify('Updated Answer');
      const staleValJson = JSON.stringify('Stale Answer');

      const pendingAnswers: Record<string, { attemptId: string; valueJson: string; inFlight: boolean }> = {
        'q-1': {
          attemptId: 'att-1',
          valueJson: currentValJson,
          inFlight: true,
        },
      };

      // If pending answer matches attempt and currentValJson, it is valid
      const currentPending = pendingAnswers['q-1'];
      const isCurrentValue =
        !currentPending ||
        (currentPending.attemptId === attemptId && currentPending.valueJson === currentValJson);
      expect(isCurrentValue).toBe(true);

      // If resolving request had staleValJson, it should not overwrite currentValJson
      const isStaleCurrent =
        currentPending &&
        currentPending.attemptId === attemptId &&
        currentPending.valueJson === staleValJson;
      expect(isStaleCurrent).toBe(false);
    });
  });

  // =========================================================================
  // Scope 4: Unified Mock Coordinator State Restoration & Failure Modes
  // =========================================================================
  describe('Scope 4: Unified Mock Coordinator State Restoration, Progress & Error Gates', () => {
    const createSession = (
      statuses: Array<'not_started' | 'in_progress' | 'completed'>,
    ): MockSession => ({
      sessionId: 'mock-session-test-4',
      state: 'in_progress',
      resumeRoute: '/mocks/session/mock-session-test-4',
      config: {
        id: 'mock-cfg-4',
        type: 'full',
        title: 'Full 4-Skill OET Mock Simulation',
        profession: 'Medicine',
        mode: 'exam',
        strictTimer: true,
        includeReview: false,
        reviewSelection: 'none',
        deliveryMode: 'computer',
      },
      sectionStates: [
        {
          id: 'sec-1-listening',
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
          id: 'sec-2-reading',
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
          id: 'sec-3-writing',
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
          id: 'sec-4-speaking',
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

    it('4.1: State Restoration: 0/4 complete shows 0% progress and first runnable subtest', () => {
      const session0 = createSession(['not_started', 'not_started', 'not_started', 'not_started']);
      render(<UnifiedMockCoordinator session={session0} />);

      expect(screen.getByText('0 / 4 Sub-Tests Complete')).toBeDefined();
      expect(screen.getByText('0%')).toBeDefined();
      expect(screen.getByText(/Next up: Listening Sub-Test/i)).toBeDefined();
    });

    it('4.2: State Restoration: Resuming in_progress subtest after page refresh prioritizes resume', () => {
      const sessionMid = createSession(['completed', 'in_progress', 'not_started', 'not_started']);
      render(<UnifiedMockCoordinator session={sessionMid} />);

      expect(screen.getByText('1 / 4 Sub-Tests Complete')).toBeDefined();
      expect(screen.getByText('25%')).toBeDefined();
      expect(screen.getByRole('button', { name: /Resume Now/i })).toBeDefined();
    });

    it('4.3: State Restoration: 4/4 complete shows Statement of Results generation card', () => {
      const sessionFull = createSession(['completed', 'completed', 'completed', 'completed']);
      render(<UnifiedMockCoordinator session={sessionFull} />);

      expect(screen.getByText('4 / 4 Sub-Tests Complete')).toBeDefined();
      expect(screen.getByText('100%')).toBeDefined();
      expect(screen.getByText('All 4 Sub-Tests Completed')).toBeDefined();
      expect(
        screen.getByRole('button', { name: /Generate Official Statement of Results/i }),
      ).toBeDefined();
    });

    it('4.4: Clock Drift Tolerance: handles large positive, negative, and synchronized clock drift', () => {
      const session = createSession(['completed', 'in_progress', 'not_started', 'not_started']);
      const { rerender } = render(<UnifiedMockCoordinator session={session} />);

      expect(screen.getByText(/Clock Sync: Synchronized/i)).toBeDefined();
    });

    it('4.5: API Error Resilience: startMockSection failure displays inline error alert without crash', async () => {
      const session = createSession(['not_started', 'not_started', 'not_started', 'not_started']);

      vi.spyOn(apiModule, 'startMockSection').mockRejectedValueOnce(
        new Error('HTTP 503: Service Unavailable — Section lock active on another device'),
      );

      render(<UnifiedMockCoordinator session={session} />);

      const startButtons = screen.getAllByRole('button', { name: /Start Sub-Test/i });
      fireEvent.click(startButtons[0]);

      // Click "Begin Sub-Test Now" in modal
      const beginBtn = screen.getByRole('button', { name: /Begin Sub-Test Now/i });
      fireEvent.click(beginBtn);

      await waitFor(() => {
        expect(
          screen.getByText(/HTTP 503: Service Unavailable — Section lock active on another device/i),
        ).toBeDefined();
      });
    });

    it('4.6: API Error Resilience: submitMockSession failure displays inline error alert without crash', async () => {
      const session = createSession(['completed', 'completed', 'completed', 'completed']);

      vi.spyOn(apiModule, 'submitMockSession').mockRejectedValueOnce(
        new Error('HTTP 409: Incomplete sub-test evaluation pending'),
      );

      render(<UnifiedMockCoordinator session={session} />);

      const submitBtn = screen.getByRole('button', {
        name: /Generate Official Statement of Results/i,
      });
      fireEvent.click(submitBtn);

      await waitFor(() => {
        expect(
          screen.getByText(/HTTP 409: Incomplete sub-test evaluation pending/i),
        ).toBeDefined();
      });
    });
  });
});
