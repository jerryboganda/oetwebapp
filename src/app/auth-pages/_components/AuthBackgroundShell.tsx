"use client";

import Link from "next/link";
import React, { ReactNode } from "react";
import styles from "./AuthBackgroundShell.module.scss";

interface SocialAction {
  href: string;
  icon: ReactNode;
  label: string;
}

interface AuthBackgroundShellProps {
  title: string;
  subtitle: string;
  children: ReactNode;
  hero?: ReactNode;
  eyebrow?: string;
  footer?: ReactNode;
  terms?: ReactNode;
  socials?: SocialAction[];
}

const AuthBackgroundShell = ({
  title,
  subtitle,
  children,
  hero,
  eyebrow = "PolytronX Access",
  footer,
  terms,
  socials,
}: AuthBackgroundShellProps) => {
  return (
    <div className={styles.page}>
      <div className={`${styles.shape} ${styles.triangleTop}`} />
      <div className={`${styles.shape} ${styles.triangleLeft}`} />
      <div className={`${styles.shape} ${styles.triangleBottom}`} />
      <div className={`${styles.shape} ${styles.dot}`} />
      <div className={`${styles.shape} ${styles.blobLeft}`} />
      <div className={`${styles.shape} ${styles.blobRight}`} />

      <div className={styles.content}>
        <div className={styles.stack}>
          <Link className={styles.brand} href="/">
            <img src="/images/logo/polytronx-dark.svg" alt="PolytronX" />
          </Link>

          <div className={styles.card}>
            <div className={styles.header}>
              <span className={styles.eyebrow}>{eyebrow}</span>
              <h1 className={styles.title}>{title}</h1>
              <p className={styles.subtitle}>{subtitle}</p>
            </div>

            {hero ? <div className={styles.hero}>{hero}</div> : null}

            <div className={styles.form}>{children}</div>

            {socials?.length ? (
              <>
                <div className={styles.divider}>OR</div>
                <div className={styles.socials}>
                  {socials.map((social) => (
                    <Link
                      key={social.label}
                      href={social.href}
                      aria-label={social.label}
                      className={styles.social}
                    >
                      {social.icon}
                    </Link>
                  ))}
                </div>
              </>
            ) : null}

            {footer ? <div className={styles.footer}>{footer}</div> : null}
            {terms ? <div className={styles.terms}>{terms}</div> : null}
          </div>
        </div>
      </div>
    </div>
  );
};

export default AuthBackgroundShell;
