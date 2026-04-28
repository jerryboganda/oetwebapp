'use client';

import Image from 'next/image';
import Link from 'next/link';
import type { ReactNode } from 'react';
import styles from './auth-screen-shell.module.scss';

interface SocialAction {
  href: string;
  icon: ReactNode;
  label: string;
}

interface AuthScreenShellProps {
  title: string;
  subtitle?: string;
  children: ReactNode;
  hero?: ReactNode;
  eyebrow?: string;
  footer?: ReactNode;
  terms?: ReactNode;
  socials?: SocialAction[];
  brandHref?: string;
  brandLabel?: string;
  stackClassName?: string;
  cardClassName?: string;
  headerClassName?: string;
}

function isActionHref(href: string) {
  return href.startsWith('/api/') || href.includes('/api/');
}

export function AuthScreenShell({
  title,
  subtitle,
  children,
  hero,
  eyebrow = 'OET Access',
  footer,
  terms,
  socials,
  brandHref = '/sign-in',
  brandLabel = 'OET',
  stackClassName,
  cardClassName,
  headerClassName,
}: AuthScreenShellProps) {
  return (
    <div className={styles.page}>
      <div className={`${styles.shape} ${styles.triangleTop}`} />
      <div className={`${styles.shape} ${styles.triangleLeft}`} />
      <div className={`${styles.shape} ${styles.triangleBottom}`} />
      <div className={`${styles.shape} ${styles.dot}`} />
      <div className={`${styles.shape} ${styles.blobLeft}`} />
      <div className={`${styles.shape} ${styles.blobRight}`} />

      <main className={styles.content}>
        <div className={`${styles.stack} ${stackClassName ?? ''}`.trim()}>
          <Link className={styles.brand} href={brandHref} aria-label={brandLabel}>
            <Image
              src="/brand/oet-with-dr-hesham-logo.png"
              alt="OET with Dr Ahmed Hesham"
              width={400}
              height={140}
              priority
              className={styles.brandLogo}
            />
          </Link>

          <div className={`${styles.card} ${cardClassName ?? ''}`.trim()}>
            <div className={`${styles.header} ${headerClassName ?? ''}`.trim()}>
              <span className={styles.eyebrow}>{eyebrow}</span>
              <h1 className={styles.title}>{title}</h1>
              {subtitle ? <p className={styles.subtitle}>{subtitle}</p> : null}
            </div>

            {hero ? <div className={styles.hero}>{hero}</div> : null}

            <div className={styles.form}>{children}</div>

            {socials?.length ? (
              <>
                <div className={styles.divider}>OR</div>
                <div className={styles.socials}>
                  {socials.map((social) => (
                    isActionHref(social.href) ? (
                      <a
                        key={social.label}
                        href={social.href}
                        aria-label={social.label}
                        className={styles.social}
                      >
                        {social.icon}
                      </a>
                    ) : (
                      <Link
                        key={social.label}
                        href={social.href}
                        prefetch={false}
                        aria-label={social.label}
                        className={styles.social}
                      >
                        {social.icon}
                      </Link>
                    )
                  ))}
                </div>
              </>
            ) : null}

            {footer ? <div className={styles.footer}>{footer}</div> : null}
            {terms ? <div className={styles.terms}>{terms}</div> : null}
          </div>
        </div>
      </main>
    </div>
  );
}
