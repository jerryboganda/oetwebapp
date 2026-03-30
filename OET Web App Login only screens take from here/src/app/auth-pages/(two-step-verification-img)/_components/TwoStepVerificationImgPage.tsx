"use client";

import React, { ChangeEvent, useRef, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { verifyOtp } from "@/lib/auth/action";
import AuthBackgroundShell from "@/app/auth-pages/_components/AuthBackgroundShell";
import styles from "@/app/auth-pages/_components/AuthBackgroundShell.module.scss";
import { AUTH_ROUTES } from "@/lib/auth/routes";

const TwoStepVerificationImgPage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [otp, setOtp] = useState<string[]>(Array(5).fill(""));
  const inputsRef = useRef<Array<HTMLInputElement | null>>([]);
  const searchParams = useSearchParams();
  const username = searchParams.get("username") ?? "learner@edu80.app";
  const error = searchParams.get("error");

  const errorMessage =
    error === "invalid-otp"
      ? "The OTP is invalid. Use 12345 for the mock sign-in verification flow."
      : null;

  const digitValidate = (e: ChangeEvent<HTMLInputElement>, index: number) => {
    const sanitized = e.target.value.replace(/[^0-9]/g, "").slice(0, 1);
    const nextOtp = [...otp];
    nextOtp[index] = sanitized;
    setOtp(nextOtp);

    if (sanitized && index < inputsRef.current.length - 1) {
      inputsRef.current[index + 1]?.focus();
    } else if (!sanitized && index > 0) {
      inputsRef.current[index - 1]?.focus();
    }
  };

  return (
    <AuthBackgroundShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Step Verification"
      title="Verify OTP"
      subtitle={`Enter the 5 digit verification code sent to ${username} to continue into your account.`}
      footer={
        <p className={styles.resend}>
          Did not receive a code?{" "}
          <button
            type="button"
            className={styles.link}
            onClick={() => setOtp(Array(5).fill(""))}
          >
            Resend it
          </button>
        </p>
      }
    >
      <form action={verifyOtp} onSubmit={() => setIsSubmitting(true)}>
        <input type="hidden" name="username" value={username} />
        <div className={styles.otpGrid}>
          {[0, 1, 2, 3, 4].map((_, index) => (
            <input
              key={index}
              type="text"
              name={`otp-${index}`}
              maxLength={1}
              inputMode="numeric"
              value={otp[index]}
              ref={(element) => {
                inputsRef.current[index] = element;
              }}
              className={styles.otpInput}
              aria-label={`OTP digit ${index + 1}`}
              onChange={(event) => digitValidate(event, index)}
            />
          ))}
        </div>

        <p className={styles.fieldHint}>
          For the current mock auth flow, use OTP <strong>12345</strong>.
        </p>

        {errorMessage ? (
          <p className={`${styles.notice} ${styles.noticeDanger}`}>
            {errorMessage}
          </p>
        ) : null}

        <button className={styles.submit} type="submit" disabled={isSubmitting}>
          {isSubmitting ? "Verifying..." : "Verify OTP"}
        </button>

        <div className={styles.footer}>
          <Link className={styles.link} href={AUTH_ROUTES.signIn}>
            Back to sign in
          </Link>
        </div>
      </form>
    </AuthBackgroundShell>
  );
};

export default TwoStepVerificationImgPage;
