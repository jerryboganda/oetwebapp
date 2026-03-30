import type { Metadata } from 'next';
import Link from 'next/link';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import { AUTH_ROUTES } from '@/lib/auth/routes';

export const metadata: Metadata = {
  title: 'Terms & Conditions',
  description:
    'Review the demo terms, privacy expectations, and account rules for the OET auth workspace.',
};

const termsSections = [
  {
    title: 'Using This Demo',
    points: [
      'This project is a frontend demo of the OET authentication journey.',
      'Account creation, sign-in, OTP, and password reset flows are mock interactions only.',
      'Do not enter real credentials or sensitive learner data into this demo.',
    ],
  },
  {
    title: 'Privacy Expectations',
    points: [
      'The forms are designed for UI demonstration and local validation.',
      'Sign-up selections stay in the browser flow and are not sent to a live backend.',
      'Use support links only for demo navigation or future integration planning.',
    ],
  },
  {
    title: 'Account Rules',
    points: [
      'Use the sample OTP `12345` wherever verification is required in the mock flow.',
      'Password reset screens are available for visual testing and route validation.',
      'The auth-only build intentionally stops at the authentication experience and does not open a product dashboard.',
    ],
  },
];

export default function TermsPage() {
  return (
    <AuthScreenShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Policy"
      title="Terms of Use & Conditions"
      subtitle="These lightweight rules keep the auth-only demo clear, safe, and easy to evaluate during local review."
      stackClassName={styles.successStackWide}
      cardClassName={styles.successCardWide}
      headerClassName={styles.successHeaderWide}
      footer={
        <>
          Ready to continue?{' '}
          <Link className={styles.link} href={AUTH_ROUTES.signIn}>
            Back to login
          </Link>
        </>
      }
    >
      <div className={styles.successPanel}>
        <div className={styles.successContentGrid}>
          {termsSections.map((section) => (
            <section
              key={section.title}
              className={styles.successChecklistCard}
            >
              <h4>{section.title}</h4>
              <ul className={styles.successChecklistList}>
                {section.points.map((point) => (
                  <li key={point}>{point}</li>
                ))}
              </ul>
            </section>
          ))}
        </div>

        <div className={styles.successActions}>
          <Link href={AUTH_ROUTES.signIn} className={styles.submit}>
            Return to Login
          </Link>
          <Link href={AUTH_ROUTES.signUp} className={styles.secondaryButton}>
            Create Account
          </Link>
          <Link
            href={AUTH_ROUTES.passwordReset}
            className={styles.secondaryButton}
          >
            Reset Password
          </Link>
        </div>
      </div>
    </AuthScreenShell>
  );
}
