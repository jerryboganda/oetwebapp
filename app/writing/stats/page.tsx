'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { BarChart3, Calendar, Clock, Flame, Target } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { BandHistoryChart } from '@/components/domain/writing/BandHistoryChart';
import { CriteriaRadar } from '@/components/domain/writing/CriteriaRadar';
import { ReadinessWidget } from '@/components/domain/writing/ReadinessWidget';
import {
  exportWritingStats,
  getWritingReadiness,
  getWritingStatsBands,
  getWritingStatsCalendar,
  getWritingStatsCanon,
  getWritingStatsCriteria,
  getWritingStatsDashboard,
  getWritingStatsLetterTypes,
  getWritingStatsSkills,
  getWritingStatsTime,
} from '@/lib/writing/api';
import type {
  WritingReadinessScoreDto,
  WritingStatsBandsDto,
  WritingStatsCalendarDto,
  WritingStatsCanonDto,
  WritingStatsCriteriaDto,
  WritingStatsDashboardDto,
  WritingStatsLetterTypesDto,
  WritingStatsSkillsDto,
  WritingStatsTimeDto,
  WritingSubSkill,
} from '@/lib/writing/types';

const SKILL_LABELS: Record<WritingSubSkill, string> = {
  W1: 'W1', W2: 'W2', W3: 'W3', W4: 'W4', W5: 'W5', W6: 'W6', W7: 'W7', W8: 'W8',
};

export default function WritingStatsPage() {
  const t = useTranslations();
  const [dashboard, setDashboard] = useState<WritingStatsDashboardDto | null>(null);
  const [bands, setBands] = useState<WritingStatsBandsDto | null>(null);
  const [criteria, setCriteria] = useState<WritingStatsCriteriaDto | null>(null);
  const [letterTypes, setLetterTypes] = useState<WritingStatsLetterTypesDto | null>(null);
  const [canon, setCanon] = useState<WritingStatsCanonDto | null>(null);
  const [time, setTime] = useState<WritingStatsTimeDto | null>(null);
  const [skills, setSkills] = useState<WritingStatsSkillsDto | null>(null);
  const [readiness, setReadiness] = useState<WritingReadinessScoreDto | null>(null);
  const [calendar, setCalendar] = useState<WritingStatsCalendarDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);

  useEffect(() => {
    let cancelled = false;
    void Promise.all([
      getWritingStatsDashboard().catch(() => null),
      getWritingStatsBands().catch(() => null),
      getWritingStatsCriteria().catch(() => null),
      getWritingStatsLetterTypes().catch(() => null),
      getWritingStatsCanon().catch(() => null),
      getWritingStatsTime().catch(() => null),
      getWritingStatsSkills().catch(() => null),
      getWritingReadiness().catch(() => null),
      getWritingStatsCalendar().catch(() => null),
    ]).then(([d, b, c, lt, can, tm, sk, r, cal]) => {
      if (cancelled) return;
      setDashboard(d);
      setBands(b);
      setCriteria(c);
      setLetterTypes(lt);
      setCanon(can);
      setTime(tm);
      setSkills(sk);
      setReadiness(r);
      setCalendar(cal);
    }).catch((err) => {
      if (cancelled) return;
      setError(err instanceof Error ? err.message : t('writing.stats.error.load'));
    });
    return () => {
      cancelled = true;
    };
  }, [t]);

  const onExport = async () => {
    setExporting(true);
    try {
      const r = await exportWritingStats();
      if (r.url && typeof window !== 'undefined') window.open(r.url, '_blank', 'noopener,noreferrer');
    } catch (err) {
      setError(err instanceof Error ? err.message : t('writing.stats.error.export'));
    } finally {
      setExporting(false);
    }
  };

  return (
    <LearnerDashboardShell pageTitle={t('writing.stats.pageTitle')}>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow={t('writing.stats.eyebrow')}
          icon={BarChart3}
          accent="amber"
          title={t('writing.stats.title')}
          description={t('writing.stats.description')}
          highlights={[
            { icon: Flame, label: t('writing.stats.highlights.streak'), value: dashboard ? t('writing.stats.highlights.streakDays', { days: dashboard.streakDays }) : '—' },
            { icon: Target, label: t('writing.stats.highlights.latestBand'), value: dashboard?.latestBand ?? '—' },
            { icon: Calendar, label: t('writing.stats.highlights.daysToExam'), value: dashboard?.daysToExam === null ? '—' : `${dashboard?.daysToExam ?? '—'}` },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <div className="flex justify-end">
          <Button onClick={() => void onExport()} loading={exporting} variant="outline" size="sm">{t('writing.stats.export')}</Button>
        </div>

        {readiness ? (
          <ReadinessWidget
            score={readiness.score}
            subScores={readiness.subScores}
            deltaVsLastWeek={readiness.deltaVsLastWeek}
            predictedBand={readiness.predictedBand}
          />
        ) : null}

        <LearnerSurfaceSectionHeader eyebrow={t('writing.stats.bands.eyebrow')} title={t('writing.stats.bands.title')} description={t('writing.stats.bands.description')} />
        <BandHistoryChart data={(bands?.history ?? []).map((p) => ({ date: p.date, rawTotal: p.rawTotal, estimatedBand: p.estimatedBand, letterType: p.letterType, isRevision: p.isRevision }))} targetBand={bands?.targetBand ?? undefined} />

        <div className="grid gap-4 lg:grid-cols-2">
          {criteria ? (
            <Card padding="md">
              <CardContent>
                <h2 className="text-base font-bold text-navy">{t('writing.stats.criteria.heading')}</h2>
                <CriteriaRadar scores={criteria.current} targetScores={criteria.target} />
              </CardContent>
            </Card>
          ) : null}
          {letterTypes ? (
            <Card padding="md">
              <CardContent>
                <h2 className="text-base font-bold text-navy">{t('writing.stats.letterTypes.heading')}</h2>
                <table className="mt-3 w-full text-sm">
                  <thead>
                    <tr className="border-b border-border text-xs uppercase tracking-wider text-muted">
                      <th className="py-2 text-left font-bold">{t('writing.stats.letterTypes.colType')}</th>
                      <th className="text-right font-bold">{t('writing.stats.letterTypes.colAttempts')}</th>
                      <th className="text-right font-bold">{t('writing.stats.letterTypes.colAverageBand')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {letterTypes.rows.length === 0 ? (
                      <tr><td colSpan={3} className="py-3 text-center text-xs text-muted">{t('writing.stats.letterTypes.empty')}</td></tr>
                    ) : null}
                    {letterTypes.rows.map((row) => (
                      <tr key={row.letterType} className="border-b border-border/60">
                        <td className="py-2 font-bold">{row.letterType}</td>
                        <td className="text-right">{row.attempts}</td>
                        <td className="text-right font-bold">{row.averageBand.toFixed(1)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </CardContent>
            </Card>
          ) : null}
        </div>

        {canon ? (
          <Card padding="md">
            <CardContent>
              <h2 className="text-base font-bold text-navy">{t('writing.stats.canon.heading')}</h2>
              <ul className="mt-3 space-y-2">
                {canon.topViolations.length === 0 ? <li className="text-sm text-muted">{t('writing.stats.canon.empty')}</li> : null}
                {canon.topViolations.slice(0, 8).map((row) => (
                  <li key={row.ruleId} className="flex items-center justify-between gap-3 rounded-lg border border-border bg-background p-2 text-sm">
                    <span className="flex flex-wrap items-center gap-2">
                      <Badge variant="muted" size="sm">{row.ruleId}</Badge>
                      {/* Rule text is OET-authored English content. */}
                      <span className="text-navy" dir="ltr">{row.ruleText}</span>
                    </span>
                    <span className="font-bold text-navy">{row.count}×</span>
                  </li>
                ))}
              </ul>
            </CardContent>
          </Card>
        ) : null}

        {time ? (
          <Card padding="md">
            <CardContent>
              <h2 className="flex items-center gap-2 text-base font-bold text-navy">
                <Clock className="h-4 w-4" aria-hidden="true" /> {t('writing.stats.time.heading')}
              </h2>
              <p className="mt-2 text-sm text-muted">
                {t('writing.stats.time.avgCompletion')} <span className="font-bold text-navy">{t('writing.stats.time.avgCompletionValue', { minutes: Math.round(time.averageCompletionSeconds / 60) })}</span>
                {' · '}
                {t('writing.stats.time.within40')} <span className="font-bold text-navy">{t('writing.stats.time.within40Value', { percent: Math.round(time.percentCompletedWithin40Min) })}</span>
              </p>
            </CardContent>
          </Card>
        ) : null}

        {skills ? (
          <Card padding="md">
            <CardContent>
              <h2 className="text-base font-bold text-navy">{t('writing.stats.skills.heading')}</h2>
              <ul className="mt-3 grid gap-1 sm:grid-cols-2">
                {(Object.entries(skills.mastery) as Array<[WritingSubSkill, number]>).map(([skill, value]) => {
                  const v = Math.max(0, Math.min(100, Math.round(value)));
                  return (
                    <li key={skill} className="flex items-center gap-2 text-xs">
                      <span className="w-12 shrink-0 font-bold text-muted">{SKILL_LABELS[skill]}</span>
                      <div className="flex-1 h-1.5 rounded-full bg-slate-200" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={v} aria-label={t('writing.stats.skills.masteryAria', { skill: SKILL_LABELS[skill] })}>
                        <div className="h-full bg-primary" style={{ width: `${v}%` }} />
                      </div>
                      <span className="w-10 text-right font-bold tabular-nums">{v}%</span>
                    </li>
                  );
                })}
              </ul>
            </CardContent>
          </Card>
        ) : null}

        {calendar ? (
          <Card padding="md">
            <CardContent>
              <h2 className="text-base font-bold text-navy">{t('writing.stats.calendar.heading')}</h2>
              <p className="mt-1 text-xs text-muted">{t('writing.stats.calendar.subtitle')}</p>
              <div className="mt-3 grid grid-cols-7 gap-1" aria-label={t('writing.stats.calendar.label')} role="img">
                {calendar.days.slice(-7 * 8).map((d) => {
                  const intensity = Math.min(4, d.count);
                  const tone = intensity === 0
                    ? 'bg-slate-100'
                    : intensity === 1
                      ? 'bg-emerald-100'
                      : intensity === 2
                        ? 'bg-emerald-300'
                        : intensity === 3
                          ? 'bg-emerald-500'
                          : 'bg-emerald-700';
                  return (
                    <div
                      key={d.date}
                      className={`h-4 w-4 rounded-sm ${tone}`}
                      title={t('writing.stats.calendar.cellAria', { date: d.date, count: d.count })}
                      aria-label={t('writing.stats.calendar.cellAria', { date: d.date, count: d.count })}
                    />
                  );
                })}
              </div>
            </CardContent>
          </Card>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
