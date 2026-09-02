import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import TutorWritingCalibrationPage from './page';
import { renderWithRouter } from '@/tests/test-utils';
import { apiClient } from '@/lib/api';

vi.mock('@/lib/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api')>();
  return {
    ...actual,
    apiClient: {
      ...actual.apiClient,
      get: vi.fn(),
    },
  };
});

describe('TutorWritingCalibrationPage', () => {
  it('renders calibration status and links to /expert/calibration when recalibration is required', async () => {
    vi.mocked(apiClient.get).mockResolvedValueOnce({
      tutorId: 'tutor-123',
      agreementCoefficient: 0.72,
      requiresRecalibration: true,
      lastCalibratedAt: '2026-03-01T00:00:00Z',
    });

    renderWithRouter(<TutorWritingCalibrationPage />, { pathname: '/tutor/writing/calibration' });

    const takeTestLink = await screen.findByRole('link', { name: /take calibration test/i });
    expect(takeTestLink).toBeInTheDocument();
    expect(takeTestLink).toHaveAttribute('href', '/expert/calibration');
  });
});
