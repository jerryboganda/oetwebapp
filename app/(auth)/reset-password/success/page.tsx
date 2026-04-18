'use client';

import { IconArrowRight, IconCheck, IconShieldLock } from '@tabler/icons-react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useEffect, useState } from 'react';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import { AUTH_ROUTES } from '@/lib/auth/routes';

const REDIRECT_SECONDS = 3;

export default function ResetPasswordSuccessPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const email = searchParams.get('email') ?? 'your account';
  const nextPath = searchParams.get('next');
  const signInHref = nextPath
    ? `${AUTH_ROUTES.signIn}?email=${encodeURIComponent(email)}&next=${encodeURIComponent(nextPath)}`
    : `${AUTH_ROUTES.signIn}?email=${encodeURIComponent(email)}`;

  // Countdown that is (a) long enough to read, (b) visible so the user
  // understands what's about to happen, (c) pausable — the timer stops if
  // the user hovers or focuses anywhere on the panel, so screen-reader
  // users or anyone who wants to click the explicit button are not
  // railroaded.
  const [secondsLeft, setSecondsLeft] = useState(REDIRECT_SECONDS);
  const [paused, setPaused] = useState(false);

  useEffect(() => {
    if (!paused && secondsLeft <= 0) {
      router.replace(signInHref);
    }
  }, [secondsLeft, paused, router, signInHref]);

  useEffect(() => {
    if (paused) return;
    const interval = window.setInterval(() => {
      setSecondsLeft((current) => Math.max(0, current - 1));
    }, 1000);

    return () => window.clearInterval(interval);
  }, [paused]);

  return (
    <AuthScreenShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Password Updated"
      title="Reset complete"
      subtitle={`Your new password is active for ${email}. You can sign in right away.`}
    >
      <div
        className={styles.successPanelCompact}
        onMouseEnter={() => setPaused(true)}
        onMouseLeave={() => setPaused(false)}
        onFocus={() => setPaused(true)}
        onBlur={() => setPaused(false)}
      >
        <div className={styles.successHeroBadge}>
          <span className={styles.successHeroBadgeIcon}>
            <IconCheck size={28} strokeWidth={2.5} />
          </span>
          <div>
            <strong>Password reset successfully</strong>
            <p>Your account is ready to use with your new password.</p>
          </div>
        </div>

        <ul className={styles.successChecklistCompact}>
          <li>
            <IconShieldLock size={16} /> All other sessions have been signed out for security.
          </li>
          <li>
            <IconCheck size={16} /> You can now sign in with {email}.
          </li>
        </ul>

        <Link href={signInHref} className={styles.successPrimaryButton}>
          <span>Continue to sign in</span>
          <IconArrowRight size={18} />
        </Link>

        <p
          className={styles.successCountdownHint}
          aria-live="polite"
          role="status"
        >
          {paused
            ? 'Auto-redirect paused. Click the button above when ready.'
            : `Redirecting automatically in ${secondsLeft} second${secondsLeft === 1 ? '' : 's'}…`}
        </p>
      </div>
    </AuthScreenShell>
  );
}
