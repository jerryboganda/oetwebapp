"use client";

import Link from "next/link";
import React, { useState } from "react";
import { Spinner } from "reactstrap";
import { unlockScreen } from "@/lib/auth/action";
import AuthBackgroundShell from "@/app/auth-pages/_components/AuthBackgroundShell";
import PasswordField from "@/app/auth-pages/_components/PasswordField";
import styles from "@/app/auth-pages/_components/AuthBackgroundShell.module.scss";
import { AUTH_ROUTES } from "@/lib/auth/routes";
import { MOCK_SESSION_USERS } from "@/lib/auth/session";

const LockScreenImgPage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const previewUser = MOCK_SESSION_USERS[0] ?? {
    username: "learner@oet.app",
    fullName: "Aisha Khan",
    role: "learner" as const,
    avatarUrl: "/images/ai_avatar/3.jpg",
  };

  return (
    <AuthBackgroundShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Session Locked"
      title="Unlock your Workspace"
      subtitle={`Hello, ${previewUser.fullName}. Enter your password to unlock the screen and return to your dashboard.`}
      hero={
        <div className={styles.avatar}>
          <img src={previewUser.avatarUrl} alt={previewUser.fullName} />
        </div>
      }
    >
      <form action={unlockScreen} onSubmit={() => setIsSubmitting(true)}>
        <PasswordField
          id="password"
          name="password"
          label="Password"
          placeholder="Enter your password"
          required
        />

        <label className={styles.checkbox} htmlFor="rememberMe">
          <input id="rememberMe" name="rememberMe" type="checkbox" />
          <span>Remember Me on this device</span>
        </label>

        <button className={styles.submit} type="submit" disabled={isSubmitting}>
          {isSubmitting ? <Spinner size="sm" /> : "Unlock"}
        </button>

        <div className={styles.footer}>
          <Link className={styles.link} href={AUTH_ROUTES.signIn}>
            Return to sign in
          </Link>
        </div>
      </form>
    </AuthBackgroundShell>
  );
};

export default LockScreenImgPage;
