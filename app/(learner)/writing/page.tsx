'use client';

import { useEffect } from 'react';
import { useTranslations } from 'next-intl';
import {
  PenTool,
  Library,
  History,
  ArrowRight,
  type LucideIcon,
} from 'lucide-react';
import { CardLink } from '@/components/ui/card-link';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { CreditsGuideButton } from '@/components/domain';
import { FreeSampleLauncher } from '@/components/domain/free-sample-launcher';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { analytics } from '@/lib/analytics';

interface WritingLandingCard {
  key: string;
  href: string;
  icon: LucideIcon;
  titleKey: string;
  descriptionKey: string;
  ctaKey: string;
  accent: string;
}

/** Primary V2 writing flows: practice library + past submissions, side by side. */
const START_CARDS: WritingLandingCard[] = [
  {
    key: 'practice',
    href: '/writing/practice/library',
    icon: Library,
    titleKey: 'writing.hub.cards.practice.title',
    descriptionKey: 'writing.hub.cards.practice.description',
    ctaKey: 'writing.hub.cards.practice.cta',
    accent: 'bg-lavender text-primary',
  },
  {
    key: 'submissions',
    href: '/submissions?subtest=writing',
    icon: History,
    titleKey: 'writing.hub.cards.submissions.title',
    descriptionKey: 'writing.hub.cards.submissions.description',
    ctaKey: 'writing.hub.cards.submissions.cta',
    accent: 'bg-background-light text-muted',
  },
];

/** The whole card is the link (bigger tap target); the CTA line is its visible affordance. */
function WritingLandingCardItem({ card }: { card: WritingLandingCard }) {
  const t = useTranslations();
  const Icon = card.icon;
  return (
    <CardLink href={card.href} className="flex h-full flex-col">
      <span className={`inline-flex h-10 w-10 items-center justify-center rounded-xl ${card.accent}`}>
        <Icon className="h-5 w-5" aria-hidden="true" />
      </span>
      <h3 className="mt-3 text-base font-bold text-navy">{t(card.titleKey)}</h3>
      <p className="mt-1 flex-1 text-sm leading-snug text-muted">{t(card.descriptionKey)}</p>
      {card.key === 'practice' ? (
        <p className="mt-2 text-xs font-semibold text-muted" data-testid="writing-credit-cost-note">
          {t('writing.hub.cards.practice.creditNote')}
        </p>
      ) : null}
      <span className="mt-4 inline-flex items-center gap-1.5 text-sm font-bold text-primary">
        {t(card.ctaKey)}
        <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
      </span>
    </CardLink>
  );
}

export default function WritingHome() {
  const t = useTranslations();

  useEffect(() => {
    analytics.track('module_entry', { module: 'writing' });
  }, []);

  return (
    <>
      <LearnerPageHero
        eyebrow={t('writing.hub.eyebrow')}
        icon={PenTool}
        accent="writing"
        title={t('writing.hub.hero.title')}
        description={t('writing.hub.hero.description')}
      />

      <CreditsGuideButton variant="banner" />

      <LearnerSkillSwitcher compact />

      <MotionSection delayIndex={0}>
        <section className="space-y-4" data-tour="writing-hub">
          <LearnerSurfaceSectionHeader
            eyebrow={t('writing.hub.start.eyebrow')}
            title={t('writing.hub.start.title')}
            description={t('writing.hub.start.description')}
          />
          {/* Free Mocks: the FIRST action under Start Writing — one free AI-graded
              letter per learner, for the learner's own profession (22 Sep 2026
              handoff: no cross-profession picker). Renders nothing unless the
              server offers a sample. */}
          <FreeSampleLauncher
            subtest="writing"
            icon={PenTool}
            testId="writing-free-mock-card"
            title={t('writing.hub.freeSample.title')}
            description={t('writing.hub.freeSample.description')}
            badgeLabel={t('writing.hub.freeSample.badge')}
            className=""
          />
          <ul className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            {START_CARDS.map((card, index) => (
              <li key={card.key}>
                <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                  <WritingLandingCardItem card={card} />
                </MotionItem>
              </li>
            ))}
          </ul>
        </section>
      </MotionSection>
    </>
  );
}
