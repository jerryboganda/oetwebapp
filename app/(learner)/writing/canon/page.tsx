'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { BookOpen, Search, ShieldCheck } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, cardClassName } from '@/components/ui/card';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { getWritingCanon, type WritingCanonDto } from '@/lib/writing-pathway-api';
import { cn } from '@/lib/utils';

export default function WritingCanonPage() {
  const t = useTranslations();
  const [canon, setCanon] = useState<WritingCanonDto | null>(null);
  const [search, setSearch] = useState('');
  const [severity, setSeverity] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const handle = window.setTimeout(() => {
      getWritingCanon({ search, severity: severity || undefined })
        .then(setCanon)
        .catch(() => setError(t('writing.canon.library.error.load')));
    }, 200);
    return () => window.clearTimeout(handle);
  }, [search, severity, t]);

  const violationLookup = useMemo(() => new Map((canon?.recentViolations ?? []).map((v) => [v.ruleId, v])), [canon]);

  const controlClassName = 'min-h-11 rounded-control border border-border bg-background text-sm text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary';

  return (
    <>
      <LearnerPageHero
        eyebrow={t('writing.canon.library.eyebrow')}
        icon={BookOpen}
        accent="amber"
        title={t('writing.canon.library.hero.title')}
        description={t('writing.canon.library.hero.description')}
        highlights={[
          { icon: BookOpen, label: t('writing.canon.library.highlights.rules'), value: `${canon?.totalRules ?? 0}` },
          { icon: ShieldCheck, label: t('writing.canon.library.highlights.recentFlags'), value: `${canon?.totalRecentViolations ?? 0}` },
        ]}
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <MotionSection delayIndex={0}>
        <Card padding="lg">
          <LearnerSurfaceSectionHeader
            eyebrow={t('writing.canon.library.browse.eyebrow')}
            title={t('writing.canon.library.browse.title')}
            description={t('writing.canon.library.browse.description')}
            className="mb-4"
          />
          <div className="grid grid-cols-1 gap-3 md:grid-cols-[minmax(0,1fr)_220px]">
            <label className="relative block">
              {/* Use logical inline-start so the icon flips for RTL chrome. */}
              <Search className="pointer-events-none absolute start-3 top-3 h-5 w-5 text-muted" aria-hidden="true" />
              <input
                type="search"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder={t('writing.canon.library.browse.searchPlaceholder')}
                aria-label={t('writing.canon.library.browse.searchPlaceholder')}
                className={`${controlClassName} w-full ps-10 pe-3`}
              />
            </label>
            <select
              value={severity}
              onChange={(event) => setSeverity(event.target.value)}
              aria-label={t('writing.canon.library.browse.severityLabel')}
              className={`${controlClassName} px-3`}
            >
              <option value="">{t('writing.canon.library.browse.severityAll')}</option>
              <option value="critical">{t('writing.canon.library.browse.severityCritical')}</option>
              <option value="major">{t('writing.canon.library.browse.severityMajor')}</option>
              <option value="minor">{t('writing.canon.library.browse.severityMinor')}</option>
              <option value="info">{t('writing.canon.library.browse.severityInfo')}</option>
            </select>
          </div>
        </Card>
      </MotionSection>

      {!canon && !error ? (
        <LearnerSkeleton variant="card-grid" />
      ) : (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          {(canon?.rules ?? []).map((rule, index) => {
            const stat = violationLookup.get(rule.ruleId);
            return (
              <MotionItem key={rule.ruleId} delayIndex={Math.min(index, 5)} className="min-w-0">
                <article className={cn(cardClassName({ padding: 'md' }), 'h-full')}>
                  <div className="mb-3 flex flex-wrap items-center gap-2">
                    <Badge variant="info" size="sm">{rule.ruleId}</Badge>
                    <Badge variant={rule.severity === 'critical' ? 'danger' : rule.severity === 'major' ? 'warning' : 'muted'} size="sm">{rule.severity}</Badge>
                    <Badge variant="muted" size="sm">{rule.category}</Badge>
                    {stat ? <Badge variant="warning" size="sm">{t('writing.canon.library.card.seen', { count: stat.count })}</Badge> : null}
                  </div>
                  {/* Canon rule text + examples are Dr Ahmed's authored English content — spec §32. */}
                  <h2 className="break-words text-base font-bold text-navy" dir="ltr">{rule.ruleText}</h2>
                  {rule.correctExamples.length > 0 || rule.incorrectExamples.length > 0 ? (
                    <div className="mt-4 grid grid-cols-1 gap-3 text-sm md:grid-cols-2">
                      {rule.correctExamples.length > 0 ? (
                        <div className="min-w-0">
                          <p className="mb-1 font-semibold text-success-strong">{t('writing.canon.library.card.correct')}</p>
                          <p className="break-words text-muted" dir="ltr">{rule.correctExamples[0]}</p>
                        </div>
                      ) : null}
                      {rule.incorrectExamples.length > 0 ? (
                        <div className="min-w-0">
                          <p className="mb-1 font-semibold text-danger-strong">{t('writing.canon.library.card.avoid')}</p>
                          <p className="break-words text-muted" dir="ltr">{rule.incorrectExamples[0]}</p>
                        </div>
                      ) : null}
                    </div>
                  ) : null}
                  {rule.lessonHref ? <Button asChild size="sm" variant="outline" className="mt-4"><Link href={rule.lessonHref}>{t('writing.canon.library.card.practise')}</Link></Button> : null}
                </article>
              </MotionItem>
            );
          })}
        </div>
      )}
    </>
  );
}