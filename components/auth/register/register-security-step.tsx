import {
  IconBriefcase,
  IconMail,
  IconMapPin,
} from '@tabler/icons-react';
import type { UseFormReturn } from 'react-hook-form';
import { PasswordField } from '@/components/auth/password-field';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import type { SignupPayloadFormValues } from '@/lib/auth/schemas';
import type { SignupExamType, SignupProfession } from '@/lib/types/auth';
import { RegisterErrorText } from './register-error-text';

interface RegisterSecurityStepProps {
  examTypes: SignupExamType[];
  form: UseFormReturn<SignupPayloadFormValues>;
  selectedExamTypeId: string;
  selectedProfession?: SignupProfession;
}

export function RegisterSecurityStep({
  examTypes,
  form,
  selectedExamTypeId,
  selectedProfession,
}: RegisterSecurityStepProps) {
  const {
    formState: { errors },
    getValues,
    register,
  } = form;

  return (
    <>
      <PasswordField
        id="password"
        label="Password"
        placeholder="Create password"
        autoComplete="new-password"
        {...register('password')}
      />
      <RegisterErrorText message={errors.password?.message} />

      <PasswordField
        id="confirmPassword"
        label="Confirm Password"
        placeholder="Repeat password"
        autoComplete="new-password"
        {...register('confirmPassword')}
      />
      <RegisterErrorText message={errors.confirmPassword?.message} />

      <label className={styles.checkbox} htmlFor="agreeToTerms">
        <input
          id="agreeToTerms"
          type="checkbox"
          {...register('agreeToTerms')}
        />
        <span>I agree to the Terms and Conditions</span>
      </label>
      <RegisterErrorText message={errors.agreeToTerms?.message} />

      <label className={styles.checkbox} htmlFor="agreeToPrivacy">
        <input
          id="agreeToPrivacy"
          type="checkbox"
          {...register('agreeToPrivacy')}
        />
        <span>I agree to the privacy policy and learner data notice</span>
      </label>
      <RegisterErrorText message={errors.agreeToPrivacy?.message} />

      <label className={styles.checkbox} htmlFor="marketingOptIn">
        <input
          id="marketingOptIn"
          type="checkbox"
          {...register('marketingOptIn')}
        />
        <span>Send me preparation reminders and platform updates</span>
      </label>

      <div className={styles.summaryCard}>
        <h4>Enrollment Summary</h4>
        <div className={styles.summaryList}>
          <div className={styles.summaryItem}>
            <span className={styles.summaryIcon}>
              <IconMail size={14} />
            </span>
            <p>
              {getValues('firstName')} {getValues('lastName')} · {getValues('email')}
            </p>
          </div>
          <div className={styles.summaryItem}>
            <span className={styles.summaryIcon}>
              <IconBriefcase size={14} />
            </span>
            <p>
              {examTypes.find((item) => item.id === selectedExamTypeId)?.label ?? 'Exam'} ·{' '}
              {selectedProfession?.label ?? 'Profession'}
            </p>
          </div>
          <div className={styles.summaryItem}>
            <span className={styles.summaryIcon}>
              <IconMapPin size={14} />
            </span>
            <p>{getValues('countryTarget') || 'Target country not selected'}</p>
          </div>
        </div>
      </div>
    </>
  );
}
