'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { PenTool, Compass, BookOpen, Target, Award, ArrowRight, Sparkles } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { getWritingV2Profile } from '@/lib/writing/api';

const STAGES = [
  { code: 'onboarding', labelKey: 'writing.welcome.stages.onboarding', descriptionKey: 'writing.welcome.stages.onboardingDescription', icon: Compass },
  { code: 'foundation', labelKey: 'writing.welcome.stages.foundation', descriptionKey: 'writing.welcome.stages.foundationDescription', icon: BookOpen },
  { code: 'practice', labelKey: 'writing.welcome.stages.practice', descriptionKey: 'writing.welcome.stages.practiceDescription', icon: Target },
  { code: 'mastery', labelKey: 'writing.welcome.stages.mastery', descriptionKey: 'writing.welcome.stages.masteryDescription', icon: Award },
] as const;

export default function WritingWelcomePage() {
  const t = useTranslations();
  const router = useRouter();
  const [checking, setChecking] = useState(true);

  useEffect(() => {
    let cancelled = false;
    void getWritingV2Profile()
      .then((profile) => {
        if (cancelled) return;
        if (profile?.onboardingCompletedAt) {
          router.replace('/writing');
          return;
        }
        setChecking(false);
      })
      .catch(() => {
        if (!cancelled) setChecking(false);
      });
    return () => {
      cancelled = true;
    };
  }, [router]);

  return (
    <LearnerDashboardShell pageTitle={t('writing.welcome.title')}>
      <div className="space-y-5 sm:space-y-8" aria-busy={checking}>
        <LearnerPageHero
          eyebrow={t('writing.welcome.eyebrow')}
          icon={PenTool}
          accent="amber"
          title={t('writing.welcome.hero.title')}
          description={t('writing.welcome.hero.description')}
          highlights={[
            { icon: Sparkles, label: t('writing.welcome.highlights.pathway'), value: t('writing.welcome.highlights.pathwayValue') },
            { icon: Award, label: t('writing.welcome.highlights.target'), value: t('writing.welcome.highlights.targetValue') },
          ]}
        />

        <section
          aria-labelledby="pathway-stages-heading"
          className="rounded-2xl border border-border bg-surface p-5 shadow-sm"
        >
          <header className="mb-5">
            <h2 id="pathway-stages-heading" className="text-lg font-bold text-navy">
              {t('writing.welcome.pathway.heading')}
            </h2>
            <p className="mt-1 text-sm text-muted">
              {t('writing.welcome.pathway.subtitle')}
            </p>
          </header>

          <ol className="grid gap-3 md:grid-cols-2 lg:grid-cols-5" aria-label={t('writing.welcome.pathway.heading')}>
            {STAGES.map((stage, index) => {
              const Icon = stage.icon;
              const label = t(stage.labelKey);
              return (
                <li
                  key={stage.code}
                  className="flex flex-col gap-2 rounded-xl border border-border bg-background p-4"
                >
                  <div className="flex items-center gap-2">
                    <span className="inline-flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 text-xs font-bold text-primary">
                      {index + 1}
                    </span>
                    <Icon className="h-5 w-5 text-amber-600" aria-hidden="true" />
                    <Badge variant="muted" size="sm">{label}</Badge>
                  </div>
                  <p className="text-sm text-navy font-semibold">{label}</p>
                  <p className="text-xs text-muted leading-snug">{t(stage.descriptionKey)}</p>
                </li>
              );
            })}
          </ol>
        </section>

        <section className="flex flex-col items-start gap-3 rounded-2xl border border-border bg-surface p-5 shadow-sm sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h2 className="text-base font-bold text-navy">{t('writing.welcome.cta.heading')}</h2>
            <p className="mt-1 text-sm text-muted">
              {t('writing.welcome.cta.subtitle')}
            </p>
          </div>
          <Button asChild size="lg" disabled={checking}>
            <Link href="/writing/profile-setup/profession" aria-label={t('writing.welcome.cta.aria')}>
              {t('writing.welcome.cta.start')} <ArrowRight className="h-4 w-4" aria-hidden="true" />
            </Link>
          </Button>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
