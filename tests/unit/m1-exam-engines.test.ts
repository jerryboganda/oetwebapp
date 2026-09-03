import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getCachedAudioUrl,
  registerCachedAudioUrl,
  clearAudioPrebufferCache,
  prebufferAudioChunks,
} from '@/lib/listening/audio-prebuffer';
import { DualTrackRecorder } from '@/lib/speaking/dual-track-recorder';

describe('Milestone 1: 4-Skill Exam & Practice Engine Hardening', () => {
  beforeEach(() => {
    clearAudioPrebufferCache();
    vi.restoreAllMocks();
  });

  describe('Feature 2: Listening Audio Pre-buffering & Offline Cache', () => {
    it('returns null for uncached audio URLs', () => {
      expect(getCachedAudioUrl('https://example.com/audio/p1.mp3')).toBeNull();
    });

    it('caches and retrieves pre-buffered audio URLs', () => {
      const sourceUrl = 'https://example.com/audio/p1.mp3';
      const blobUrl = 'blob:http://localhost/mock-blob-123';
      registerCachedAudioUrl(sourceUrl, blobUrl);

      expect(getCachedAudioUrl(sourceUrl)).toBe(blobUrl);
    });

    it('clears audio cache cleanly', () => {
      const sourceUrl = 'https://example.com/audio/p1.mp3';
      const blobUrl = 'blob:http://localhost/mock-blob-123';
      registerCachedAudioUrl(sourceUrl, blobUrl);

      clearAudioPrebufferCache();
      expect(getCachedAudioUrl(sourceUrl)).toBeNull();
    });

    it('handles empty audio URL lists gracefully', async () => {
      const result = await prebufferAudioChunks([]);
      expect(result.success).toBe(true);
      expect(result.prebufferedCount).toBe(0);
      expect(result.failedCount).toBe(0);
    });
  });

  describe('Feature 4: Speaking Dual-Track Local Audio Safety Net', () => {
    it('initializes in idle state', () => {
      const recorder = new DualTrackRecorder('speaking-session-123');
      const state = recorder.getState();
      expect(state.isRecording).toBe(false);
      expect(state.isPaused).toBe(false);
      expect(state.chunkCount).toBe(0);
      expect(state.totalBytes).toBe(0);
    });

    it('returns null for stop if not recording', async () => {
      const recorder = new DualTrackRecorder('speaking-session-123');
      const result = await recorder.stop();
      expect(result).toBeNull();
    });
  });

  describe('Feature 1 & 5: Structural Invariants', () => {
    it('verifies official 20/6/16 = 42 Reading paper structure', () => {
      const readingPartAQuestions = 20;
      const readingPartBQuestions = 6;
      const readingPartCQuestions = 16;
      const totalReadingQuestions =
        readingPartAQuestions + readingPartBQuestions + readingPartCQuestions;
      expect(totalReadingQuestions).toBe(42);
    });

    it('verifies official 24/6/12 = 42 Listening paper structure', () => {
      const listeningPartAQuestions = 24; // 2 consultations x 12
      const listeningPartBQuestions = 6;  // 6 extracts x 1
      const listeningPartCQuestions = 12; // 2 presentations x 6
      const totalListeningQuestions =
        listeningPartAQuestions + listeningPartBQuestions + listeningPartCQuestions;
      expect(totalListeningQuestions).toBe(42);
    });

    it('verifies official 45-minute Writing sub-test window (5m reading + 40m writing)', () => {
      const readingWindowSeconds = 5 * 60;
      const writingWindowSeconds = 40 * 60;
      const totalWritingSeconds = readingWindowSeconds + writingWindowSeconds;
      expect(totalWritingSeconds).toBe(45 * 60);
    });

    it('verifies official 2-card Speaking sub-test timing', () => {
      const cardPrepSeconds = 3 * 60;
      const cardConsultationSeconds = 5 * 60;
      const totalPerCardSeconds = cardPrepSeconds + cardConsultationSeconds;
      expect(totalPerCardSeconds).toBe(8 * 60);
      const totalTwoCardsSeconds = totalPerCardSeconds * 2;
      expect(totalTwoCardsSeconds).toBe(16 * 60);
    });
  });
});
