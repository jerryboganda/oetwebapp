"use client";

import React, { useState } from "react";
import { Spinner } from "reactstrap";
import { unlockScreenImg } from "@/lib/auth/action";
import AuthBackgroundShell from "@/app/auth-pages/_components/AuthBackgroundShell";
import styles from "@/app/auth-pages/_components/AuthBackgroundShell.module.scss";

const LockScreenImgPage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);

  return (
    <AuthBackgroundShell
      eyebrow="Session Locked"
      title="Unlock your Workspace"
      subtitle="Hello, Sunny Airey. Enter your password to unlock the screen and return to your dashboard."
      hero={
        <div className={styles.avatar}>
          <img src="/images/ai_avatar/3.jpg" alt="Sunny Airey" />
        </div>
      }
    >
      <form action={unlockScreenImg} onSubmit={() => setIsSubmitting(true)}>
        <div className={styles.field}>
          <label htmlFor="password">Password</label>
          <input
            id="password"
            name="password"
            type="password"
            className={styles.input}
            placeholder="Enter your password"
            required
          />
        </div>

        <label className={styles.checkbox} htmlFor="rememberMe">
          <input id="rememberMe" name="rememberMe" type="checkbox" />
          <span>Remember me on this device</span>
        </label>

        <button className={styles.submit} type="submit" disabled={isSubmitting}>
          {isSubmitting ? <Spinner size="sm" /> : "Unlock"}
        </button>
      </form>
    </AuthBackgroundShell>
  );
};

export default LockScreenImgPage;
