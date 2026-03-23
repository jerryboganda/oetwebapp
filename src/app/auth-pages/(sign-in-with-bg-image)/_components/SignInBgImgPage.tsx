"use client";

import React, { useState } from "react";
import Link from "next/link";
import {
  IconBrandFacebook,
  IconBrandGithub,
  IconBrandGoogle,
} from "@tabler/icons-react";
import { Spinner } from "reactstrap";
import { loginUserImg } from "@/lib/auth/action";
import AuthBackgroundShell from "@/app/auth-pages/_components/AuthBackgroundShell";
import styles from "@/app/auth-pages/_components/AuthBackgroundShell.module.scss";

const SignInBgImgPage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);

  return (
    <AuthBackgroundShell
      eyebrow="Welcome Back"
      title="Login to your Account"
      subtitle="Get started with PolytronX, access your workspace, and continue exactly where you left off."
      footer={
        <>
          New here?{" "}
          <Link
            className={styles.link}
            href="/auth-pages/sign-up-with-bg-image"
          >
            Create an account
          </Link>
        </>
      }
      terms={
        <Link className={styles.link} href="/other-pages/terms-condition">
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
          label: "Sign in with GitHub",
          icon: <IconBrandGithub size={18} />,
        },
      ]}
    >
      <form action={loginUserImg} onSubmit={() => setIsSubmitting(true)}>
        <div className={styles.field}>
          <label htmlFor="username">Email address</label>
          <input
            id="username"
            name="username"
            type="email"
            className={styles.input}
            placeholder="Enter your email address"
            required
          />
          <p className={styles.fieldHint}>
            We&apos;ll never share your email with anyone else.
          </p>
        </div>

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

        <div className={styles.metaRow}>
          <label className={styles.checkbox} htmlFor="remember">
            <input id="remember" name="remember" type="checkbox" />
            <span>Remember me</span>
          </label>

          <Link className={styles.link} href="/auth-pages/password-reset-img">
            Forgot password?
          </Link>
        </div>

        <button className={styles.submit} type="submit" disabled={isSubmitting}>
          {isSubmitting ? <Spinner size="sm" /> : "Submit"}
        </button>
      </form>
    </AuthBackgroundShell>
  );
};

export default SignInBgImgPage;
