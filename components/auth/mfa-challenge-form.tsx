'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { IconArrowRight, IconDeviceMobile, IconKey, IconShieldLock } from '@tabler/icons-react';
import { useAuth } from '@/contexts/auth-context';
import { resolvePostAuthDestination } from '@/lib/auth-routes';
import { AuthScreenShell } from './auth-screen-shell';
import { OtpCodeInput } from './otp-code-input';
import styles from './auth-screen-shell.module.scss';

interface MfaChallengeFormProps {
  nextHref?: string | null;
}

function readErrorMessage(error: unknown): string {
  if (error && typeof error === 'object' && 'message' in error && typeof error.message === 'string') {
    return error.message;
  }

  return 'Unable to verify the MFA challenge.';
}

export function MfaChallengeForm({ nextHref }: MfaChallengeFormProps) {
  const router = useRouter();
  const { pendingMfaChallenge, completeMfaChallenge, completeRecoveryChallenge } = useAuth();
  const [code, setCode] = useState('');
  const [recoveryCode, setRecoveryCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isSubmittingCode, setIsSubmittingCode] = useState(false);
  const [isSubmittingRecovery, setIsSubmittingRecovery] = useState(false);

  const handleCodeSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalizedCode = code.replace(/\D/g, '');

    if (normalizedCode.length !== 6) {
      setError('Enter the 6-digit code from your authenticator app.');
      return;
    }

    setIsSubmittingCode(true);
    setError(null);

    try {
      const session = await completeMfaChallenge(normalizedCode);
      router.replace(resolvePostAuthDestination(session.currentUser, nextHref));
    } catch (submitError) {
      setError(readErrorMessage(submitError));
    } finally {
      setIsSubmittingCode(false);
    }
  };

  const handleRecoverySubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setIsSubmittingRecovery(true);
    setError(null);

    try {
      const session = await completeRecoveryChallenge(recoveryCode.trim());
      router.replace(resolvePostAuthDestination(session.currentUser, nextHref));
    } catch (submitError) {
      setError(readErrorMessage(submitError));
    } finally {
      setIsSubmittingRecovery(false);
    }
  };

  return (
    <AuthScreenShell
      eyebrow="Multi-Factor Authentication"
      title="Complete MFA challenge"
      subtitle={pendingMfaChallenge
        ? `Enter the authenticator code for ${pendingMfaChallenge.email}.`
        : 'A pending MFA challenge is required before you can continue.'}
    >
      <div className={styles.wizard}>
        <form className={styles.passwordFlowForm} onSubmit={handleCodeSubmit}>
          <div className={styles.summaryCard}>
            <h4>Authenticator verification</h4>
            <div className={styles.summaryList}>
              <div className={styles.summaryItem}>
                <span className={styles.summaryIcon}>
                  <IconDeviceMobile size={16} />
                </span>
                <p>Open your authenticator app and enter the current 6-digit code for this account.</p>
              </div>
              <div className={styles.summaryItem}>
                <span className={styles.summaryIcon}>
                  <IconShieldLock size={16} />
                </span>
                <p>This step must succeed before the platform restores the session and routes you onward.</p>
              </div>
            </div>
          </div>

          <div className={styles.field}>
            <label>Authenticator code</label>
            <OtpCodeInput value={code} onChange={(next) => {
              setCode(next.replace(/\D/g, '').slice(0, 6));
              setError(null);
            }} disabled={!pendingMfaChallenge || isSubmittingCode} />
            <p className={styles.fieldHint}>Use the live code from your authenticator app.</p>
          </div>

          {error ? <div className={`${styles.notice} ${styles.noticeDanger}`.trim()}>{error}</div> : null}

          <button
            type="submit"
            className={`${styles.submit} ${styles.passwordFlowSubmit}`.trim()}
            disabled={!pendingMfaChallenge || isSubmittingCode}
          >
            <span>{isSubmittingCode ? 'Verifying code...' : 'Verify Code'}</span>
            {!isSubmittingCode ? <IconArrowRight size={18} /> : null}
          </button>

          <p className={styles.fieldHint} style={{ textAlign: 'center', marginTop: '0.75rem' }}>
            Lost your authenticator?{' '}
            <Link
              href={`/mfa/recovery${nextHref ? `?next=${encodeURIComponent(nextHref)}` : ''}`}
              className={styles.link}
            >
              Use a recovery code
            </Link>
          </p>
        </form>

        <div className={styles.summaryCard}>
          <h4>Use a recovery code instead</h4>
          <form className={styles.passwordFlowForm} onSubmit={handleRecoverySubmit}>
            <div className={styles.field}>
              <label htmlFor="mfa-recovery-code">Recovery code</label>
              <input
                id="mfa-recovery-code"
                className={styles.input}
                value={recoveryCode}
                onChange={(event) => setRecoveryCode(event.target.value)}
                placeholder="Use one of your saved recovery codes"
                autoComplete="one-time-code"
              />
              <p className={styles.fieldHint}>Recovery codes are one-time use and should be stored securely.</p>
            </div>

            <button
              type="submit"
              className={styles.secondaryButton}
              disabled={!pendingMfaChallenge || isSubmittingRecovery || recoveryCode.trim().length === 0}
            >
              <span>{isSubmittingRecovery ? 'Checking recovery code...' : 'Use Recovery Code'}</span>
              {!isSubmittingRecovery ? <IconKey size={18} /> : null}
            </button>
          </form>
        </div>
      </div>
    </AuthScreenShell>
  );
}
