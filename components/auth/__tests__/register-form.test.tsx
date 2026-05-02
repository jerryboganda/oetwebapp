import { screen, within } from '@testing-library/react';
import { useForm } from 'react-hook-form';
import { RegisterEnrollmentStep } from '@/components/auth/register/register-enrollment-step';
import { RegisterStepProgress } from '@/components/auth/register/register-step-progress';
import type { SignupPayloadFormValues } from '@/lib/auth/schemas';
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

  it('keeps the streamlined enrollment step free of session wording and catalog-derived countries', () => {
    renderWithRouter(<EnrollmentHarness />);

    expect(screen.getByText(/exam, profession, country/i)).toBeInTheDocument();
    expect(screen.queryByText(/exam, profession, session/i)).not.toBeInTheDocument();

    const targetCountry = screen.getByRole('combobox', { name: /target country/i });
    expect(targetCountry).toBeRequired();
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
    expect(screen.queryByText(/published billing plans/i)).not.toBeInTheDocument();
  });
});
