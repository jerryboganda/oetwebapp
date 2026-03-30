import styles from '@/components/auth/auth-screen-shell.module.scss';

export function RegisterErrorText({ message }: { message?: string }) {
  return message ? (
    <p className={styles.fieldHint} style={{ color: '#c23d69' }}>
      {message}
    </p>
  ) : null;
}
