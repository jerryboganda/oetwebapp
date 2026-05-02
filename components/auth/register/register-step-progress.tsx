import styles from '@/components/auth/auth-screen-shell.module.scss';

export const registerStepMeta = [
  { title: 'Personal', caption: 'Name, email, mobile' },
  { title: 'Enrollment', caption: 'Exam, profession, country' },
  { title: 'Security', caption: 'Password, consent, summary' },
] as const;

interface RegisterStepProgressProps {
  step: 1 | 2 | 3;
}

export function RegisterStepProgress({ step }: RegisterStepProgressProps) {
  return (
    <div className={styles.wizardProgress} aria-label="Signup progress">
      <div
        className={styles.wizardProgressBar}
        style={{
          transform: `scaleX(${(step - 1) / (registerStepMeta.length - 1)})`,
        }}
      />
      {registerStepMeta.map((item, index) => {
        const currentStep = (index + 1) as 1 | 2 | 3;
        const isActive = step === currentStep;
        const isComplete = step > currentStep;

        return (
          <div
            key={item.title}
            className={`${styles.stepNode} ${
              isActive ? styles.stepNodeActive : ''
            } ${isComplete ? styles.stepNodeComplete : ''}`}
          >
            <span className={styles.stepDot}>{currentStep}</span>
            <div className={styles.stepText}>
              <strong>{item.title}</strong>
              <span>{item.caption}</span>
            </div>
          </div>
        );
      })}
    </div>
  );
}
