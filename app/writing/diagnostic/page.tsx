'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { ClipboardCheck, PlayCircle, Clock, BookOpen, AlertTriangle, FileText, Route } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { getWritingV2Profile, startWritingDiagnostic } from '@/lib/writing/api';
import type { WritingProfileDto } from '@/lib/writing/types';

const TIMELINE = [
  { labelKey: 'writing.diagnostic.briefing.timeline.readingLabel', headingKey: 'writing.diagnostic.briefing.timeline.reading', bodyKey: 'writing.diagnostic.briefing.timeline.readingBody', icon: BookOpen },
  { labelKey: 'writing.diagnostic.briefing.timeline.writingLabel', headingKey: 'writing.diagnostic.briefing.timeline.writing', bodyKey: 'writing.diagnostic.briefing.timeline.writingBody', icon: FileText },
  { labelKey: 'writing.diagnostic.briefing.timeline.gradingLabel', headingKey: 'writing.diagnostic.briefing.timeline.grading', bodyKey: 'writing.diagnostic.briefing.timeline.gradingBody', icon: ClipboardCheck },
] as const;

export default function WritingDiagnosticBriefingPage() {
  const t = useTranslations();
  const router = useRouter();
  const [profile, setProfile] = useState<WritingProfileDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void getWritingV2Profile()
      .then((p) => {
        if (cancelled) return;
        setProfile(p);
      })
      .catch(() => {
        if (cancelled) return;
        setError(t('writing.diagnostic.briefing.error.profile'));
      })
      .finally(() => {
        if (cancelled) return;
        setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [t]);

  const onStart = async () => {
    if (starting) return;
    setStarting(true);
    setError(null);
    try {
      const session = await startWritingDiagnostic();
      router.push(`/writing/diagnostic/session/${encodeURIComponent(session.id)}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('writing.diagnostic.briefing.error.start'));
      setStarting(false);
    }
  };

  return (
    <LearnerDashboardShell pageTitle={t('writing.diagnostic.briefing.pageTitle')}>
      <div className="space-y-6" aria-busy={loading}>
        <LearnerPageHero
          eyebrow={t('writing.diagnostic.briefing.title')}
          icon={ClipboardCheck}
          accent="amber"
          title={t('writing.diagnostic.briefing.hero.title')}
          description={t('writing.diagnostic.briefing.hero.description')}
          highlights={[
            { icon: Clock, label: t('writing.diagnostic.briefing.highlights.totalTime'), value: t('writing.diagnostic.briefing.highlights.totalTimeValue') },
            { icon: Route, label: t('writing.diagnostic.briefing.highlights.stage'), value: profile?.currentStage ?? '—' },
            { icon: ClipboardCheck, label: t('writing.diagnostic.briefing.highlights.status'), value: profile?.diagnosticCompleted ? t('writing.diagnostic.briefing.status.completed') : t('writing.diagnostic.briefing.status.pending') },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <section
          aria-labelledby="format-heading"
          className="rounded-2xl border border-border bg-surface p-5 shadow-sm"
        >
          <header className="mb-4">
            <h2 id="format-heading" className="text-lg font-bold text-navy">{t('writing.diagnostic.briefing.format.heading')}</h2>
            <p className="mt-1 text-sm text-muted">
              {t('writing.diagnostic.briefing.format.subtitle')}
            </p>
          </header>

          <ol className="grid gap-3 md:grid-cols-3" aria-label={t('writing.diagnostic.briefing.format.heading')}>
            {TIMELINE.map((step) => {
              const Icon = step.icon;
              return (
                <li key={step.headingKey} className="rounded-xl border border-border bg-background p-4">
                  <div className="flex items-center gap-2">
                    <Badge variant="warning" size="sm">{t(step.labelKey)}</Badge>
                    <Icon className="h-5 w-5 text-amber-600" aria-hidden="true" />
                  </div>
                  <h3 className="mt-2 text-sm font-bold text-navy">{t(step.headingKey)}</h3>
                  <p className="mt-1 text-xs text-muted leading-snug">{t(step.bodyKey)}</p>
                </li>
              );
            })}
          </ol>

          <div className="mt-5 rounded-xl border border-amber-200/70 bg-amber-50/60 p-4 text-sm text-amber-900">
            <div className="flex items-start gap-2">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <div>
                <p className="font-bold">{t('writing.diagnostic.briefing.strict.title')}</p>
                <ul className="mt-1 ms-4 list-disc text-xs">
                  <li>{t('writing.diagnostic.briefing.strict.item1')}</li>
                  <li>{t('writing.diagnostic.briefing.strict.item2')}</li>
                  <li>{t('writing.diagnostic.briefing.strict.item3')}</li>
                </ul>
              </div>
            </div>
          </div>
        </section>

        <section className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-5 shadow-sm sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h2 className="text-base font-bold text-navy">{t('writing.diagnostic.briefing.ready.title')}</h2>
            <p className="mt-1 text-sm text-muted">{t('writing.diagnostic.briefing.ready.subtitle')}</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button asChild variant="outline">
              <Link href="/writing">{t('writing.diagnostic.briefing.back')}</Link>
            </Button>
            <Button onClick={onStart} loading={starting} disabled={loading}>
              <PlayCircle className="h-4 w-4" aria-hidden="true" />
              {t('writing.diagnostic.briefing.cta')}
            </Button>
          </div>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
