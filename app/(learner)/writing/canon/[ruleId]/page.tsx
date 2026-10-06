'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { ArrowLeft, BookOpen, CheckCircle2, XCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, cardClassName } from '@/components/ui/card';
import { MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { getMyCanonViolationsForRule, getWritingCanonRule } from '@/lib/writing/api';
import { cleanCandidateText, plainCategoryLabel } from '@/lib/writing/candidate-text';
import { toCandidateSafeWritingErrorMessage } from '@/lib/writing/submit-keys';
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

// Rules from the rulebook bridge list "all" or a plain name, not an LT code: only a real code has a translated label.
const LETTER_TYPE_CODE = /^LT-(?:RR|UR|DG|TR|NM|OT)$/;

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
        setError(toCandidateSafeWritingErrorMessage(err, t('writing.canon.detail.error.load')));
      });
    return () => {
      cancelled = true;
    };
  }, [ruleId, t]);

  const tone = rule ? SEVERITY_TONE[rule.severity] : null;
  const personalCount = violations.length;

  return (
    <>
      <LearnerPageHero
        eyebrow={t('writing.canon.detail.eyebrow')}
        icon={BookOpen}
        accent="writing"
        // The rule id and canon version are internal; the title is the plain category.
        title={rule ? plainCategoryLabel(rule.category) || t('writing.canon.detail.pageTitleFallback') : t('writing.canon.detail.pageTitleFallback')}
        // Rule text is Dr Ahmed's authored English guidance (spec §32).
        description={rule ? cleanCandidateText(rule.ruleText) : t('writing.canon.detail.descriptionLoading')}
        highlights={rule ? [
          { icon: BookOpen, label: t('writing.canon.detail.fields.category'), value: plainCategoryLabel(rule.category) },
        ] : []}
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {!rule && !error ? <LearnerSkeleton variant="card-grid" /> : null}

      {rule ? (
        <MotionSection delayIndex={0}>
          <section aria-labelledby="meta-heading" className={cardClassName({ padding: 'lg' })}>
            <header className="flex flex-wrap items-center justify-between gap-2">
              <h2 id="meta-heading" className="text-base font-bold text-navy">{t('writing.canon.detail.metadataTitle')}</h2>
              {tone ? <Badge variant={tone.badge} size="sm">{t(tone.labelKey)}</Badge> : null}
            </header>
            <dl className="mt-3 grid grid-cols-1 gap-3 text-sm sm:grid-cols-2">
              <div className="min-w-0">
                <dt className="eyebrow text-muted">{t('writing.canon.detail.fields.letterTypes')}</dt>
                <dd className="mt-1 flex flex-wrap gap-1">
                  {rule.appliesToLetterTypes.length > 0
                    ? rule.appliesToLetterTypes.map((lt) => <Badge key={lt} variant="muted" size="sm">{LETTER_TYPE_CODE.test(lt) ? t(`writing.practice.library.letterType.${lt}`) : plainCategoryLabel(lt)}</Badge>)
                    : <Badge variant="muted" size="sm">{t('writing.canon.detail.fields.all')}</Badge>}
                </dd>
              </div>
              <div className="min-w-0">
                <dt className="eyebrow text-muted">{t('writing.canon.detail.fields.professions')}</dt>
                <dd className="mt-1 flex flex-wrap gap-1">
                  {rule.appliesToProfessions.length > 0
                    ? rule.appliesToProfessions.map((p) => <Badge key={p} variant="info" size="sm" className="capitalize">{p}</Badge>)
                    : <Badge variant="info" size="sm">{t('writing.canon.detail.fields.all')}</Badge>}
                </dd>
              </div>
            </dl>
          </section>
        </MotionSection>
      ) : null}

      {rule ? (
        <MotionSection delayIndex={1}>
          <section aria-labelledby="examples-heading" className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <h2 id="examples-heading" className="sr-only">{t('writing.canon.detail.examples.title')}</h2>
            <Card padding="md" className="min-w-0 border-success/20 bg-success/10">
              <h3 className="flex items-center gap-2 text-sm font-bold text-success-strong">
                <CheckCircle2 className="h-4 w-4" aria-hidden="true" /> {t('writing.canon.detail.correct')}
              </h3>
              <ul className="mt-2 space-y-2">
                {rule.correctExamples.length === 0 ? <li className="text-xs text-muted">{t('writing.canon.detail.examples.empty')}</li> : null}
                {/* Examples are authored English canon content (spec §32). */}
                {rule.correctExamples.map((ex, idx) => (
                  <li key={idx} className="rounded-control border border-success/20 bg-surface p-2 text-xs text-success-strong" dir="ltr">
                    {ex}
                  </li>
                ))}
              </ul>
            </Card>
            <Card padding="md" className="min-w-0 border-danger/20 bg-danger/10">
              <h3 className="flex items-center gap-2 text-sm font-bold text-danger-strong">
                <XCircle className="h-4 w-4" aria-hidden="true" /> {t('writing.canon.detail.incorrect')}
              </h3>
              <ul className="mt-2 space-y-2">
                {rule.incorrectExamples.length === 0 ? <li className="text-xs text-muted">{t('writing.canon.detail.examples.empty')}</li> : null}
                {rule.incorrectExamples.map((ex, idx) => (
                  <li key={idx} className="rounded-control border border-danger/20 bg-surface p-2 text-xs text-danger-strong" dir="ltr">
                    {ex}
                  </li>
                ))}
              </ul>
            </Card>
          </section>
        </MotionSection>
      ) : null}

      {rule?.lessonId ? (
        <Card padding="md">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="text-sm text-navy">{t('writing.canon.detail.lessonPrompt')}</p>
            <Button asChild size="sm">
              <Link href={`/writing/lessons/${encodeURIComponent(rule.lessonId)}`}>{t('writing.canon.detail.lessonOpen')}</Link>
            </Button>
          </div>
        </Card>
      ) : null}

      <MotionSection delayIndex={2}>
        <section aria-labelledby="history-heading" className={cardClassName({ padding: 'lg' })}>
          <h2 id="history-heading" className="text-base font-bold text-navy">{t('writing.canon.detail.history.heading')}</h2>
          <p className="mt-1 text-sm text-muted">
            {t('writing.canon.detail.history.summary', { count: personalCount })}
          </p>
          {violations.length > 0 ? (
            <ul className="mt-3 divide-y divide-border">
              {violations.slice(0, 5).map((v) => (
                <li key={v.id} className="py-2.5 text-sm first:pt-0 last:pb-0">
                  <Link href={`/writing/submissions/${encodeURIComponent(v.submissionId)}/results`} className="rounded font-bold text-primary underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary">
                    {t('writing.canon.detail.history.submission')}
                  </Link>
                  {/* Snippet is verbatim from the learner letter (English). */}
                  <span className="ms-1 text-xs text-muted" dir="ltr">· line {v.lineNumber}: &quot;{v.snippet}&quot;</span>
                </li>
              ))}
            </ul>
          ) : null}
          <div className="mt-3">
            <Button asChild variant="outline" size="sm">
              <Link href="/writing/canon"><ArrowLeft className="h-3 w-3 rtl:rotate-180" aria-hidden="true" /> {t('writing.canon.detail.back')}</Link>
            </Button>
          </div>
        </section>
      </MotionSection>
    </>
  );
}
