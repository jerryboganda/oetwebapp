"use client";

import React, { ChangeEvent, useRef, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Spinner } from "reactstrap";
import { verifyPasswordResetOtp } from "@/lib/auth/action";
import AuthBackgroundShell from "@/app/auth-pages/_components/AuthBackgroundShell";
import styles from "@/app/auth-pages/_components/AuthBackgroundShell.module.scss";
import { AUTH_ROUTES, getAuthFlowLinks } from "@/lib/auth/routes";

const PasswordResetOtpImgPage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [otp, setOtp] = useState<string[]>(Array(5).fill(""));
  const inputsRef = useRef<Array<HTMLInputElement | null>>([]);
  const searchParams = useSearchParams();
  const flowLinks = getAuthFlowLinks("passwordResetOtp");
  const email = searchParams.get("email") ?? "";
  const error = searchParams.get("error");

  const errorMessage =
    error === "invalid-otp"
      ? "The OTP is invalid. Use 12345 for the current mock reset flow."
      : null;

  const digitValidate = (
    event: ChangeEvent<HTMLInputElement>,
    index: number
  ) => {
    const sanitized = event.target.value.replace(/[^0-9]/g, "").slice(0, 1);
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
      eyebrow="Verify Reset OTP"
      title="Check Your Email"
      subtitle={`Step 2 of 3. Enter the 5 digit reset code sent to ${email} so we can unlock the new-password screen.`}
      footer={
        <p className={styles.resend}>
          Need to use another email?{" "}
          <Link className={styles.link} href={flowLinks.primary}>
            Go back
          </Link>
        </p>
      }
    >
      <form
        action={verifyPasswordResetOtp}
        onSubmit={() => setIsSubmitting(true)}
        className={styles.passwordFlowForm}
      >
        <input type="hidden" name="email" value={email} />

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
              aria-label={`Reset OTP digit ${index + 1}`}
              onChange={(event) => digitValidate(event, index)}
            />
          ))}
        </div>

        <p className={styles.fieldHint}>
          For the current mock flow, use OTP <strong>12345</strong>.
        </p>

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
          {isSubmitting ? <Spinner size="sm" /> : "Verify OTP"}
        </button>

        <div className={styles.footer}>
          <button
            type="button"
            className={styles.linkButton}
            onClick={() => setOtp(Array(5).fill(""))}
          >
            Clear and resend
          </button>
        </div>
      </form>
    </AuthBackgroundShell>
  );
};

export default PasswordResetOtpImgPage;
