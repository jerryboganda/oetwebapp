import type { UseFormReturn } from 'react-hook-form';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import type { SignupPayloadFormValues } from '@/lib/auth/schemas';
import type { SignupExamType, SignupProfession } from '@/lib/types/auth';
import { RegisterErrorText } from './register-error-text';
import { TARGET_COUNTRY_OPTIONS } from './target-countries';

interface RegisterEnrollmentStepProps {
  examTypes: SignupExamType[];
  filteredProfessions: SignupProfession[];
  form: UseFormReturn<SignupPayloadFormValues>;
}

/**
 * Step 2 of the legacy register wizard.
 *
 * Per PRD Phase 2 §1 the Session select, Session summary card, and the
 * "Published Billing Plans" preview have been removed. The country dropdown
 * is now a fixed mandatory list ({@link ./target-countries.ts}) rather than
 * being derived from the signup catalog.
 */
export function RegisterEnrollmentStep({
  examTypes,
  filteredProfessions,
  form,
}: RegisterEnrollmentStepProps) {
  const {
    formState: { errors },
    register,
  } = form;

  return (
    <>
      <div className={styles.gridTwo}>
        <div className={styles.field}>
          <label htmlFor="examTypeId">Exam Type</label>
          <select
            id="examTypeId"
            className={styles.select}
            {...register('examTypeId')}
          >
            <option value="">Select exam type</option>
            {examTypes.map((item) => (
              <option key={item.id} value={item.id}>
                {item.label}
              </option>
            ))}
          </select>
          <RegisterErrorText message={errors.examTypeId?.message} />
        </div>
        <div className={styles.field}>
          <label htmlFor="professionId">Current Profession</label>
          <select
            id="professionId"
            className={styles.select}
            {...register('professionId')}
          >
            <option value="">Select profession</option>
            {filteredProfessions.map((item) => (
              <option key={item.id} value={item.id}>
                {item.label}
              </option>
            ))}
          </select>
          <RegisterErrorText message={errors.professionId?.message} />
        </div>
      </div>

      <div className={styles.field}>
        <label htmlFor="countryTarget">Target Country</label>
        <select
          id="countryTarget"
          className={styles.select}
          required
          aria-required="true"
          {...register('countryTarget')}
        >
          <option value="">Select target country</option>
          {TARGET_COUNTRY_OPTIONS.map((item) => (
            <option key={item} value={item}>
              {item}
            </option>
          ))}
        </select>
        <RegisterErrorText message={errors.countryTarget?.message} />
      </div>
    </>
  );
}
