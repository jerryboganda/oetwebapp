import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import CatalogPage from './page';
import { fetchPublicCatalog } from '@/lib/api';

vi.mock('@/lib/api', () => ({
  fetchPublicCatalog: vi.fn(),
  fetchMyEntitlementSnapshot: vi.fn(),
}));

const mockFetchPublicCatalog = vi.mocked(fetchPublicCatalog);

describe('CatalogPage', () => {
  beforeEach(() => {
    mockFetchPublicCatalog.mockReset();
  });

  it('renders the redesigned storefront with package and add-on pricing', async () => {
    mockFetchPublicCatalog.mockResolvedValue({
      currency: 'GBP',
      plans: [
        {
          code: 'full-condensed-medicine',
          name: 'Full Condensed Recorded OET Course - Medicine',
          description: 'Complete recorded course.',
          price: 100,
          originalPrice: null,
          currency: 'GBP',
          accessDurationDays: 180,
          productCategory: 'full_course',
          profession: 'medicine',
          writingAddonsEnabled: true,
          speakingAddonsEnabled: false,
          speakingPracticeAccessEnabled: true,
          tutorBookDiscountEnabled: true,
          bundledWritingAssessments: 5,
          bundledSpeakingSessions: 1,
          bundledAiCredits: 5,
          bundledTutorBook: false,
          bundledBasicEnglish: false,
          dashboardModules: ['Listening', 'Reading'],
          displayOrder: 100,
        },
      ],
      addOns: [
        {
          code: 'tutor-book-addon',
          name: 'The Tutor Book add-on',
          description: 'Discounted Tutor Book.',
          price: 32,
          originalPrice: 45,
          currency: 'GBP',
          addonKind: 'tutor_book',
          eligibilityFlag: 'tutor_book_discount',
          lettersGranted: 0,
          sessionsGranted: 0,
          isStackable: false,
          displayOrder: 10,
        },
      ],
    });

    const { container } = render(<CatalogPage />);

    await waitFor(() => expect(mockFetchPublicCatalog).toHaveBeenCalledTimes(1));
    expect((await screen.findAllByText('Full Condensed Recorded OET Course - Medicine')).length).toBeGreaterThan(0);
    expect(screen.getByText('OET with Dr. Ahmed Hesham · 2026 Portfolio')).toBeInTheDocument();
    expect(screen.getAllByText('Tutor Book £32').length).toBeGreaterThan(0);
    expect(screen.getAllByText('£100').length).toBeGreaterThan(0);
    expect(screen.getAllByText('£32').length).toBeGreaterThan(0);
    expect(container.textContent).toContain('£45');
    expect(container.textContent).not.toMatch(/Â|â|�/);
  });
});
