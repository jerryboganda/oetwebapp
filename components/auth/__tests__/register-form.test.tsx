import { screen } from '@testing-library/react';
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
  }),
  buildExternalAuthStartHref: vi.fn((_provider: string) => '#'),
  registerLearner: vi.fn(),
}));

import { RegisterForm } from '../register-form';
import { renderWithRouter } from '@/tests/test-utils';

describe('RegisterForm', () => {
  it('uses meaningful autocomplete and input attributes on signup fields', async () => {
    renderWithRouter(<RegisterForm />);

    const firstNameInput = await screen.findByLabelText(/first name/i);
    const lastNameInput = screen.getByLabelText(/last name/i);
    const emailInput = screen.getByLabelText(/email address/i);
    const mobileInput = screen.getByLabelText(/mobile number/i);
    const countryCodeSelect = screen.getByRole('combobox', { name: /country calling code/i });

    expect(firstNameInput).toHaveAttribute('autocomplete', 'given-name');
    expect(lastNameInput).toHaveAttribute('autocomplete', 'family-name');

    expect(emailInput).toHaveAttribute('name', 'email');
    expect(emailInput).toHaveAttribute('autocomplete', 'email');
    expect(emailInput).toHaveAttribute('inputmode', 'email');
    expect(emailInput).toHaveAttribute('spellcheck', 'false');

    expect(mobileInput).toHaveAttribute('autocomplete', 'tel-national');
    expect(mobileInput).toHaveAttribute('inputmode', 'numeric');
    expect(countryCodeSelect).toBeInTheDocument();
  });
});
