import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockUseAuth } = vi.hoisted(() => ({
  mockUseAuth: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({
    replace: vi.fn(),
  }),
  useSearchParams: () => ({
    get: (_key: string) => null,
  }),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock('@/lib/auth-client', () => ({
  fetchSignupCatalog: vi.fn().mockResolvedValue({
    examTypes: [
      { id: 'oet', label: 'OET', code: 'OET', description: 'Occupational English Test' },
    ],
    professions: [
      {
        id: 'nursing',
        label: 'Nursing',
        countryTargets: ['Australia'],
        examTypeIds: ['oet'],
        description: 'Nursing pathway',
      },
    ],
    sessions: [
      {
        id: 'session-oet-nursing-apr',
        name: 'April OET Nursing',
        examTypeId: 'oet',
        professionIds: ['nursing'],
        priceLabel: '$100',
        startDate: '01 Apr 2026',
        endDate: '30 Apr 2026',
        deliveryMode: 'Online',
        capacity: 50,
        seatsRemaining: 12,
      },
    ],
    externalAuthProviders: ['linkedin'],
  }),
  buildExternalAuthStartHref: vi.fn((_provider: string) => '#'),
  registerLearner: vi.fn(),
}));

import RegisterPage from './page';

describe('RegisterPage', () => {
  beforeEach(() => {
    mockUseAuth.mockReturnValue({
      loading: false,
      isAuthenticated: false,
      user: null,
      pendingMfaChallenge: null,
    });
  });

  it('renders the dedicated learner registration screen', async () => {
    render(<RegisterPage />);

    expect(await screen.findByRole('heading', { name: /register your account/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /next step/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /sign in instead/i })).toBeInTheDocument();
  });
});
