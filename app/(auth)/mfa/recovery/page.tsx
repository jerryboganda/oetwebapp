'use client';

import { useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { IconArrowRight } from '@tabler/icons-react';
import { useAuth } from '@/contexts/auth-context';
import { resolvePostAuthDestination } from '@/lib/auth-routes';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import styles from '@/components/auth/auth-screen-shell.module.scss';

export default function MfaRecoveryPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { loading, pendingMfaChallenge, isAuthenticated, user, completeRecoveryChallenge } = useAuth();
  const nextHref = searchParams.get('next');

  const [recoveryCode, setRecoveryCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    if (loading) {
      return;
    }

    if (isAuthenticated && user) {
      router.replace(nextHref ?? '/');
      return;
    }

    if (!pendingMfaChallenge) {
      const nextQuery = nextHref ? `?next=${encodeURIComponent(nextHref)}` : '';
      router.replace(`/sign-in${nextQuery}`);
    }
  }, [isAuthenticated, loading, nextHref, pendingMfaChallenge, router, user]);

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmed = recoveryCode.trim();

    if (trimmed.length === 0) {
      setError('Enter a recovery code.');
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      const session = await completeRecoveryChallenge(trimmed);
      router.replace(resolvePostAuthDestination(session.currentUser, nextHref));
    } catch (submitError) {
      if (submitError && typeof submitError === 'object' && 'message' in submitError && typeof submitError.message === 'string') {
        setError(submitError.message);
      } else {
        setError('Unable to verify the recovery code.');
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <AuthScreenShell
      eyebrow="Account Recovery"
      title="Use a recovery code"
      subtitle="Enter one of the recovery codes you saved when setting up two-factor authentication."
    >
      <form className={styles.passwordFlowForm} onSubmit={handleSubmit}>
        <div className={styles.field}>
          <label htmlFor="recovery-code-input">Recovery code</label>
          <input
            id="recovery-code-input"
            className={styles.input}
            type="text"
            value={recoveryCode}
            onChange={(event) => {
              setRecoveryCode(event.target.value);
              setError(null);
            }}
            placeholder="e.g. ABCD-1234-EFGH"
            autoComplete="one-time-code"
            disabled={!pendingMfaChallenge || isSubmitting}
          />
          <p className={styles.fieldHint}>Recovery codes are one-time use and should be stored securely.</p>
        </div>

        {error ? <div className={`${styles.notice} ${styles.noticeDanger}`.trim()}>{error}</div> : null}

        <button
          type="submit"
          className={`${styles.submit} ${styles.passwordFlowSubmit}`.trim()}
          disabled={!pendingMfaChallenge || isSubmitting || recoveryCode.trim().length === 0}
        >
          <span>{isSubmitting ? 'Verifying recovery code...' : 'Use Recovery Code'}</span>
          {!isSubmitting ? <IconArrowRight size={18} /> : null}
        </button>
      </form>
    </AuthScreenShell>
  );
}
