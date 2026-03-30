import type { UseFormReturn } from 'react-hook-form';
import CountryCodeSelect from '@/components/auth/country-code-select';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import type { SignupPayloadFormValues } from '@/lib/auth/schemas';
import { RegisterErrorText } from './register-error-text';

interface RegisterPersonalStepProps {
  form: UseFormReturn<SignupPayloadFormValues>;
  mobileLocalNumber: string;
  onCountryCodeChange: (value: string) => void;
  onMobileLocalNumberChange: (value: string) => void;
  selectedCountryCode: string;
}

export function RegisterPersonalStep({
  form,
  mobileLocalNumber,
  onCountryCodeChange,
  onMobileLocalNumberChange,
  selectedCountryCode,
}: RegisterPersonalStepProps) {
  const {
    formState: { errors },
    register,
  } = form;

  return (
    <>
      <div className={styles.gridTwo}>
        <div className={styles.field}>
          <label htmlFor="firstName">First Name</label>
          <input
            id="firstName"
            className={styles.input}
            placeholder="Aisha"
            autoComplete="given-name"
            {...register('firstName')}
          />
          <RegisterErrorText message={errors.firstName?.message} />
        </div>
        <div className={styles.field}>
          <label htmlFor="lastName">Last Name</label>
          <input
            id="lastName"
            className={styles.input}
            placeholder="Khan"
            autoComplete="family-name"
            {...register('lastName')}
          />
          <RegisterErrorText message={errors.lastName?.message} />
        </div>
      </div>

      <div className={styles.field}>
        <label htmlFor="email">Email Address</label>
        <input
          id="email"
          type="email"
          className={styles.input}
          placeholder="name@example.com"
          autoComplete="email"
          inputMode="email"
          spellCheck={false}
          {...register('email')}
        />
        <RegisterErrorText message={errors.email?.message} />
      </div>

      <div className={styles.field}>
        <label htmlFor="mobileNumberLocal">Mobile Number</label>
        <div className={styles.inputGroup}>
          <CountryCodeSelect
            inputId="mobile-country-code"
            value={selectedCountryCode}
            onChange={(option) => onCountryCodeChange(option.value)}
          />
          <input
            id="mobileNumberLocal"
            className={styles.input}
            placeholder="3001234567"
            value={mobileLocalNumber}
            autoComplete="tel-national"
            inputMode="numeric"
            onChange={(event) => onMobileLocalNumberChange(event.target.value.replace(/\D/g, ''))}
          />
        </div>
        <p className={styles.fieldHint}>
          Search by country, see the flag, and keep the dial code attached.
        </p>
        <RegisterErrorText message={errors.mobileNumber?.message} />
      </div>
    </>
  );
}
