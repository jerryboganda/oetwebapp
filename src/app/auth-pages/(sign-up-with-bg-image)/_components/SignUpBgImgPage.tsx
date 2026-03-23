"use client";

import React, { FormEvent, useState } from "react";
import Link from "next/link";
import {
  IconBrandFacebook,
  IconBrandGithub,
  IconBrandGoogle,
} from "@tabler/icons-react";
import { Spinner } from "reactstrap";
import AuthBackgroundShell from "@/app/auth-pages/_components/AuthBackgroundShell";
import styles from "@/app/auth-pages/_components/AuthBackgroundShell.module.scss";

const SignUpBgImgPage = () => {
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setIsSubmitting(true);
    await new Promise((resolve) => setTimeout(resolve, 700));
    setIsSubmitting(false);
  };

  return (
    <AuthBackgroundShell
      eyebrow="Create Workspace"
      title="Open your PolytronX Account"
      subtitle="Create your account, set your credentials, and start using the full product with the same polished experience across every auth step."
      footer={
        <>
          Already have an account?{" "}
          <Link
            className={styles.link}
            href="/auth-pages/sign-in-with-bg-image"
          >
            Sign in
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
          label: "Sign up with Facebook",
          icon: <IconBrandFacebook size={18} />,
        },
        {
          href: "#",
          label: "Sign up with Google",
          icon: <IconBrandGoogle size={18} />,
        },
        {
          href: "#",
          label: "Sign up with GitHub",
          icon: <IconBrandGithub size={18} />,
        },
      ]}
    >
      <form onSubmit={handleSubmit}>
        <div className={styles.field}>
          <label htmlFor="username">Username</label>
          <input
            id="username"
            name="username"
            type="text"
            className={styles.input}
            placeholder="Choose a username"
            required
          />
        </div>

        <div className={styles.field}>
          <label htmlFor="email">Email</label>
          <input
            id="email"
            name="email"
            type="email"
            className={styles.input}
            placeholder="Enter your email"
            required
          />
        </div>

        <div className={styles.field}>
          <label htmlFor="password">Password</label>
          <input
            id="password"
            name="password"
            type="password"
            className={styles.input}
            placeholder="Create a password"
            required
          />
        </div>

        <div className={styles.field}>
          <label htmlFor="confirmPassword">Confirm password</label>
          <input
            id="confirmPassword"
            name="confirmPassword"
            type="password"
            className={styles.input}
            placeholder="Repeat your password"
            required
          />
        </div>

        <label className={styles.checkbox} htmlFor="rememberSignUp">
          <input id="rememberSignUp" name="rememberSignUp" type="checkbox" />
          <span>Remember this device</span>
        </label>

        <button className={styles.submit} type="submit" disabled={isSubmitting}>
          {isSubmitting ? <Spinner size="sm" /> : "Create account"}
        </button>
      </form>
    </AuthBackgroundShell>
  );
};

export default SignUpBgImgPage;
