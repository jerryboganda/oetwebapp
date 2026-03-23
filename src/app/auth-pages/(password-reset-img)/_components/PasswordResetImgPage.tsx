"use client";

import React, { useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Spinner } from "reactstrap";
import { requestPasswordResetOtp } from "@/lib/auth/action";
import AuthBackgroundShell from "@/app/auth-pages/_components/AuthBackgroundShell";
import styles from "@/app/auth-pages/_components/AuthBackgroundShell.module.scss";
import { AUTH_ROUTES, getAuthFlowLinks } from "@/lib/auth/routes";

const PasswordResetImgPage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const searchParams = useSearchParams();
  const flowLinks = getAuthFlowLinks("passwordReset");
  const email = searchParams.get("email") ?? "";
  const error = searchParams.get("error");

  const errorMessage =
    error === "missing-email" ? "Enter your email address to continue." : null;

  return (
    <AuthBackgroundShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Recover Access"
      title="Find Your Account"
      subtitle="Step 1 of 3. Enter your email address so we can send a reset OTP and verify your identity first."
      footer={
        <>
          Remembered your password?{" "}
          <Link className={styles.link} href={flowLinks.primary}>
            Back to sign in
          </Link>
        </>
      }
    >
      <form
        action={requestPasswordResetOtp}
        onSubmit={() => setIsSubmitting(true)}
        className={styles.passwordFlowForm}
      >
        <div className={styles.field}>
          <label htmlFor="email">Email Address</label>
          <input
            id="email"
            name="email"
            type="email"
            className={styles.input}
            placeholder="Enter your registered email"
            defaultValue={email}
            required
          />
          <p className={styles.fieldHint}>
            We&apos;ll send a 5 digit reset code to this email address.
          </p>
        </div>

        {errorMessage ? (
          <p className={styles.fieldHint} style={{ color: "#c23d69" }}>
            {errorMessage}
          </p>
        ) : null}

        <button
          className={`${styles.submit} ${styles.passwordFlowSubmit}`}
          type="submit"
          disabled={isSubmitting}
        >
          {isSubmitting ? <Spinner size="sm" /> : "Send OTP"}
        </button>

        <div className={`${styles.footer} ${styles.passwordFlowFooter}`}>
          <Link className={styles.link} href={flowLinks.primary}>
            Back to sign in
          </Link>
        </div>
      </form>
    </AuthBackgroundShell>
  );
};

export default PasswordResetImgPage;
