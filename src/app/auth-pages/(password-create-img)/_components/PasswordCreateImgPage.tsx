"use client";

import React, { useState } from "react";
import Link from "next/link";
import { Spinner } from "reactstrap";
import { createPassword } from "@/lib/auth/action";
import AuthBackgroundShell from "@/app/auth-pages/_components/AuthBackgroundShell";
import styles from "@/app/auth-pages/_components/AuthBackgroundShell.module.scss";

const PasswordCreateImgPage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);

  return (
    <AuthBackgroundShell
      eyebrow="Security Setup"
      title="Create Password"
      subtitle="Your new password should be different from previous passwords and strong enough to protect your full PolytronX workspace."
      footer={
        <>
          Need to go back?{" "}
          <Link
            className={styles.link}
            href="/auth-pages/sign-in-with-bg-image"
          >
            Return to sign in
          </Link>
        </>
      }
    >
      <form action={createPassword} onSubmit={() => setIsSubmitting(true)}>
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
          {isSubmitting ? <Spinner size="sm" /> : "Create Password"}
        </button>
      </form>
    </AuthBackgroundShell>
  );
};

export default PasswordCreateImgPage;
