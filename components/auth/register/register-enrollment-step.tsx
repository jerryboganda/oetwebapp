import type { UseFormReturn } from 'react-hook-form';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import type { SignupPayloadFormValues } from '@/lib/auth/schemas';
import type { SignupExamType, SignupProfession, SignupSession } from '@/lib/types/auth';
import { RegisterErrorText } from './register-error-text';

interface RegisterEnrollmentStepProps {
  availableCountries: string[];
  examTypes: SignupExamType[];
  filteredProfessions: SignupProfession[];
  filteredSessions: SignupSession[];
  form: UseFormReturn<SignupPayloadFormValues>;
  selectedSession?: SignupSession;
}

export function RegisterEnrollmentStep({
  availableCountries,
  examTypes,
  filteredProfessions,
  filteredSessions,
  form,
  selectedSession,
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
        <label htmlFor="sessionId">Session</label>
        <select
          id="sessionId"
          className={styles.select}
          {...register('sessionId')}
        >
          <option value="">Select session</option>
          {filteredSessions.map((item) => (
            <option key={item.id} value={item.id}>
              {item.name} · {item.priceLabel}
            </option>
          ))}
        </select>
        <RegisterErrorText message={errors.sessionId?.message} />
      </div>

      <div className={styles.field}>
        <label htmlFor="countryTarget">Target Country</label>
        <select
          id="countryTarget"
          className={styles.select}
          {...register('countryTarget')}
        >
          <option value="">Select target country</option>
          {availableCountries.map((item) => (
            <option key={item} value={item}>
              {item}
            </option>
          ))}
        </select>
        <RegisterErrorText message={errors.countryTarget?.message} />
      </div>

      <div className={styles.summaryCard}>
        <h4>Session Summary</h4>
        {selectedSession ? (
          <>
            <p>{selectedSession.name}</p>
            <p>
              {selectedSession.priceLabel} · {selectedSession.deliveryMode}
            </p>
            <p>
              {selectedSession.startDate} to {selectedSession.endDate}
            </p>
            <p>
              Seats left: {selectedSession.seatsRemaining}/{selectedSession.capacity}
            </p>
          </>
        ) : (
          <p>Select a session to preview the cohort summary.</p>
        )}
      </div>
    </>
  );
}
