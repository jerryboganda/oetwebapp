"use client";

import React, { ChangeEvent, useRef, useState } from "react";
import Link from "next/link";
import { Spinner } from "reactstrap";
import { verifyOtp } from "@/lib/auth/action";
import AuthBackgroundShell from "@/app/auth-pages/_components/AuthBackgroundShell";
import styles from "@/app/auth-pages/_components/AuthBackgroundShell.module.scss";

const TwoStepVerificationImgPage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const inputsRef = useRef<Array<HTMLInputElement | null>>([]);

  const digitValidate = (e: ChangeEvent<HTMLInputElement>, index: number) => {
    const newValue = e.target.value.replace(/[^0-9]/g, "");
    e.target.value = newValue;

    if (newValue && index < inputsRef.current.length - 1) {
      inputsRef.current[index + 1]?.focus();
    } else if (!newValue && index > 0) {
      inputsRef.current[index - 1]?.focus();
    }
  };

  return (
    <AuthBackgroundShell
      eyebrow="Step Verification"
      title="Verify OTP"
      subtitle="Enter the 5 digit verification code sent to your registered email address to continue into your account."
      footer={
        <p className={styles.resend}>
          Did not receive a code? <Link href="#">Resend it</Link>
        </p>
      }
    >
      <form action={verifyOtp} onSubmit={() => setIsSubmitting(true)}>
        <div className={styles.otpGrid}>
          {[0, 1, 2, 3, 4].map((_, index) => (
            <input
              key={index}
              type="text"
              name={`otp-${index}`}
              maxLength={1}
              inputMode="numeric"
              ref={(element) => {
                inputsRef.current[index] = element;
              }}
              className={styles.otpInput}
              aria-label={`OTP digit ${index + 1}`}
              onChange={(event) => digitValidate(event, index)}
            />
          ))}
        </div>

        <button className={styles.submit} type="submit" disabled={isSubmitting}>
          {isSubmitting ? <Spinner size="sm" /> : "Verify"}
        </button>
      </form>
    </AuthBackgroundShell>
  );
};

export default TwoStepVerificationImgPage;
