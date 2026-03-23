"use client";

import React, { useState } from "react";
import Link from "next/link";
import {
  IconBrandFacebook,
  IconBrandGoogle,
  IconBrandLinkedin,
} from "@tabler/icons-react";
import { Spinner } from "reactstrap";
import { loginUser } from "@/lib/auth/action";
import AuthBackgroundShell from "@/app/auth-pages/_components/AuthBackgroundShell";
import AuthModeSwitch from "@/app/auth-pages/_components/AuthModeSwitch";
import PasswordField from "@/app/auth-pages/_components/PasswordField";
import styles from "@/app/auth-pages/_components/AuthBackgroundShell.module.scss";
import { AUTH_ROUTES, getAuthFlowLinks } from "@/lib/auth/routes";
import { MOCK_SESSION_USERS } from "@/lib/auth/session";

const SignInBgImgPage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const flowLinks = getAuthFlowLinks("signIn");
  const demoAccounts = [
    "learner@edu80.app",
    ...MOCK_SESSION_USERS.filter((user) => user.role !== "learner").map(
      (user) => user.username
    ),
  ];

  return (
    <AuthBackgroundShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Welcome Back"
      title="Login to your Account"
      footer={
        <>
          New here?{" "}
          <Link className={styles.link} href={flowLinks.primary}>
            Create an account
          </Link>
        </>
      }
      terms={
        <Link className={styles.link} href={AUTH_ROUTES.terms}>
          Terms of use &amp; Conditions
        </Link>
      }
      socials={[
        {
          href: "#",
          label: "Sign in with Facebook",
          icon: <IconBrandFacebook size={18} />,
        },
        {
          href: "#",
          label: "Sign in with Google",
          icon: <IconBrandGoogle size={18} />,
        },
        {
          href: "#",
          label: "Sign in with LinkedIn",
          icon: <IconBrandLinkedin size={18} />,
        },
      ]}
    >
      <form
        action={loginUser}
        onSubmit={() => setIsSubmitting(true)}
        className={styles.signInForm}
      >
        <AuthModeSwitch mode="signIn" />

        <div className={styles.field}>
          <label htmlFor="username">Email address</label>
          <input
            id="username"
            name="username"
            type="email"
            className={styles.input}
            placeholder="Enter your email address"
            defaultValue={demoAccounts[0]}
            list="demo-accounts"
            required
          />
          <datalist id="demo-accounts">
            {demoAccounts.map((account) => (
              <option key={account} value={account} />
            ))}
          </datalist>
          <p className={styles.fieldHint}>
            We&apos;ll never share your email with anyone else.
          </p>
        </div>

        <div className={styles.signInPasswordField}>
          <PasswordField
            id="password"
            name="password"
            label="Password"
            placeholder="Enter your password"
            required
          />
        </div>

        <div className={`${styles.metaRow} ${styles.signInMetaRow}`}>
          <label className={styles.checkbox} htmlFor="remember">
            <input id="remember" name="remember" type="checkbox" />
            <span>Remember Me</span>
          </label>

          <Link className={styles.link} href={flowLinks.secondary}>
            Forgot password?
          </Link>
        </div>

        <button
          className={`${styles.submit} ${styles.signInSubmit}`}
          type="submit"
          disabled={isSubmitting}
        >
          {isSubmitting ? <Spinner size="sm" /> : "Submit"}
        </button>
      </form>
    </AuthBackgroundShell>
  );
};

export default SignInBgImgPage;
