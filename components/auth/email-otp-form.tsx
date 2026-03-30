'use client';

import Link from 'next/link';
import { useState } from 'react';
import { IconArrowRight, IconMailCheck, IconRefresh, IconShieldCheck } from '@tabler/icons-react';
import { sendEmailVerificationOtp, verifyEmailOtp } from '@/lib/auth-client';
import type { CurrentUser } from '@/lib/types/auth';
import { AuthScreenShell } from './auth-screen-shell';
import { OtpCodeInput } from './otp-code-input';
import styles from './auth-screen-shell.module.scss';

interface EmailOtpFormProps {
  email: string;
  onVerified?: (user: CurrentUser) => void;
}

function readErrorMessage(error: unknown): string {
  if (error && typeof error === 'object' && 'message' in error && typeof error.message === 'string') {
    return error.message;
  }

  return 'Unable to verify the OTP code.';
}

export function EmailOtpForm({ email, onVerified }: EmailOtpFormProps) {
  const [code, setCode] = useState('');
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isSending, setIsSending] = useState(false);
  const [isVerifying, setIsVerifying] = useState(false);

  const handleSendCode = async () => {
    setIsSending(true);
    setError(null);

    try {
      const challenge = await sendEmailVerificationOtp(email);
      const destination = challenge.destinationHint || email;
      setStatusMessage(`A verification code was sent to ${destination}. Enter it below to continue.`);
    } catch (sendError) {
      setError(readErrorMessage(sendError));
    } finally {
      setIsSending(false);
    }
  };

  const handleVerify = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalizedCode = code.replace(/\D/g, '');

    if (normalizedCode.length !== 6) {
      setError('Enter the 6-digit verification code from your email.');
      return;
    }

    setIsVerifying(true);
    setError(null);

    try {
      const currentUser = await verifyEmailOtp(email, normalizedCode);
      setStatusMessage('Email verified successfully.');
      onVerified?.(currentUser);
    } catch (verifyError) {
      setError(readErrorMessage(verifyError));
    } finally {
      setIsVerifying(false);
    }
  };

  return (
    <AuthScreenShell
      eyebrow="Email Verification"
      title="Verify your email"
      subtitle={`We'll send a one-time verification code to ${email}.`}
      footer={(
        <p>
          Need to use another account?{' '}
          <Link href="/sign-in" className={styles.link}>
            Return to sign in
          </Link>
        </p>
      )}
    >
      <form className={styles.passwordFlowForm} onSubmit={handleVerify}>
        <div className={styles.summaryCard}>
          <h4>Complete this security step</h4>
          <div className={styles.summaryList}>
            <div className={styles.summaryItem}>
              <span className={styles.summaryIcon}>
                <IconMailCheck size={16} />
              </span>
              <p>The code is sent to the email attached to your platform account.</p>
            </div>
            <div className={styles.summaryItem}>
              <span className={styles.summaryIcon}>
                <IconShieldCheck size={16} />
              </span>
              <p>Successful verification unlocks the rest of the sign-in flow and keeps the backend state authoritative.</p>
            </div>
          </div>
        </div>

        <button type="button" className={styles.secondaryButton} onClick={() => void handleSendCode()} disabled={isSending}>
          <span>{isSending ? 'Sending code...' : 'Send verification code'}</span>
          {!isSending ? <IconRefresh size={18} /> : null}
        </button>

        <div className={styles.field}>
          <label>Verification code</label>
          <OtpCodeInput value={code} onChange={(next) => {
            setCode(next.replace(/\D/g, '').slice(0, 6));
            setError(null);
          }} disabled={isVerifying} />
          <p className={styles.fieldHint}>Enter the 6-digit email verification code.</p>
        </div>

        {statusMessage ? <div className={`${styles.notice} ${styles.noticeSuccess}`.trim()}>{statusMessage}</div> : null}
        {error ? <div className={`${styles.notice} ${styles.noticeDanger}`.trim()}>{error}</div> : null}

        <button type="submit" className={`${styles.submit} ${styles.passwordFlowSubmit}`.trim()} disabled={isVerifying}>
          <span>{isVerifying ? 'Verifying email...' : 'Verify Email'}</span>
          {!isVerifying ? <IconArrowRight size={18} /> : null}
        </button>
      </form>
    </AuthScreenShell>
  );
}
