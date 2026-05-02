'use client';

import {
  IconArrowRight,
  IconBrandWhatsapp,
  IconCalendarEvent,
  IconCheck,
  IconLifebuoy,
  IconMail,
  IconMapPin,
  IconUserCircle,
} from '@tabler/icons-react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useEffect } from 'react';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import { AUTH_ROUTES } from '@/lib/auth/routes';
import {
  buildSupportMailto,
  buildSupportWhatsAppLink,
  SUPPORT_EMAIL,
} from '@/lib/auth/support';

export default function RegisterSuccessPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const email = searchParams.get('email') ?? 'your email';
  const fullName = searchParams.get('fullName') ?? 'New learner';
  const exam = searchParams.get('exam') ?? 'OET';
  const profession = searchParams.get('profession') ?? 'Profession';
  const country = searchParams.get('country') ?? 'Not selected';
  const registrationStamp = searchParams.get('stamp') ?? 'just now';
  const nextPath = searchParams.get('next');
  const signInHref = nextPath
    ? `${AUTH_ROUTES.signIn}?email=${encodeURIComponent(email)}&next=${encodeURIComponent(nextPath)}`
    : `${AUTH_ROUTES.signIn}?email=${encodeURIComponent(email)}`;

  useEffect(() => {
    const timeout = window.setTimeout(() => {
      router.replace(signInHref);
    }, 3000);

    return () => window.clearTimeout(timeout);
  }, [router, signInHref]);

  return (
    <AuthScreenShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Registration Complete"
      title="Account Created Successfully"
      subtitle="Your registration details have been captured. Review your setup below, then continue to login or reach support if you need help."
      stackClassName={styles.successStackWide}
      cardClassName={styles.successCardWide}
      headerClassName={styles.successHeaderWide}
      footer={
        <span>
          Need personal assistance?{' '}
          <a className={styles.link} href={buildSupportMailto(email)}>
            {SUPPORT_EMAIL}
          </a>
        </span>
      }
    >
      <div className={styles.successPanel}>
        <div className={styles.successHeroGrid}>
          <div className={styles.successBadge}>
            <span className={styles.successBadgeIcon}>
              <IconCheck size={18} />
            </span>
            <div>
              <strong className={styles.successBadgeName}>{fullName}</strong>
              <p>Your OET workspace setup has been saved for {email}.</p>
              <div className={styles.successBadgeTime}>
                <IconCalendarEvent size={14} />
                <span>Registered on {registrationStamp}</span>
              </div>
              <div className={styles.successBadgeMeta}>
                <span>Workspace Ready</span>
                <span>Support Active</span>
              </div>
            </div>
          </div>

          <div className={styles.successHighlightCard}>
            <span className={styles.successHighlightEyebrow}>
              Enrollment Snapshot
            </span>
            <strong className={styles.successHighlightPrice}>
              {exam}
            </strong>
            <p>
              {profession} candidate preparing for {country}.
            </p>
            <div className={styles.successHighlightFoot}>
              <span>{country}</span>
              <span>{exam}</span>
            </div>
          </div>
        </div>

        <div className={styles.successContentGrid}>
          <section className={styles.successSummaryCard}>
            <h4>Registration Summary</h4>
            <div className={styles.successSummaryGrid}>
              <div className={styles.successSummaryItem}>
                <span className={styles.summaryIcon}>
                  <IconMail size={14} />
                </span>
                <div>
                  <strong>Email</strong>
                  <p>{email}</p>
                </div>
              </div>
              <div className={styles.successSummaryItem}>
                <span className={styles.summaryIcon}>
                  <IconUserCircle size={14} />
                </span>
                <div>
                  <strong>Exam And Profession</strong>
                  <p>{`${exam} - ${profession}`}</p>
                </div>
              </div>
              <div className={styles.successSummaryItem}>
                <span className={styles.summaryIcon}>
                  <IconMapPin size={14} />
                </span>
                <div>
                  <strong>Target Country</strong>
                  <p>{country}</p>
                </div>
              </div>
              <div className={styles.successSummaryItem}>
                <span className={styles.summaryIcon}>
                  <IconCalendarEvent size={14} />
                </span>
                <div>
                  <strong>Registered</strong>
                  <p>{registrationStamp}</p>
                </div>
              </div>
            </div>
          </section>

          <section className={styles.successChecklistCard}>
            <h4>Priority Actions</h4>
            <ul className={styles.successChecklistList}>
              <li>
                Your registration record is now active in the OET workspace.
              </li>
              <li>
                Use Login to enter your dashboard and continue onboarding.
              </li>
              <li>
                Need corrections? Support and WhatsApp are both available below.
              </li>
            </ul>
          </section>
        </div>

        <div className={styles.successActions}>
          <Link
            href={signInHref}
            className={`${styles.submit} ${styles.successPrimaryAction}`.trim()}
          >
            <IconArrowRight size={18} />
            <span>Login</span>
          </Link>
          <a
            href={buildSupportMailto(email)}
            className={`${styles.secondaryButton} ${styles.successGhostAction}`.trim()}
          >
            <IconLifebuoy size={18} />
            <span>Contact Support</span>
          </a>
          <a
            href={buildSupportWhatsAppLink(email)}
            className={`${styles.secondaryButton} ${styles.successGhostAction}`.trim()}
            target="_blank"
            rel="noreferrer"
          >
            <IconBrandWhatsapp size={18} />
            <span>Contact on WhatsApp</span>
          </a>
        </div>
      </div>
    </AuthScreenShell>
  );
}
