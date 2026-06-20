import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useForm } from 'react-hook-form';
import { RegisterEnrollmentStep } from '@/components/auth/register/register-enrollment-step';
import { RegisterStepProgress } from '@/components/auth/register/register-step-progress';
import type { SignupPayloadFormValues } from '@/lib/auth/schemas';
vi.mock('@/lib/auth-client', () => ({
  fetchSignupCatalog: vi.fn().mockResolvedValue({
    examTypes: [
      { id: 'oet', label: 'OET', code: 'OET', description: 'Occupational English Test' },
      { id: 'ielts', label: 'IELTS', code: 'IELTS', description: 'IELTS preparation' },
    ],
    professions: [
      {
        id: 'nursing',
        label: 'Nursing',
        countryTargets: [],
        examTypeIds: ['oet'],
        description: 'Nursing pathway',
      },
      {
        id: 'academic-english',
        label: 'Academic / General English',
        countryTargets: [],
        examTypeIds: ['ielts'],
        description: 'IELTS pathway',
      },
    ],
    targetCountryOptions: ['Canada', 'Qatar'],
  }),
  buildExternalAuthStartHref: vi.fn((_provider: string) => '#'),
  registerLearner: vi.fn(),
}));

import { RegisterForm } from '../register-form';
import { renderWithRouter } from '@/tests/test-utils';

function EnrollmentHarness() {
  const form = useForm<SignupPayloadFormValues>({
    defaultValues: {
      countryTarget: '',
      examTypeId: 'oet',
      professionId: 'nursing',
    },
  });

  return (
    <>
      <RegisterStepProgress step={2} />
      <RegisterEnrollmentStep
        availableCountries={['United Kingdom']}
        examTypes={[{ id: 'oet', label: 'OET', code: 'OET', description: 'Occupational English Test' }]}
        filteredProfessions={[{ id: 'nursing', label: 'Nursing', countryTargets: [], examTypeIds: ['oet'], description: 'Nursing pathway' }]}
        form={form}
      />
    </>
  );
}

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

  it('keeps the streamlined enrollment step free of session wording and filters countries by profession catalog', () => {
    renderWithRouter(<EnrollmentHarness />);

    expect(screen.getByText(/exam, profession, country/i)).toBeInTheDocument();
    expect(screen.queryByText(/exam, profession, session/i)).not.toBeInTheDocument();

    const targetCountry = screen.getByRole('combobox', { name: /target country/i });
    expect(targetCountry).toBeRequired();
    expect(within(targetCountry).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Select target country',
      'United Kingdom',
    ]);
    expect(screen.queryByRole('combobox', { name: /^session$/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/published billing plans/i)).not.toBeInTheDocument();
  });

  it('uses backend-served target countries when the profession has no country restriction', async () => {
    const user = userEvent.setup();
    renderWithRouter(<RegisterForm />);

    await user.type(await screen.findByLabelText(/first name/i), 'Aisha');
    await user.type(screen.getByLabelText(/last name/i), 'Khan');
    await user.type(screen.getByLabelText(/email address/i), 'aisha-countries@example.test');
    await user.type(screen.getByLabelText(/mobile number/i), '3001234567');
    await user.click(screen.getByRole('button', { name: /next step/i }));

    const targetCountry = await screen.findByRole('combobox', { name: /target country/i });
    expect(within(targetCountry).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Select target country',
      'Canada',
      'Qatar',
    ]);
  });
});
