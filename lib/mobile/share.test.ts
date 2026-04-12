import { describe, expect, it, vi, beforeEach } from 'vitest';

// ── Mock Capacitor Core ─────────────────────────────────────────

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    isNativePlatform: vi.fn(() => true),
    getPlatform: vi.fn(() => 'ios'),
  },
}));

// ── Mock Share Plugin ───────────────────────────────────────────

const mockShare = {
  canShare: vi.fn(),
  share: vi.fn(),
};

vi.mock('@capacitor/share', () => ({
  Share: mockShare,
}));

import {
  canShare,
  triggerShare,
  shareAppLink,
  shareAchievement,
  shareScore,
} from '@/lib/mobile/share';

describe('share', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('canShare', () => {
    it('returns true when native share is available', async () => {
      mockShare.canShare.mockResolvedValue({ value: true });
      const result = await canShare();
      expect(result).toBe(true);
    });

    it('returns false on error', async () => {
      mockShare.canShare.mockRejectedValue(new Error('fail'));
      const result = await canShare();
      expect(result).toBe(false);
    });
  });

  describe('triggerShare', () => {
    it('calls native share with provided options', async () => {
      mockShare.share.mockResolvedValue(undefined);
      const result = await triggerShare({
        title: 'Test',
        text: 'Hello',
        url: 'https://example.com',
      });
      expect(result).toEqual({ shared: true, platform: 'ios' });
      expect(mockShare.share).toHaveBeenCalledWith({
        title: 'Test',
        text: 'Hello',
        url: 'https://example.com',
        dialogTitle: undefined,
      });
    });

    it('returns shared:false on error (e.g. user cancelled)', async () => {
      mockShare.share.mockRejectedValue(new Error('Share cancelled'));
      const result = await triggerShare({ title: 'Test' });
      expect(result).toEqual({ shared: false, platform: 'ios' });
    });
  });

  describe('shareAppLink', () => {
    it('shares the app link with predefined text', async () => {
      mockShare.share.mockResolvedValue(undefined);
      const result = await shareAppLink();
      expect(result.shared).toBe(true);
      expect(mockShare.share).toHaveBeenCalledWith(
        expect.objectContaining({
          title: 'OET Prep Learner',
          url: 'https://app.oetwithdrhesham.co.uk',
        })
      );
    });
  });

  describe('shareAchievement', () => {
    it('shares an achievement with the title', async () => {
      mockShare.share.mockResolvedValue(undefined);
      await shareAchievement('First Mock Exam');
      expect(mockShare.share).toHaveBeenCalledWith(
        expect.objectContaining({
          text: expect.stringContaining('First Mock Exam'),
        })
      );
    });
  });

  describe('shareScore', () => {
    it('shares a score with subtest and percentage', async () => {
      mockShare.share.mockResolvedValue(undefined);
      await shareScore('Listening', 85);
      expect(mockShare.share).toHaveBeenCalledWith(
        expect.objectContaining({
          text: expect.stringContaining('85%'),
        })
      );
    });
  });
});
