"use client";

import React, { useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Spinner } from "reactstrap";
import { completePasswordReset } from "@/lib/auth/action";
import AuthBackgroundShell from "@/app/auth-pages/_components/AuthBackgroundShell";
import PasswordField from "@/app/auth-pages/_components/PasswordField";
import styles from "@/app/auth-pages/_components/AuthBackgroundShell.module.scss";
import { AUTH_ROUTES, getAuthFlowLinks } from "@/lib/auth/routes";

const errorCopy: Record<string, string> = {
  "password-mismatch": "Your passwords must match before you continue.",
  "password-too-short": "Your new password must be at least 8 characters long.",
};

const PasswordCreateImgPage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const searchParams = useSearchParams();
  const flowLinks = getAuthFlowLinks("passwordCreate");
  const email = searchParams.get("email") ?? "";
  const error = searchParams.get("error") ?? "";

  return (
    <AuthBackgroundShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Create New Password"
      title="Set Your New Password"
      subtitle={`Step 3 of 3. Create a fresh password for ${email} and return securely to your OET sign-in.`}
      footer={
        <>
          Need to go back?{" "}
          <Link className={styles.link} href={flowLinks.primary}>
            Return to sign in
          </Link>
        </>
      }
    >
      <form
        action={completePasswordReset}
        onSubmit={() => setIsSubmitting(true)}
        className={styles.passwordFlowForm}
      >
        <input type="hidden" name="email" value={email} />

        <div className={styles.summaryCard}>
          <h4>Password Reset Context</h4>
          <p>{email}</p>
          <p>This password change applies to your verified reset request.</p>
        </div>

        <PasswordField
          id="newPassword"
          name="newPassword"
          label="New Password"
          placeholder="Enter your new password"
          required
        />

        <PasswordField
          id="confirmPassword"
          name="confirmPassword"
          label="Confirm Password"
          placeholder="Confirm your new password"
          required
        />

        {errorCopy[error] ? (
          <p className={styles.fieldHint} style={{ color: "#c23d69" }}>
            {errorCopy[error]}
          </p>
        ) : null}

        <button
          className={`${styles.submit} ${styles.passwordFlowSubmit}`}
          type="submit"
          disabled={isSubmitting}
        >
          {isSubmitting ? <Spinner size="sm" /> : "Reset Password"}
        </button>

        <div className={`${styles.footer} ${styles.passwordFlowFooter}`}>
          <Link className={styles.link} href={AUTH_ROUTES.signIn}>
            Back to sign in
          </Link>
        </div>
      </form>
    </AuthBackgroundShell>
  );
};

export default PasswordCreateImgPage;
