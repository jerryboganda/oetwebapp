'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { ArrowLeft, BookOpen, CheckCircle2, XCircle } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { getMyCanonViolationsForRule, getWritingCanonRule } from '@/lib/writing/api';
import type {
  WritingCanonRuleV2Dto,
  WritingCanonViolationDto,
  WritingSeverity,
} from '@/lib/writing/types';

const SEVERITY_TONE: Record<WritingSeverity, { badge: 'danger' | 'warning' | 'muted'; labelKey: string }> = {
  high: { badge: 'danger', labelKey: 'writing.canon.detail.severity.high' },
  medium: { badge: 'warning', labelKey: 'writing.canon.detail.severity.medium' },
  low: { badge: 'muted', labelKey: 'writing.canon.detail.severity.low' },
};

export default function WritingCanonRuleDetailPage() {
  const t = useTranslations();
  const params = useParams<{ ruleId: string }>();
  const ruleId = String(params?.ruleId ?? '');
  const [rule, setRule] = useState<WritingCanonRuleV2Dto | null>(null);
  const [violations, setViolations] = useState<WritingCanonViolationDto[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!ruleId) return;
    let cancelled = false;
    void Promise.all([
      getWritingCanonRule(ruleId),
      getMyCanonViolationsForRule(ruleId).catch(() => ({ items: [] as WritingCanonViolationDto[] })),
    ])
      .then(([r, v]) => {
        if (cancelled) return;
        setRule(r);
        setViolations(v.items);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : t('writing.canon.detail.error.load'));
      });
    return () => {
      cancelled = true;
    };
  }, [ruleId, t]);

  const tone = rule ? SEVERITY_TONE[rule.severity] : null;
  const personalCount = violations.length;

  return (
    <LearnerDashboardShell pageTitle={rule?.id ?? t('writing.canon.detail.pageTitleFallback')}>
      <div className="space-y-6" aria-busy={!rule}>
        <LearnerPageHero
          eyebrow={t('writing.canon.detail.eyebrow', { version: rule?.version ?? '—' })}
          icon={BookOpen}
          accent="amber"
          // Rule id is a canonical identifier (e.g. "PURPOSE-01") — keep verbatim.
          title={rule?.id ?? t('writing.canon.detail.pageTitleFallback')}
          // Rule text is Dr Ahmed's authored English canon content (spec §32).
          description={rule?.ruleText ?? t('writing.canon.detail.descriptionLoading')}
          highlights={rule ? [
            { icon: BookOpen, label: t('writing.canon.detail.fields.category'), value: rule.category },
            { icon: CheckCircle2, label: t('writing.canon.detail.fields.active'), value: rule.active ? t('writing.canon.detail.fields.activeYes') : t('writing.canon.detail.fields.activeNo') },
          ] : []}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {rule ? (
          <section aria-labelledby="meta-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <header className="flex items-center justify-between gap-2">
              <h2 id="meta-heading" className="text-base font-bold text-navy">{t('writing.canon.detail.metadataTitle')}</h2>
              {tone ? <Badge variant={tone.badge} size="sm">{t(tone.labelKey)}</Badge> : null}
            </header>
            <dl className="mt-3 grid gap-3 sm:grid-cols-3 text-sm">
              <div>
                <dt className="text-xs font-bold uppercase tracking-wider text-muted">{t('writing.canon.detail.fields.detection')}</dt>
                <dd className="mt-1 font-bold text-navy capitalize">{rule.detectionType}</dd>
              </div>
              <div>
                <dt className="text-xs font-bold uppercase tracking-wider text-muted">{t('writing.canon.detail.fields.letterTypes')}</dt>
                <dd className="mt-1 flex flex-wrap gap-1">
                  {rule.appliesToLetterTypes.length > 0
                    ? rule.appliesToLetterTypes.map((lt) => <Badge key={lt} variant="muted" size="sm">{lt}</Badge>)
                    : <Badge variant="muted" size="sm">{t('writing.canon.detail.fields.all')}</Badge>}
                </dd>
              </div>
              <div>
                <dt className="text-xs font-bold uppercase tracking-wider text-muted">{t('writing.canon.detail.fields.professions')}</dt>
                <dd className="mt-1 flex flex-wrap gap-1">
                  {rule.appliesToProfessions.length > 0
                    ? rule.appliesToProfessions.map((p) => <Badge key={p} variant="info" size="sm" className="capitalize">{p}</Badge>)
                    : <Badge variant="info" size="sm">{t('writing.canon.detail.fields.all')}</Badge>}
                </dd>
              </div>
            </dl>
          </section>
        ) : null}

        {rule ? (
          <section aria-labelledby="examples-heading" className="grid gap-4 md:grid-cols-2">
            <h2 id="examples-heading" className="sr-only">{t('writing.canon.detail.examples.title')}</h2>
            <Card padding="md" className="border-emerald-200/70 bg-emerald-50/40">
              <CardContent>
                <h3 className="flex items-center gap-2 text-sm font-bold text-emerald-800">
                  <CheckCircle2 className="h-4 w-4" aria-hidden="true" /> {t('writing.canon.detail.correct')}
                </h3>
                <ul className="mt-2 space-y-2">
                  {rule.correctExamples.length === 0 ? <li className="text-xs text-muted">{t('writing.canon.detail.examples.empty')}</li> : null}
                  {/* Examples are authored English canon content (spec §32). */}
                  {rule.correctExamples.map((ex, idx) => (
                    <li key={idx} className="rounded border border-emerald-200/60 bg-white p-2 text-xs text-emerald-900" dir="ltr">
                      {ex}
                    </li>
                  ))}
                </ul>
              </CardContent>
            </Card>
            <Card padding="md" className="border-red-200/70 bg-red-50/40">
              <CardContent>
                <h3 className="flex items-center gap-2 text-sm font-bold text-red-800">
                  <XCircle className="h-4 w-4" aria-hidden="true" /> {t('writing.canon.detail.incorrect')}
                </h3>
                <ul className="mt-2 space-y-2">
                  {rule.incorrectExamples.length === 0 ? <li className="text-xs text-muted">{t('writing.canon.detail.examples.empty')}</li> : null}
                  {rule.incorrectExamples.map((ex, idx) => (
                    <li key={idx} className="rounded border border-red-200/60 bg-white p-2 text-xs text-red-900" dir="ltr">
                      {ex}
                    </li>
                  ))}
                </ul>
              </CardContent>
            </Card>
          </section>
        ) : null}

        {rule?.lessonId ? (
          <Card padding="md">
            <CardContent>
              <div className="flex items-center justify-between gap-2">
                <p className="text-sm text-navy">{t('writing.canon.detail.lessonPrompt')}</p>
                <Button asChild size="sm">
                  <Link href={`/writing/lessons/${encodeURIComponent(rule.lessonId)}`}>{t('writing.canon.detail.lessonOpen')}</Link>
                </Button>
              </div>
            </CardContent>
          </Card>
        ) : null}

        <section aria-labelledby="history-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <h2 id="history-heading" className="text-base font-bold text-navy">{t('writing.canon.detail.history.heading')}</h2>
          <p className="mt-1 text-sm text-muted">
            {t('writing.canon.detail.history.summary', { count: personalCount })}
          </p>
          {violations.length > 0 ? (
            <ul className="mt-3 space-y-2">
              {violations.slice(0, 5).map((v) => (
                <li key={v.id} className="rounded-lg border border-border bg-background p-3 text-sm">
                  <Link href={`/writing/submissions/${encodeURIComponent(v.submissionId)}/results`} className="font-bold text-primary underline">
                    {t('writing.canon.detail.history.submission', { id: v.submissionId.slice(0, 8) })}
                  </Link>
                  {/* Snippet is verbatim from the learner letter (English). */}
                  <span className="ms-1 text-xs text-muted" dir="ltr">— line {v.lineNumber}: "{v.snippet}"</span>
                </li>
              ))}
            </ul>
          ) : null}
          <div className="mt-3">
            <Button asChild variant="outline" size="sm">
              <Link href="/writing/canon"><ArrowLeft className="h-3 w-3" aria-hidden="true" /> {t('writing.canon.detail.back')}</Link>
            </Button>
          </div>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
