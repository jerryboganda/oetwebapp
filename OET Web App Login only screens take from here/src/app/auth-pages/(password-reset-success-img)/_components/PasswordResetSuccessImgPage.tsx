"use client";

import { IconArrowRight, IconCheck, IconMailOpened } from "@tabler/icons-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect } from "react";
import AuthBackgroundShell from "@/app/auth-pages/_components/AuthBackgroundShell";
import styles from "@/app/auth-pages/_components/AuthBackgroundShell.module.scss";
import { AUTH_ROUTES } from "@/lib/auth/routes";

const PasswordResetSuccessImgPage = () => {
  const router = useRouter();
  const searchParams = useSearchParams();
  const email = searchParams.get("email") ?? "your account";

  useEffect(() => {
    const timeout = window.setTimeout(() => {
      router.replace(AUTH_ROUTES.signIn);
    }, 3000);

    return () => window.clearTimeout(timeout);
  }, [router]);

  return (
    <AuthBackgroundShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Password Updated"
      title="Reset Complete"
      subtitle={`Your password has been updated successfully for ${email}. You can now sign in with your new password.`}
      footer={
        <span>
          Redirecting to login in a few seconds. If nothing happens, use the
          button below.
        </span>
      }
    >
      <div className={styles.successPanel}>
        <div className={styles.successBadge}>
          <span className={styles.successBadgeIcon}>
            <IconCheck size={18} />
          </span>
          <div>
            <strong>Password reset successful</strong>
            <p>Your account is ready to be accessed again.</p>
          </div>
        </div>

        <div className={styles.successSummaryCard}>
          <h4>Recovery Summary</h4>
          <div className={styles.successSummaryList}>
            <div className={styles.successSummaryItem}>
              <span className={styles.summaryIcon}>
                <IconMailOpened size={14} />
              </span>
              <div>
                <strong>Recovered Account</strong>
                <p>{email}</p>
              </div>
            </div>
            <div className={styles.successSummaryItem}>
              <span className={styles.summaryIcon}>
                <IconCheck size={14} />
              </span>
              <div>
                <strong>Status</strong>
                <p>Your new password is now active.</p>
              </div>
            </div>
          </div>
        </div>

        <div className={styles.successActions}>
          <Link href={AUTH_ROUTES.signIn} className={styles.submit}>
            <IconArrowRight size={18} />
            <span>Back to Login</span>
          </Link>
        </div>
      </div>
    </AuthBackgroundShell>
  );
};

export default PasswordResetSuccessImgPage;
