'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { PenTool, Compass, BookOpen, Target, Award, ArrowRight } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card, cardClassName } from '@/components/ui/card';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
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
    <>
      {/* No highlight chips: "AI-driven" and "B / B+ / A" were static copy, not data. */}
      <LearnerPageHero
        eyebrow={t('writing.welcome.eyebrow')}
        icon={PenTool}
        accent="amber"
        title={t('writing.welcome.hero.title')}
        description={t('writing.welcome.hero.description')}
      />

      <MotionSection delayIndex={0}>
        <section
          aria-labelledby="pathway-stages-heading"
          className={cardClassName({ padding: 'lg' })}
        >
          <header className="mb-5">
            <h2 id="pathway-stages-heading" className="text-lg font-bold text-navy">
              {t('writing.welcome.pathway.heading')}
            </h2>
            <p className="mt-1 text-sm text-muted">
              {t('writing.welcome.pathway.subtitle')}
            </p>
          </header>

          <ol className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4" aria-label={t('writing.welcome.pathway.heading')}>
            {STAGES.map((stage, index) => {
              const Icon = stage.icon;
              return (
                <li key={stage.code} className="min-w-0">
                  <MotionItem delayIndex={Math.min(index, 5)} className="flex h-full flex-col gap-2 rounded-xl bg-background-light p-4">
                    <div className="flex items-center gap-2">
                      <span className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-primary/10 text-xs font-bold tabular-nums text-primary">
                        {index + 1}
                      </span>
                      <Icon className="h-5 w-5 text-warning-strong" aria-hidden="true" />
                    </div>
                    <h3 className="text-sm font-semibold text-navy">{t(stage.labelKey)}</h3>
                    <p className="text-xs leading-snug text-muted">{t(stage.descriptionKey)}</p>
                  </MotionItem>
                </li>
              );
            })}
          </ol>
        </section>
      </MotionSection>

      <MotionSection delayIndex={1}>
        <Card padding="lg" className="flex flex-col items-start gap-3 sm:flex-row sm:items-center sm:justify-between" aria-busy={checking}>
          <div className="min-w-0">
            <h2 className="text-base font-bold text-navy">{t('writing.welcome.cta.heading')}</h2>
            <p className="mt-1 text-sm text-muted">
              {t('writing.welcome.cta.subtitle')}
            </p>
          </div>
          <Button asChild size="lg" disabled={checking}>
            <Link href="/writing/profile-setup/profession" aria-label={t('writing.welcome.cta.aria')}>
              {t('writing.welcome.cta.start')} <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
            </Link>
          </Button>
        </Card>
      </MotionSection>
    </>
  );
}
