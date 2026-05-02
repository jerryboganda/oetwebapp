import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
const { mockUseAuth } = vi.hoisted(() => ({
  mockUseAuth: vi.fn(),
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
        countryTargets: [],
        examTypeIds: ['oet'],
        description: 'Nursing pathway',
      },
    ],
    externalAuthProviders: ['linkedin'],
  }),
  buildExternalAuthStartHref: vi.fn((_provider: string) => '#'),
  registerLearner: vi.fn(),
}));

import RegisterPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

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
    renderWithRouter(<RegisterPage />);

    expect(await screen.findByRole('heading', { name: /register your account/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /next step/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /sign in instead/i })).toBeInTheDocument();
  });

  it('uses the fixed mandatory target-country list without session or billing controls', async () => {
    const user = userEvent.setup();
    renderWithRouter(<RegisterPage />);

    await user.type(await screen.findByLabelText(/first name/i), 'Aisha');
    await user.type(screen.getByLabelText(/last name/i), 'Khan');
    await user.type(screen.getByLabelText(/email address/i), 'aisha@example.test');
    await user.type(screen.getByLabelText(/mobile number/i), '3001234567');
    await user.click(screen.getByRole('button', { name: /next step/i }));

    const targetCountry = await screen.findByRole('combobox', { name: /target country/i });
    expect(targetCountry).toBeRequired();
    expect(targetCountry).toHaveAttribute('aria-required', 'true');
    expect(within(targetCountry).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Select target country',
      'United Kingdom',
      'Ireland',
      'Scotland',
      'USA',
      'Australia',
      'New Zealand',
      'Canada',
      'Gulf Countries',
      'Other Countries',
    ]);

    expect(screen.queryByRole('combobox', { name: /^session$/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/session summary/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/published billing plans/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/exam, profession, session/i)).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /next step/i }));
    expect(await screen.findByText(/select your target country/i)).toBeInTheDocument();

    await user.selectOptions(targetCountry, 'USA');
    await user.click(screen.getByRole('button', { name: /next step/i }));
    expect(await screen.findByText('USA')).toBeInTheDocument();
    expect(screen.queryByText(/session updates/i)).not.toBeInTheDocument();
  });
});
