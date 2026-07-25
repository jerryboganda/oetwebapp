'use client';

import { useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowRight, Laptop, ShieldCheck } from 'lucide-react';
import { useAuth } from '@/contexts/auth-context';
import { sendDeviceVerificationOtp } from '@/lib/auth-client';
import { resolvePostAuthDestination } from '@/lib/auth-routes';
import { AuthScreenShell } from './auth-screen-shell';
import { OtpCodeInput } from './otp-code-input';
import styles from './auth-screen-shell.module.scss';
import { readErrorMessage } from '@/lib/read-error-message';

interface DeviceChallengeFormProps {
  nextHref?: string | null;
}

export function DeviceChallengeForm({ nextHref }: DeviceChallengeFormProps) {
  const router = useRouter();
  const { pendingDeviceChallenge, completeDeviceVerification } = useAuth();
  const [code, setCode] = useState('');
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isSending, setIsSending] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const sentForToken = useRef<string | null>(null);

  useEffect(() => {
    const challengeToken = pendingDeviceChallenge?.challengeToken;
    if (!challengeToken || sentForToken.current === challengeToken) {
      return;
    }

    sentForToken.current = challengeToken;
    let cancelled = false;
    setIsSending(true);

    (async () => {
      try {
        const challenge = await sendDeviceVerificationOtp();
        if (!cancelled) {
          setNotice(`Enter the 6-digit code sent to ${challenge.destinationHint || pendingDeviceChallenge?.email}.`);
        }
      } catch (sendError) {
        if (!cancelled) {
          setError(readErrorMessage(sendError, 'Unable to send the device verification code.'));
        }
      } finally {
        if (!cancelled) {
          setIsSending(false);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [pendingDeviceChallenge?.challengeToken, pendingDeviceChallenge?.email]);

  const handleResend = async () => {
    setCode('');
    setError(null);
    setIsSending(true);

    try {
      const challenge = await sendDeviceVerificationOtp();
      setNotice(`Enter the 6-digit code sent to ${challenge.destinationHint || pendingDeviceChallenge?.email}.`);
    } catch (sendError) {
      setError(readErrorMessage(sendError, 'Unable to send the device verification code.'));
    } finally {
      setIsSending(false);
    }
  };

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalizedCode = code.replace(/\D/g, '');

    if (normalizedCode.length !== 6) {
      setError('Enter the 6-digit code sent to your email.');
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      const session = await completeDeviceVerification(normalizedCode);
      router.replace(resolvePostAuthDestination(session.currentUser, nextHref));
    } catch (submitError) {
      setError(readErrorMessage(submitError, 'Unable to verify this device.'));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <AuthScreenShell
      eyebrow="Device Verification"
      title="Verify this device"
      subtitle={pendingDeviceChallenge
        ? `We don't recognize this device for ${pendingDeviceChallenge.email}. Confirm it's you before continuing.`
        : 'A pending device verification is required before you can continue.'}
      footer={
        <p className={styles.resend}>
          Did not receive a code?{' '}
          <button
            type="button"
            className={styles.link}
            onClick={() => void handleResend()}
            disabled={!pendingDeviceChallenge || isSending}
          >
            Resend it
          </button>
        </p>
      }
    >
      <form className={styles.passwordFlowForm} onSubmit={handleSubmit}>
        <div className={styles.summaryCard}>
          <h4>New device detected</h4>
          <div className={styles.summaryList}>
            <div className={styles.summaryItem}>
              <span className={styles.summaryIcon}>
                <Laptop size={16} />
              </span>
              <p>Approving this device signs your previous device out once verification succeeds.</p>
            </div>
            <div className={styles.summaryItem}>
              <span className={styles.summaryIcon}>
                <ShieldCheck size={16} />
              </span>
              <p>Enter the code we emailed you to trust this device and finish signing in.</p>
            </div>
          </div>
        </div>

        <div className={styles.field}>
          <label>Verification code</label>
          <OtpCodeInput value={code} onChange={(next) => {
            setCode(next.replace(/\D/g, '').slice(0, 6));
            setError(null);
          }} disabled={!pendingDeviceChallenge || isSubmitting} />
          <p className={styles.fieldHint}>Use the 6-digit code from your email.</p>
        </div>

        {notice ? <p className={styles.fieldHint}>{notice}</p> : null}
        {error ? <div className={`${styles.notice} ${styles.noticeDanger}`.trim()}>{error}</div> : null}

        <button
          type="submit"
          className={`${styles.submit} ${styles.passwordFlowSubmit}`.trim()}
          disabled={!pendingDeviceChallenge || isSubmitting}
        >
          <span>{isSubmitting ? 'Verifying device...' : 'Verify Device'}</span>
          {!isSubmitting ? <ArrowRight size={18} /> : null}
        </button>
      </form>
    </AuthScreenShell>
  );
}
