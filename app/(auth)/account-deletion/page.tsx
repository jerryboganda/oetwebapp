import type { Metadata } from 'next';
import Link from 'next/link';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import shellStyles from '@/components/auth/auth-screen-shell.module.scss';
import legalStyles from '../../terms/terms.module.scss';
import { AUTH_ROUTES } from '@/lib/auth/routes';

export const metadata: Metadata = {
  title: 'Delete your account · OET with Dr Ahmed Hesham',
  description:
    'How to request deletion of your OET with Dr Ahmed Hesham account and associated data, in the app or by email. Play Data Deletion URL.',
};

const SUPPORT_EMAIL = 'support@oetwithdrhesham.co.uk';

export default function AccountDeletionPage() {
  return (
    <AuthScreenShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET with Dr Ahmed Hesham"
      eyebrow="Privacy"
      title="Delete your account"
      subtitle="Request deletion of your account and associated data. This is the public deletion page referenced in our Google Play listing."
      stackClassName={shellStyles.successStackWide}
      cardClassName={shellStyles.successCardWide}
      headerClassName={shellStyles.successHeaderWide}
      footer={
        <>
          Prefer email?{' '}
          <a className={shellStyles.link} href={`mailto:${SUPPORT_EMAIL}?subject=Delete%20my%20account`}>
            {SUPPORT_EMAIL}
          </a>
        </>
      }
    >
      <div className={legalStyles.shell}>
        <div className={legalStyles.meta}>
          <span className={legalStyles.metaPill}>Play Data Deletion URL</span>
          <span>https://app.oetwithdrhesham.co.uk/account-deletion</span>
        </div>

        <div className={legalStyles.layout}>
          <div className={legalStyles.body}>
            <section className={legalStyles.section} aria-labelledby="delete-in-app-title">
              <header className={legalStyles.sectionHeader}>
                <span className={legalStyles.sectionNumber} aria-hidden="true">1</span>
                <h2 id="delete-in-app-title" className={legalStyles.sectionTitle}>
                  Option A — delete inside the app (fastest)
                </h2>
              </header>
              <ul className={legalStyles.sectionList}>
                <li>Sign in, then go to <strong>Settings → Delete Account</strong> (also linked from <strong>Settings → Privacy</strong>).</li>
                <li>Confirm your password and optionally tell us why you are leaving.</li>
                <li>Deletion is scheduled with a <strong>30-day grace period</strong>. You are signed out immediately.</li>
                <li>Changed your mind during grace? Email <a href={`mailto:${SUPPORT_EMAIL}`}>{SUPPORT_EMAIL}</a> from your account email to cancel.</li>
              </ul>
            </section>

            <section className={legalStyles.section} aria-labelledby="delete-web-title">
              <header className={legalStyles.sectionHeader}>
                <span className={legalStyles.sectionNumber} aria-hidden="true">2</span>
                <h2 id="delete-web-title" className={legalStyles.sectionTitle}>
                  Option B — request by email (no sign-in needed)
                </h2>
              </header>
              <ul className={legalStyles.sectionList}>
                <li>
                  Email <a href={`mailto:${SUPPORT_EMAIL}?subject=Delete%20my%20account`}>{SUPPORT_EMAIL}</a> from the
                  SAME email address used on the account, subject <strong>“Delete my account”</strong>.
                </li>
                <li>We verify ownership before acting and confirm the irreversible steps with you.</li>
                <li>Privacy/deletion requests are acknowledged within <strong>7 days</strong> after verification; account/billing follow-ups normally within <strong>2 business days</strong>.</li>
                <li>Do NOT email passwords, card details, or clinical documents.</li>
              </ul>
            </section>

            <section className={legalStyles.section} aria-labelledby="delete-data-title">
              <header className={legalStyles.sectionHeader}>
                <span className={legalStyles.sectionNumber} aria-hidden="true">3</span>
                <h2 id="delete-data-title" className={legalStyles.sectionTitle}>
                  What is deleted and what is retained
                </h2>
              </header>
              <p className={legalStyles.sectionLead}>
                Deletion removes your account and associated learning data (profile, submissions, recordings,
                progress, preferences), subject only to clearly disclosed legitimate retention.
              </p>
              <ul className={legalStyles.sectionList}>
                <li>Speaking/conversation audio: default 30-day retention; deleted with the account unless already expired.</li>
                <li>Account data: retained only up to 24 months post-closure for re-activation, disputes, and statutory duties, then removed.</li>
                <li>Billing records: kept 7 years where UK accounting law requires it (invoices without learning content).</li>
                <li>Security logs: up to 90 days for incident investigation.</li>
              </ul>
              <p className={legalStyles.callout}>
                Merely disabling or freezing an account does not satisfy deletion. This flow deletes the account and
                associated data as above. Full details: <Link className={shellStyles.link} href="/privacy">Privacy Notice</Link>.
              </p>
            </section>
          </div>
        </div>

        <div className={legalStyles.actions}>
          <a href={`mailto:${SUPPORT_EMAIL}?subject=Delete%20my%20account`} className={shellStyles.submit}>
            Request deletion by email
          </a>
          <Link href="/privacy" className={shellStyles.secondaryButton}>
            Privacy Notice
          </Link>
          <Link href={AUTH_ROUTES.signIn} className={shellStyles.secondaryButton}>
            Return to sign in
          </Link>
        </div>
      </div>
    </AuthScreenShell>
  );
}
