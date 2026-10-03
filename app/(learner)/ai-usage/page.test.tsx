import { render, screen } from '@testing-library/react';

const { fetchLearnerAiUsage, fetchMyForecast } = vi.hoisted(() => ({
  fetchLearnerAiUsage: vi.fn(),
  fetchMyForecast: vi.fn(),
}));

// Only the learner's own usage and forecast: reaching for anything else (the
// internal churn-risk scorer) is not on this mock and fails the test.
vi.mock('@/lib/api', () => ({ fetchLearnerAiUsage, fetchMyForecast }));

import LearnerAiUsagePage from './page';

describe('AI usage page', () => {
  it("renders the learner's usage and forecast without the churn-risk scorer", async () => {
    fetchLearnerAiUsage.mockResolvedValue({
      from: '2026-09-02', to: '2026-10-02', totalCalls: 12, totalTokens: 0, totalCostUsd: 0, failedCalls: 0,
      creditsUsed: 7, walletBalance: 40,
      byFeature: [{ featureCode: 'writing.grade', calls: 12, totalTokens: 0, costUsd: 0 }],
      daily: [{ day: '2026-10-01', calls: 12, totalTokens: 0, costUsd: 0 }],
      forecastCalls30d: 0, forecastCredits30d: 0, forecastCostUsd30d: 0, suggestedTopUpCredits: 0,
    });
    fetchMyForecast.mockResolvedValue({ forecastCalls: 30, forecastCredits: 18, suggestedTopUpCredits: 0 });

    render(<LearnerAiUsagePage />);

    expect(await screen.findByText('writing.grade')).toBeInTheDocument();
    expect(screen.getByText('Forecast: next 30 days')).toBeInTheDocument();
    expect(screen.queryByText(/account health|risk score/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/failed to load/i)).not.toBeInTheDocument();
  });

  it('never prints platform-only jev.* feature codes', async () => {
    fetchLearnerAiUsage.mockResolvedValue({
      from: '2026-09-02', to: '2026-10-02', totalCalls: 14, totalTokens: 0, totalCostUsd: 0, failedCalls: 0,
      creditsUsed: 7, walletBalance: 40,
      byFeature: [
        { featureCode: 'writing.grade', calls: 12, totalTokens: 0, costUsd: 0 },
        { featureCode: 'jev.writing.verify', calls: 2, totalTokens: 0, costUsd: 0 },
      ],
      daily: [{ day: '2026-10-01', calls: 14, totalTokens: 0, costUsd: 0 }],
      forecastCalls30d: 0, forecastCredits30d: 0, forecastCostUsd30d: 0, suggestedTopUpCredits: 0,
    });
    fetchMyForecast.mockResolvedValue({ forecastCalls: 30, forecastCredits: 18, suggestedTopUpCredits: 0 });

    const { container } = render(<LearnerAiUsagePage />);

    expect(await screen.findByText('writing.grade')).toBeInTheDocument();
    expect(container.textContent).not.toMatch(/jev/i);
  });

  it('shows the empty state when every feature row is platform-only', async () => {
    fetchLearnerAiUsage.mockResolvedValue({
      from: '2026-09-02', to: '2026-10-02', totalCalls: 0, totalTokens: 0, totalCostUsd: 0, failedCalls: 0,
      creditsUsed: 0, walletBalance: 40,
      byFeature: [{ featureCode: 'jev.development.triage', calls: 3, totalTokens: 0, costUsd: 0 }],
      daily: [],
      forecastCalls30d: 0, forecastCredits30d: 0, forecastCostUsd30d: 0, suggestedTopUpCredits: 0,
    });
    fetchMyForecast.mockResolvedValue({ forecastCalls: 0, forecastCredits: 0, suggestedTopUpCredits: 0 });

    const { container } = render(<LearnerAiUsagePage />);

    expect((await screen.findAllByText('No AI calls yet in this window.')).length).toBeGreaterThan(0);
    expect(container.textContent).not.toMatch(/jev/i);
  });
});
