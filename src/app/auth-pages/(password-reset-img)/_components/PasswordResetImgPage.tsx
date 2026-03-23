"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Spinner } from "reactstrap";
import { resetPassword } from "@/lib/auth/action";
import AuthBackgroundShell from "@/app/auth-pages/_components/AuthBackgroundShell";
import styles from "@/app/auth-pages/_components/AuthBackgroundShell.module.scss";

const PasswordResetImgPage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);

  return (
    <AuthBackgroundShell
      eyebrow="Recover Access"
      title="Reset Your Password"
      subtitle="Choose a fresh password to secure your account and get back into PolytronX without losing momentum."
      footer={
        <>
          Remembered it?{" "}
          <Link
            className={styles.link}
            href="/auth-pages/sign-in-with-bg-image"
          >
            Back to sign in
          </Link>
        </>
      }
    >
      <form action={resetPassword} onSubmit={() => setIsSubmitting(true)}>
        <div className={styles.field}>
          <label htmlFor="currentPassword">Current Password</label>
          <input
            id="currentPassword"
            name="currentPassword"
            type="password"
            className={styles.input}
            placeholder="Enter your current password"
            required
          />
        </div>

        <div className={styles.field}>
          <label htmlFor="newPassword">New Password</label>
          <input
            id="newPassword"
            name="newPassword"
            type="password"
            className={styles.input}
            placeholder="Enter your new password"
            required
          />
        </div>

        <div className={styles.field}>
          <label htmlFor="confirmPassword">Confirm Password</label>
          <input
            id="confirmPassword"
            name="confirmPassword"
            type="password"
            className={styles.input}
            placeholder="Confirm your new password"
            required
          />
        </div>

        <button className={styles.submit} type="submit" disabled={isSubmitting}>
          {isSubmitting ? <Spinner size="sm" /> : "Reset Password"}
        </button>
      </form>
    </AuthBackgroundShell>
  );
};

export default PasswordResetImgPage;
