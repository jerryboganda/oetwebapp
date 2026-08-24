'use client';

import React, { useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import { OtpCodeInput } from '@/components/auth/otp-code-input';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import { AUTH_ROUTES, getAuthFlowLinks } from '@/lib/auth/routes';
import { describeOtpDelivery } from '@/lib/auth/otp-delivery';
import { loadPasswordResetOtp } from '@/lib/auth/password-reset-otp';

export function ForgotPasswordVerifyPageContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const flowLinks = getAuthFlowLinks('passwordResetOtp');
  const email = searchParams?.get('email') ?? '';
  const [deliveryChannel, setDeliveryChannel] = useState('email');
  const [destinationHint, setDestinationHint] = useState(email || 'your email');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [otp, setOtp] = useState('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    const stored = loadPasswordResetOtp(email);
    if (!stored) {
      return;
    }
    setDeliveryChannel(stored.deliveryChannel || 'email');
    setDestinationHint(stored.destinationHint || email || 'your email');
  }, [email]);

  const deliveryLabel = describeOtpDelivery(deliveryChannel, destinationHint, email || 'your email');

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalizedOtp = otp.replace(/\D/g, '');

    if (normalizedOtp.length !== 6) {
      setErrorMessage('The OTP is invalid. Enter the 6 digit reset code.');
      return;
    }

    setIsSubmitting(true);
    const params = new URLSearchParams({ token: normalizedOtp });
    if (email) {
      params.set('email', email);
    }

    router.push(`${AUTH_ROUTES.passwordCreate}?${params.toString()}`);
  };

  return (
    <AuthScreenShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Verify Reset OTP"
      title={deliveryChannel === 'sms' ? 'Check Your Phone' : 'Check Your Email'}
      subtitle={`Step 2 of 3. Enter the 6 digit reset code sent by ${deliveryLabel} so we can unlock the new-password screen.`}
      footer={
        <p className={styles.resend}>
          Need to use another email?{' '}
          <Link className={styles.link} href={flowLinks.primary}>
            Go back
          </Link>
        </p>
      }
    >
      <form onSubmit={handleSubmit} className={styles.passwordFlowForm}>
        <OtpCodeInput
          value={otp}
          onChange={(value) => {
            setOtp(value.replace(/\D/g, '').slice(0, 6));
            setErrorMessage(null);
          }}
          length={6}
        />

        {errorMessage ? (
          <p className={styles.fieldError} role="alert" aria-live="polite">
            {errorMessage}
          </p>
        ) : null}

        <button
          className={`${styles.submit} ${styles.passwordFlowSubmit}`.trim()}
          type="submit"
          disabled={isSubmitting}
        >
          {isSubmitting ? 'Verifying OTP...' : 'Verify OTP'}
        </button>

        <div className={styles.footer}>
          <button
            type="button"
            className={styles.linkButton}
            onClick={() => setOtp('')}
          >
            Clear and resend
          </button>
        </div>
      </form>
    </AuthScreenShell>
  );
}
