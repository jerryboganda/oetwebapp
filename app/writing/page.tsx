'use client';

import { useEffect } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import {
  PenTool,
  Award,
  Library,
  History,
  ArrowRight,
  type LucideIcon,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
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
    accent: 'text-primary',
  },
  {
    key: 'submissions',
    href: '/submissions',
    icon: History,
    titleKey: 'writing.hub.cards.submissions.title',
    descriptionKey: 'writing.hub.cards.submissions.description',
    ctaKey: 'writing.hub.cards.submissions.cta',
    accent: 'text-muted',
  },
];

function WritingLandingCardItem({ card }: { card: WritingLandingCard }) {
  const t = useTranslations();
  const Icon = card.icon;
  return (
    <li>
      <Card padding="md" className="h-full">
        <CardContent className="flex h-full flex-col">
          <span className={`inline-flex h-10 w-10 items-center justify-center rounded-xl bg-background-light ${card.accent}`}>
            <Icon className="h-5 w-5" aria-hidden="true" />
          </span>
          <h3 className="mt-3 text-base font-bold text-navy">{t(card.titleKey)}</h3>
          <p className="mt-1 flex-1 text-sm leading-snug text-muted">{t(card.descriptionKey)}</p>
          <Link
            href={card.href}
            className="mt-4 inline-flex items-center gap-1.5 text-sm font-bold text-primary transition-colors hover:text-primary/80 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 rounded"
          >
            {t(card.ctaKey)}
            <ArrowRight className="h-4 w-4" aria-hidden="true" />
          </Link>
        </CardContent>
      </Card>
    </li>
  );
}

export default function WritingHome() {
  const t = useTranslations();

  useEffect(() => {
    analytics.track('module_entry', { module: 'writing' });
  }, []);

  return (
    <LearnerDashboardShell pageTitle={t('writing.hub.pageTitle')}>
      <div className="space-y-5 sm:space-y-8">
        <LearnerPageHero
          eyebrow={t('writing.hub.eyebrow')}
          icon={PenTool}
          accent="amber"
          title={t('writing.hub.hero.title')}
          description={t('writing.hub.hero.description')}
          highlights={[
            { icon: Award, label: t('writing.hub.highlights.mocks'), value: t('writing.hub.highlights.mocksValue') },
            { icon: Library, label: t('writing.hub.highlights.practice'), value: t('writing.hub.highlights.practiceValue') },
          ]}
        />

        <LearnerSkillSwitcher compact />

        <section className="space-y-4" data-tour="writing-hub">
          <LearnerSurfaceSectionHeader
            eyebrow={t('writing.hub.start.eyebrow')}
            title={t('writing.hub.start.title')}
            description={t('writing.hub.start.description')}
          />
          <ul className="grid gap-4 sm:grid-cols-2">
            {START_CARDS.map((card) => (
              <WritingLandingCardItem key={card.key} card={card} />
            ))}
          </ul>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
