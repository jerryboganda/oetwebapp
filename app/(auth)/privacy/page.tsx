import type { Metadata } from 'next';
import Link from 'next/link';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import shellStyles from '@/components/auth/auth-screen-shell.module.scss';
import legalStyles from '../../terms/terms.module.scss';
import { AUTH_ROUTES } from '@/lib/auth/routes';

export const metadata: Metadata = {
  title: 'Privacy Notice · OET with Dr Ahmed Hesham',
  description:
    'How OET with Dr Ahmed Hesham collects, uses, retains, and protects your personal data — written under UK GDPR.',
};

const LAST_UPDATED = '26 April 2026';
const EFFECTIVE_FROM = '26 April 2026';

interface LegalSection {
  id: string;
  title: string;
  lead?: string;
  bullets?: Array<string | React.ReactNode>;
  callout?: React.ReactNode;
}

const sections: LegalSection[] = [
  {
    id: 'who-and-scope',
    title: 'Who this notice covers',
    lead:
      'This Privacy Notice explains how OET with Dr Ahmed Hesham ("we", "us", "our") processes personal data when you use our learner platform, mobile and desktop apps, websites, marketing emails, and support channels.',
    bullets: [
      'We act as the data controller for personal data you give us directly (account details, submissions, payment metadata).',
      'For some services we also process data on behalf of sponsors or institutions (e.g. employer-funded learners) — in those cases the sponsor is the controller and we are a processor under a written agreement.',
      'This notice is read alongside our Terms of Service.',
    ],
  },
  {
    id: 'what-we-collect',
    title: 'What we collect',
    bullets: [
      <>
        <strong>Account data —</strong> name, email, mobile number, country,
        password hash, MFA secrets, profession, target exam date.
      </>,
      <>
        <strong>Learning data —</strong> writing submissions, speaking and
        conversation audio recordings, mock-exam answers, AI feedback, expert
        review notes, study plan progress, predicted scores.
      </>,
      <>
        <strong>Billing data —</strong> Stripe customer / subscription IDs,
        plan, invoice history, partial card metadata (last 4, brand) — full
        card numbers stay with Stripe.
      </>,
      <>
        <strong>Device & technical data —</strong> IP address, user-agent,
        device model, OS, app version, language, time zone, crash and
        performance traces.
      </>,
      <>
        <strong>Communications —</strong> emails to support, feedback, in-app
        chat, satisfaction surveys.
      </>,
    ],
  },
  {
    id: 'lawful-bases',
    title: 'Why we process it (lawful bases)',
    bullets: [
      'Performance of contract — to provide the Service you signed up for, including AI feedback, tutor review, and mock scoring.',
      'Legitimate interests — keeping the Service secure, preventing abuse, improving features, measuring product quality, and running de-identified analytics.',
      'Legal obligation — accounting, tax, fraud prevention, response to lawful requests.',
      'Consent — optional marketing emails, product research interviews, and any non-essential cookies. You can withdraw consent at any time without affecting prior processing.',
    ],
  },
  {
    id: 'how-long',
    title: 'How long we keep it',
    bullets: [
      'Account data: while your account is active, plus up to 24 months after closure to handle re-activation, support disputes, and statutory obligations.',
      'Speaking and conversation audio: by default 30 days, configurable in Settings → Privacy. Transcripts and scores have longer retention (1 year) for progress tracking.',
      'Writing submissions and tutor feedback: retained while your account is active so you can revisit feedback over your prep journey.',
      'Billing records: 7 years (UK accounting requirement).',
      'Security logs: up to 90 days for incident investigation.',
    ],
  },
  {
    id: 'sharing',
    title: 'Who we share it with',
    lead:
      'We never sell personal data. We share it only with vetted processors and only as needed to run the Service:',
    bullets: [
      'Stripe — payments and subscription billing.',
      'Brevo — transactional and (where you opted in) marketing email delivery.',
      'AI providers — Azure OpenAI, OpenAI, Whisper, ElevenLabs, Deepgram, and others, selected per feature. We send the minimum data required (e.g. your submission, our grounded prompt) and contractually prohibit training on your content where the provider supports it.',
      'Sentry — application error reporting (no audio or full submissions are attached to error reports).',
      'Cloud infrastructure — our hosting provider in the UK / EU.',
      'Professional advisers, insurers, and authorities — when legally required.',
    ],
    callout: (
      <>
        <strong>International transfers —</strong> some providers operate
        outside the UK / EEA. Where they do, we rely on UK IDTA / EU Standard
        Contractual Clauses and additional safeguards as required.
      </>
    ),
  },
  {
    id: 'security',
    title: 'How we protect it',
    bullets: [
      'Encryption in transit (TLS 1.2+) and at rest for sensitive learner content.',
      'Two-step verification (MFA) for all admin and expert accounts; offered to learners.',
      'Granular role-based access — 16 distinct admin permissions; reviewers see only the submissions they are assigned.',
      'Refresh-token rotation, short-lived access tokens, IP & device anomaly detection.',
      'Annual penetration tests and quarterly security reviews.',
    ],
  },
  {
    id: 'your-rights',
    title: 'Your rights',
    lead:
      'Under UK GDPR (and equivalent regimes) you have the right to:',
    bullets: [
      'Access a copy of your personal data.',
      'Have inaccurate data corrected.',
      'Have your data deleted (subject to legal retention).',
      'Restrict or object to certain processing.',
      'Receive a portable export of data you provided.',
      'Withdraw consent for marketing or optional processing at any time.',
      'Lodge a complaint with the UK ICO (ico.org.uk) or your local supervisory authority.',
    ],
    callout: (
      <>
        Most rights can be exercised from <strong>Settings → Privacy</strong>{' '}
        or by emailing dpo@oetwithdrhesham.co.uk. We aim to respond within
        30 days.
      </>
    ),
  },
  {
    id: 'cookies',
    title: 'Cookies & similar technologies',
    bullets: [
      'Strictly necessary cookies — used for authentication, CSRF protection, and session continuity. These cannot be disabled without breaking sign-in.',
      'Functional storage — remembers your theme, language, and study-plan preferences locally.',
      'Analytics — privacy-respecting, aggregated, IP-truncated. No cross-site tracking. No third-party advertising cookies are set.',
    ],
  },
  {
    id: 'children',
    title: 'Children',
    bullets: [
      'The Service is not intended for under-16s. If you believe a child has registered, contact dpo@oetwithdrhesham.co.uk and we will delete the account.',
    ],
  },
  {
    id: 'changes',
    title: 'Changes to this notice',
    bullets: [
      'We may update this notice from time to time. Material changes will be notified by email and posted in-app at least 14 days in advance unless a faster change is required by law.',
    ],
  },
];

const tocItems = sections.map((s, i) => ({
  id: s.id,
  number: i + 1,
  title: s.title,
}));

export default function PrivacyPage() {
  return (
    <AuthScreenShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET with Dr Ahmed Hesham"
      eyebrow="Legal"
      title="Privacy Notice"
      subtitle="What we collect, why, how long we keep it, and the rights you have over your data."
      stackClassName={shellStyles.successStackWide}
      cardClassName={shellStyles.successCardWide}
      headerClassName={shellStyles.successHeaderWide}
      footer={
        <>
          Looking for our Terms?{' '}
          <Link className={shellStyles.link} href="/terms">
            Read them here
          </Link>
        </>
      }
    >
      <div className={legalStyles.shell}>
        <div className={legalStyles.meta}>
          <span className={legalStyles.metaPill}>Effective {EFFECTIVE_FROM}</span>
          <span>Last updated {LAST_UPDATED}</span>
          <span aria-hidden="true">·</span>
          <span>UK GDPR & Data Protection Act 2018</span>
        </div>

        <div className={legalStyles.layout}>
          <aside className={legalStyles.toc} aria-label="Table of contents">
            <p className={legalStyles.tocTitle}>On this page</p>
            {tocItems.map((item) => (
              <a
                key={item.id}
                className={legalStyles.tocLink}
                href={`#${item.id}`}
              >
                <span className={legalStyles.tocNumber}>{item.number}</span>
                <span>{item.title}</span>
              </a>
            ))}
          </aside>

          <div className={legalStyles.body}>
            {sections.map((section, index) => (
              <section
                key={section.id}
                id={section.id}
                className={legalStyles.section}
                aria-labelledby={`${section.id}-title`}
              >
                <header className={legalStyles.sectionHeader}>
                  <span className={legalStyles.sectionNumber} aria-hidden="true">
                    {index + 1}
                  </span>
                  <h2
                    id={`${section.id}-title`}
                    className={legalStyles.sectionTitle}
                  >
                    {section.title}
                  </h2>
                </header>

                {section.lead ? (
                  <p className={legalStyles.sectionLead}>{section.lead}</p>
                ) : null}

                {section.bullets?.length ? (
                  <ul className={legalStyles.sectionList}>
                    {section.bullets.map((bullet, i) => (
                      <li key={i}>{bullet}</li>
                    ))}
                  </ul>
                ) : null}

                {section.callout ? (
                  <p className={legalStyles.callout}>{section.callout}</p>
                ) : null}
              </section>
            ))}

            <section
              className={legalStyles.section}
              aria-labelledby="privacy-contact-title"
            >
              <header className={legalStyles.sectionHeader}>
                <span className={legalStyles.sectionNumber} aria-hidden="true">
                  {sections.length + 1}
                </span>
                <h2
                  id="privacy-contact-title"
                  className={legalStyles.sectionTitle}
                >
                  How to reach the Data Protection team
                </h2>
              </header>
              <div className={legalStyles.contactGrid}>
                <div className={legalStyles.contactCard}>
                  <span>Data Protection Officer</span>
                  <a href="mailto:dpo@oetwithdrhesham.co.uk">
                    dpo@oetwithdrhesham.co.uk
                  </a>
                </div>
                <div className={legalStyles.contactCard}>
                  <span>General support</span>
                  <a href="mailto:support@oetwithdrhesham.co.uk">
                    support@oetwithdrhesham.co.uk
                  </a>
                </div>
                <div className={legalStyles.contactCard}>
                  <span>UK supervisory authority</span>
                  <a
                    href="https://ico.org.uk"
                    target="_blank"
                    rel="noopener noreferrer"
                  >
                    ico.org.uk
                  </a>
                </div>
              </div>
            </section>
          </div>
        </div>

        <div className={legalStyles.actions}>
          <Link href={AUTH_ROUTES.signIn} className={shellStyles.submit}>
            Return to sign in
          </Link>
          <Link href="/terms" className={shellStyles.secondaryButton}>
            Terms of Service
          </Link>
          <Link href={AUTH_ROUTES.signUp} className={shellStyles.secondaryButton}>
            Create an account
          </Link>
        </div>
      </div>
    </AuthScreenShell>
  );
}
