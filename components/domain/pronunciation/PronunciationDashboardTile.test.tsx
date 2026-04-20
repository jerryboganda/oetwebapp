import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { PronunciationDashboardTile } from '@/components/domain/pronunciation/PronunciationDashboardTile';

vi.mock('@/lib/api', () => ({
  fetchPronunciationProfile: vi.fn(),
  fetchPronunciationDueDrills: vi.fn(),
}));

import { fetchPronunciationProfile, fetchPronunciationDueDrills } from '@/lib/api';

describe('PronunciationDashboardTile', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the empty-state hint when the learner has no assessments yet', async () => {
    (fetchPronunciationProfile as unknown as ReturnType<typeof vi.fn>).mockResolvedValue({
      overallScore: 0,
      projectedSpeakingScaled: 0,
      projectedSpeakingGrade: 'E',
      projectedSpeakingPassed: false,
      totalAssessments: 0,
      weakPhonemes: [],
    });
    (fetchPronunciationDueDrills as unknown as ReturnType<typeof vi.fn>).mockResolvedValue([]);

    render(<PronunciationDashboardTile />);
    await waitFor(() =>
      expect(screen.getByText(/record your first drill/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole('link', { name: /open pronunciation practice/i })).toHaveAttribute('href', '/pronunciation');
  });

  it('shows projected speaking band + weak phonemes when assessments exist', async () => {
    (fetchPronunciationProfile as unknown as ReturnType<typeof vi.fn>).mockResolvedValue({
      overallScore: 78,
      projectedSpeakingScaled: 390,
      projectedSpeakingGrade: 'B',
      projectedSpeakingPassed: true,
      totalAssessments: 4,
      weakPhonemes: [
        { phonemeCode: 'θ', averageScore: 52, attemptCount: 3 },
        { phonemeCode: 'v', averageScore: 58, attemptCount: 2 },
      ],
    });
    (fetchPronunciationDueDrills as unknown as ReturnType<typeof vi.fn>).mockResolvedValue([
      { id: 'pd-001', label: 't', targetPhoneme: 'θ', difficulty: 'medium' },
    ]);

    render(<PronunciationDashboardTile />);
    await waitFor(() =>
      expect(screen.getByText(/390\/500 · Grade B/)).toBeInTheDocument(),
    );
    expect(screen.getByText(/pass/i)).toBeInTheDocument();
    expect(screen.getByText('/θ/')).toBeInTheDocument();
    expect(screen.getByText('/v/')).toBeInTheDocument();
    expect(screen.getByText(/drill.*due today/i)).toBeInTheDocument();
  });

  it('handles API failure silently by falling back to the empty state', async () => {
    (fetchPronunciationProfile as unknown as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('down'));
    (fetchPronunciationDueDrills as unknown as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('down'));
    render(<PronunciationDashboardTile />);
    await waitFor(() =>
      expect(screen.getByText(/record your first drill/i)).toBeInTheDocument(),
    );
  });
});
