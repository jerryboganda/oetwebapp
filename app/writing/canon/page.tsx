'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { BookOpen, Search, ShieldCheck } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { getWritingCanon, type WritingCanonDto } from '@/lib/writing-pathway-api';

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

  return (
    <LearnerDashboardShell pageTitle={t('writing.canon.library.pageTitle')}>
      <div className="space-y-8">
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

        <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <LearnerSurfaceSectionHeader
            eyebrow={t('writing.canon.library.browse.eyebrow')}
            title={t('writing.canon.library.browse.title')}
            description={t('writing.canon.library.browse.description')}
            className="mb-4"
          />
          <div className="grid gap-3 md:grid-cols-[1fr_220px]">
            <label className="relative block">
              {/* Use logical inline-start so the icon flips for RTL chrome. */}
              <Search className="pointer-events-none absolute start-3 top-3 h-5 w-5 text-muted" />
              <input value={search} onChange={(event) => setSearch(event.target.value)} placeholder={t('writing.canon.library.browse.searchPlaceholder')} className="min-h-11 w-full rounded-lg border border-border bg-background ps-10 pe-3 text-sm" />
            </label>
            <select value={severity} onChange={(event) => setSeverity(event.target.value)} className="min-h-11 rounded-lg border border-border bg-background px-3 text-sm">
              <option value="">{t('writing.canon.library.browse.severityAll')}</option>
              <option value="critical">{t('writing.canon.library.browse.severityCritical')}</option>
              <option value="major">{t('writing.canon.library.browse.severityMajor')}</option>
              <option value="minor">{t('writing.canon.library.browse.severityMinor')}</option>
              <option value="info">{t('writing.canon.library.browse.severityInfo')}</option>
            </select>
          </div>
        </section>

        <div className="grid gap-4 lg:grid-cols-2">
          {(canon?.rules ?? []).map((rule) => {
            const stat = violationLookup.get(rule.ruleId);
            return (
              <article key={rule.ruleId} className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
                <div className="mb-3 flex flex-wrap items-center gap-2">
                  <Badge variant="info" size="sm">{rule.ruleId}</Badge>
                  <Badge variant={rule.severity === 'critical' ? 'danger' : rule.severity === 'major' ? 'warning' : 'muted'} size="sm">{rule.severity}</Badge>
                  <Badge variant="muted" size="sm">{rule.category}</Badge>
                  {stat ? <Badge variant="warning" size="sm">{t('writing.canon.library.card.seen', { count: stat.count })}</Badge> : null}
                </div>
                {/* Canon rule text + examples are Dr Ahmed's authored English content — spec §32. */}
                <h2 className="text-base font-bold text-navy" dir="ltr">{rule.ruleText}</h2>
                {rule.correctExamples.length > 0 || rule.incorrectExamples.length > 0 ? (
                  <div className="mt-4 grid gap-3 text-sm md:grid-cols-2">
                    {rule.correctExamples.length > 0 ? (
                      <div>
                        <p className="mb-1 font-semibold text-success">{t('writing.canon.library.card.correct')}</p>
                        <p className="text-muted" dir="ltr">{rule.correctExamples[0]}</p>
                      </div>
                    ) : null}
                    {rule.incorrectExamples.length > 0 ? (
                      <div>
                        <p className="mb-1 font-semibold text-danger">{t('writing.canon.library.card.avoid')}</p>
                        <p className="text-muted" dir="ltr">{rule.incorrectExamples[0]}</p>
                      </div>
                    ) : null}
                  </div>
                ) : null}
                {rule.lessonHref ? <Button asChild size="sm" variant="outline" className="mt-4"><Link href={rule.lessonHref}>{t('writing.canon.library.card.practise')}</Link></Button> : null}
              </article>
            );
          })}
        </div>
      </div>
    </LearnerDashboardShell>
  );
}