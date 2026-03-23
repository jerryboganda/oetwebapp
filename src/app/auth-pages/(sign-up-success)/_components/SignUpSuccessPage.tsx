"use client";

import {
  IconArrowRight,
  IconBook2,
  IconBrandWhatsapp,
  IconCalendarEvent,
  IconCheck,
  IconCreditCard,
  IconLifebuoy,
  IconMail,
  IconMapPin,
  IconUserCircle,
} from "@tabler/icons-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useMemo } from "react";
import AuthBackgroundShell from "@/app/auth-pages/_components/AuthBackgroundShell";
import styles from "@/app/auth-pages/_components/AuthBackgroundShell.module.scss";
import { AUTH_ROUTES } from "@/lib/auth/routes";
import {
  buildSupportMailto,
  buildSupportWhatsAppLink,
  SUPPORT_EMAIL,
} from "@/lib/auth/support";

const SignUpSuccessPage = () => {
  const searchParams = useSearchParams();
  const email = searchParams.get("email") ?? "your email";
  const fullName = searchParams.get("fullName") ?? "New learner";
  const exam = searchParams.get("exam") ?? "OET";
  const profession = searchParams.get("profession") ?? "Profession";
  const session = searchParams.get("session") ?? "Session pending";
  const sessionPrice = searchParams.get("sessionPrice") ?? "TBC";
  const sessionMode = searchParams.get("sessionMode") ?? "online";
  const sessionStart = searchParams.get("sessionStart") ?? "TBC";
  const country = searchParams.get("country") ?? "Not selected";
  const registrationStamp = useMemo(() => {
    const now = new Date();
    const datePart = new Intl.DateTimeFormat("en-GB", {
      day: "2-digit",
      month: "short",
      year: "numeric",
    }).format(now);
    const timePart = new Intl.DateTimeFormat("en-US", {
      hour: "numeric",
      minute: "2-digit",
      hour12: true,
    }).format(now);

    return `${datePart} at ${timePart}`;
  }, []);

  return (
    <AuthBackgroundShell
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
          Need personal assistance?{" "}
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
              {sessionPrice}
            </strong>
            <p>
              {sessionMode} access for the {session} cohort starting{" "}
              {sessionStart}.
            </p>
            <div className={styles.successHighlightFoot}>
              <span>{country}</span>
              <span>{exam}</span>
            </div>
          </div>
        </div>

        <div className={styles.successContentGrid}>
          <section className={styles.successSummaryCard}>
            <h4>Subscription And Enrollment Summary</h4>
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
                  <IconBook2 size={14} />
                </span>
                <div>
                  <strong>Selected Session</strong>
                  <p>{session}</p>
                </div>
              </div>
              <div className={styles.successSummaryItem}>
                <span className={styles.summaryIcon}>
                  <IconCreditCard size={14} />
                </span>
                <div>
                  <strong>Subscription Snapshot</strong>
                  <p>{`${sessionPrice} - ${sessionMode}`}</p>
                </div>
              </div>
              <div className={styles.successSummaryItem}>
                <span className={styles.summaryIcon}>
                  <IconCalendarEvent size={14} />
                </span>
                <div>
                  <strong>Session Start</strong>
                  <p>{sessionStart}</p>
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
            href={AUTH_ROUTES.signIn}
            className={`${styles.submit} ${styles.successPrimaryAction}`}
          >
            <IconArrowRight size={18} />
            <span>Login</span>
          </Link>
          <a
            href={buildSupportMailto(email)}
            className={`${styles.secondaryButton} ${styles.successGhostAction}`}
          >
            <IconLifebuoy size={18} />
            <span>Contact Support</span>
          </a>
          <a
            href={buildSupportWhatsAppLink(email)}
            className={`${styles.secondaryButton} ${styles.successGhostAction}`}
            target="_blank"
            rel="noreferrer"
          >
            <IconBrandWhatsapp size={18} />
            <span>Contact on WhatsApp</span>
          </a>
        </div>
      </div>
    </AuthBackgroundShell>
  );
};

export default SignUpSuccessPage;
