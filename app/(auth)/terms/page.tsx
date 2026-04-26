import type { Metadata } from 'next';
import Link from 'next/link';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import shellStyles from '@/components/auth/auth-screen-shell.module.scss';
import legalStyles from './terms.module.scss';
import { AUTH_ROUTES } from '@/lib/auth/routes';

export const metadata: Metadata = {
  title: 'Terms of Service · OET with Dr Ahmed Hesham',
  description:
    'The terms of service governing use of the OET with Dr Ahmed Hesham preparation platform — eligibility, billing, the Score Guarantee, AI feedback, expert review, intellectual property, and your rights.',
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
    id: 'about',
    title: 'About these Terms',
    lead:
      'These Terms of Service form a binding agreement between you and OET with Dr Ahmed Hesham (&quot;we&quot;, &quot;us&quot;, &quot;our&quot;) covering your use of our learner platform, mobile and desktop apps, websites, and related services (together, the &quot;Service&quot;). By creating an account or using the Service you accept these Terms.',
    bullets: [
      <>
        These Terms apply alongside our <strong>Privacy Notice</strong>, the
        <strong> Score Guarantee Terms</strong> (where applicable), and any
        plan-specific terms shown at checkout.
      </>,
      'If you do not agree to these Terms, do not use the Service.',
      'We may amend these Terms from time to time; material changes will be communicated by email and surfaced in-app at least 14 days before they take effect, except where a faster change is required by law.',
    ],
  },
  {
    id: 'who-we-are',
    title: 'Who we are',
    lead:
      'OET with Dr Ahmed Hesham is an independent OET (Occupational English Test) preparation service operated for healthcare professionals preparing to register and practise abroad.',
    callout: (
      <>
        <strong>Important —</strong> we are not affiliated with, endorsed by, or
        a partner of Cambridge Boxhill Language Assessment (CBLA), the operator
        of the official OET examination. &quot;OET&quot; is referenced descriptively to
        identify the test we help you prepare for. The official OET website is
        <a
          href="https://www.oet.com"
          target="_blank"
          rel="noopener noreferrer"
        >
          {' '}
          oet.com
        </a>
        .
      </>
    ),
  },
  {
    id: 'eligibility',
    title: 'Eligibility & accounts',
    bullets: [
      'You must be at least 16 years old, or the age of digital consent in your jurisdiction (whichever is higher), to create a learner account.',
      'You agree to provide accurate, current, and complete information during registration and to keep your profile up to date.',
      'You are responsible for safeguarding your password, two-step verification (MFA) codes, and any device on which you remain signed in.',
      'You must notify us immediately of any unauthorised access by emailing support@oetwithdrhesham.co.uk so we can revoke sessions and refresh tokens on your account.',
      'One human, one account. Sharing accounts, automating logins, or selling access is prohibited.',
    ],
  },
  {
    id: 'subscriptions',
    title: 'Subscriptions, billing & refunds',
    lead:
      'Most features are available through paid subscriptions. Pricing, billing cadence, and renewal terms are shown at checkout and in your account billing settings.',
    bullets: [
      'Subscriptions auto-renew at the end of each billing period at the then-current rate unless you cancel before the renewal date. You can cancel anytime from Settings → Billing; cancellation takes effect at the end of the current period.',
      'Payments are processed by Stripe. We do not store full card details on our servers; only Stripe customer / subscription identifiers are retained.',
      'UK / EU consumers have a 14-day right of withdrawal under applicable consumer-protection law. Where you actively use the Service (open AI feedback, submit speaking attempts, request expert review, etc.) within that window, you expressly request immediate access and acknowledge that the withdrawal right is reduced proportionally to the value of services consumed.',
      'Subscriptions purchased through Apple App Store or Google Play are subject to those stores’ refund policies and may need to be cancelled within the relevant store account.',
      'Disputed charges should first be raised with us via support@oetwithdrhesham.co.uk so we can investigate before any chargeback.',
    ],
  },
  {
    id: 'score-guarantee',
    title: 'The Score Guarantee',
    lead:
      'Eligible learners on qualifying plans may enrol in our Score Guarantee programme, which provides additional preparation support if a target sub-score is not met on a qualifying official OET attempt.',
    bullets: [
      'Eligibility, qualifying activity (mocks, expert reviews, study-plan completion), and the redemption process are set out in the Score Guarantee Terms shown at enrolment and in Billing → Score Guarantee.',
      'The Score Guarantee is a preparation-support pledge — it is not a refund of fees paid to CBLA, immigration regulators, or third parties, and it is not a guarantee of registration with any regulatory body.',
      'Fraudulent activity (account sharing, falsified score reports, manipulated practice data) voids Score Guarantee eligibility immediately.',
    ],
  },
  {
    id: 'ai-and-experts',
    title: 'AI feedback & expert review',
    lead:
      'The Service uses a mix of automated AI feedback and human expert review to help you improve. Both are advisory and educational in nature.',
    bullets: [
      'AI-generated feedback (Writing, Speaking, Conversation, Pronunciation, Reading explanations, Grammar drafts) is grounded against our published rulebooks but may still contain mistakes. Treat it as a guided second opinion, not the final word.',
      'Expert reviewers are qualified human experts under contract; their feedback represents their professional opinion based on the OET rubric. Turnaround windows shown at submission are targets, not guarantees.',
      'Predicted scores, advisory bands, and readiness percentages are calibrated estimates — they do not guarantee any outcome on the official OET examination.',
      'You retain ownership of the work you submit (your essays, recordings, written responses). You grant us a non-exclusive, worldwide, royalty-free licence to process, store, and use that work for the purpose of operating the Service, generating feedback, training internal quality models on de-identified data, and improving the platform.',
    ],
  },
  {
    id: 'acceptable-use',
    title: 'Acceptable use',
    lead: 'When using the Service you agree not to:',
    bullets: [
      'Submit content that is unlawful, defamatory, hateful, or that infringes any third-party rights.',
      'Upload other people’s work, copyrighted exam material, or audio recordings of real patients without consent.',
      'Attempt to reverse-engineer, scrape, or systematically extract our content, AI prompts, rulebooks, or learner data.',
      'Probe, attack, or degrade the Service, including via automation, denial-of-service, or credential stuffing.',
      'Use the Service to cheat on the actual OET exam, impersonate another person, or submit AI-generated work as your own to a regulator.',
      'Resell, sublicense, or repackage the Service or its content in any form.',
    ],
  },
  {
    id: 'ip',
    title: 'Content & intellectual property',
    bullets: [
      'All Service content — including practice papers, rulebooks, model answers, scoring logic, AI prompts, design system, brand marks, and software — is owned by us or our licensors and protected by copyright and other intellectual-property laws.',
      'We grant you a personal, non-transferable, non-exclusive, revocable licence to access the Service for your own OET preparation while your subscription is active.',
      <>
        The &quot;OET with Dr Ahmed Hesham&quot; name and logo are our trademarks. The
        &quot;OET&quot; name is referenced under fair use to describe the test we prepare
        learners for; it remains the property of CBLA.
      </>,
      'If you believe any content on the Service infringes your rights, contact dpo@oetwithdrhesham.co.uk with a takedown request and we will review it promptly.',
    ],
  },
  {
    id: 'privacy',
    title: 'Privacy & data protection',
    lead:
      'We process personal data under UK GDPR and the Data Protection Act 2018 (and equivalent regimes in your jurisdiction). Our Privacy Notice explains what we collect, why, how long we retain it, and your rights.',
    bullets: [
      'Sensitive learner data — speaking recordings, written submissions, expert feedback — is stored encrypted at rest and is only accessed by personnel with a need-to-know basis.',
      'You can request export or deletion of your account data at any time from Settings → Privacy or by emailing dpo@oetwithdrhesham.co.uk.',
      'Audio retention is controlled by the configurable Pronunciation/Conversation retention windows set out in the in-app Privacy Notice; defaults are 30 days unless you change them.',
    ],
  },
  {
    id: 'third-parties',
    title: 'Third-party services',
    lead:
      'The Service integrates carefully selected third parties to deliver core functionality:',
    bullets: [
      'Stripe (payments)',
      'Brevo (transactional email)',
      'Azure / OpenAI / Whisper / ElevenLabs / Deepgram and other AI providers, selected per feature and disclosed in-app',
      'Sentry (error monitoring)',
    ],
    callout: (
      <>
        These providers act as our processors and process your data on our
        instructions. We never sell your personal data, and we do not allow
        third-party advertising or behavioural profiling on the Service.
      </>
    ),
  },
  {
    id: 'disclaimers',
    title: 'Disclaimers',
    bullets: [
      'The Service is provided on an "as is" and "as available" basis. Predicted scores, readiness percentages, AI feedback, and expert reviews are educational guidance and do not guarantee any specific outcome on the official OET, with any regulator, or for any registration, employment, immigration, or visa decision.',
      'We are not a medical, legal, immigration, or registration adviser. Decisions you make based on your preparation experience are your own.',
      'We do our best to maintain availability but cannot guarantee uninterrupted access. Planned maintenance and incidents are communicated via the in-app status banner where possible.',
    ],
  },
  {
    id: 'liability',
    title: 'Limitation of liability',
    bullets: [
      'Nothing in these Terms limits liability for death or personal injury caused by negligence, fraud, or any other liability that cannot lawfully be limited.',
      'Subject to the above, our total aggregate liability to you in connection with the Service in any 12-month period is limited to the greater of (a) the fees you actually paid us in that period or (b) £100.',
      'We are not liable for indirect or consequential losses, loss of profits, loss of opportunity, exam-fee costs, immigration costs, or registration delays.',
    ],
  },
  {
    id: 'termination',
    title: 'Suspension & termination',
    bullets: [
      'You may close your account at any time from Settings → Account.',
      'We may suspend or terminate access immediately if you breach these Terms, attempt to defraud the Service or other users, or pose a security risk to the platform.',
      'On termination, your access to paid features ends and we will delete or anonymise your data per the retention schedule in the Privacy Notice. Statutory retention obligations (tax, billing records, fraud investigation) override deletion where required by law.',
    ],
  },
  {
    id: 'governing-law',
    title: 'Governing law & disputes',
    bullets: [
      'These Terms are governed by the laws of England and Wales.',
      'The courts of England and Wales have exclusive jurisdiction, except that consumers resident in another UK nation, the EU, or other jurisdictions retain the protection of the mandatory consumer-protection rules of their place of habitual residence.',
      'Before bringing any claim, please email support@oetwithdrhesham.co.uk so we can attempt to resolve the matter quickly and fairly.',
    ],
  },
];

const tocItems = sections.map((s, i) => ({
  id: s.id,
  number: i + 1,
  title: s.title,
}));

export default function TermsPage() {
  return (
    <AuthScreenShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET with Dr Ahmed Hesham"
      eyebrow="Legal"
      title="Terms of Service"
      subtitle="The rules for using the OET with Dr Ahmed Hesham preparation platform — written in plain English, organised by topic."
      stackClassName={shellStyles.successStackWide}
      cardClassName={shellStyles.successCardWide}
      headerClassName={shellStyles.successHeaderWide}
      footer={
        <>
          Looking for our Privacy Notice?{' '}
          <Link className={shellStyles.link} href="/privacy">
            Read it here
          </Link>
        </>
      }
    >
      <div className={legalStyles.shell}>
        <div className={legalStyles.meta}>
          <span className={legalStyles.metaPill}>Effective {EFFECTIVE_FROM}</span>
          <span>Last updated {LAST_UPDATED}</span>
          <span aria-hidden="true">·</span>
          <span>Plain-English summary first, full terms below</span>
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
              aria-labelledby="contact-title"
            >
              <header className={legalStyles.sectionHeader}>
                <span className={legalStyles.sectionNumber} aria-hidden="true">
                  {sections.length + 1}
                </span>
                <h2 id="contact-title" className={legalStyles.sectionTitle}>
                  Contact us
                </h2>
              </header>
              <p className={legalStyles.sectionLead}>
                Questions about these Terms? Reach the right team directly.
              </p>
              <div className={legalStyles.contactGrid}>
                <div className={legalStyles.contactCard}>
                  <span>General support</span>
                  <a href="mailto:support@oetwithdrhesham.co.uk">
                    support@oetwithdrhesham.co.uk
                  </a>
                </div>
                <div className={legalStyles.contactCard}>
                  <span>Billing</span>
                  <a href="mailto:billing@oetwithdrhesham.co.uk">
                    billing@oetwithdrhesham.co.uk
                  </a>
                </div>
                <div className={legalStyles.contactCard}>
                  <span>Data Protection (DPO)</span>
                  <a href="mailto:dpo@oetwithdrhesham.co.uk">
                    dpo@oetwithdrhesham.co.uk
                  </a>
                </div>
                <div className={legalStyles.contactCard}>
                  <span>Legal notices</span>
                  <a href="mailto:legal@oetwithdrhesham.co.uk">
                    legal@oetwithdrhesham.co.uk
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
          <Link href={AUTH_ROUTES.signUp} className={shellStyles.secondaryButton}>
            Create an account
          </Link>
          <Link href="/privacy" className={shellStyles.secondaryButton}>
            Privacy Notice
          </Link>
        </div>
      </div>
    </AuthScreenShell>
  );
}

