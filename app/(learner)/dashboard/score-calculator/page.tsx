'use client';

import { useCallback, useEffect, useState } from 'react';
import { Calculator, Globe, GraduationCap, ArrowLeftRight } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { ErrorState } from '@/components/ui/empty-error';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

/* ── types ─────────────────────────────────────── */
interface ScoreEquivalence {
  oetGrade: string; oetScoreMin: number; oetScoreMax: number;
  ielts: number; pte: number; cefr: string;
}

interface Requirement {
  country: string; body: string; oetMinGrade: string; oetMinScore: number; ieltsMin: number;
}

interface EquivalenceData {
  equivalences: ScoreEquivalence[];
  commonRequirements: Requirement[];
}

/* ── api helper ───────────────────────────────── */
const apiRequest = apiClient.request;

/* ── grade colour helpers ──────────────────────── */
const GRADE_VARIANTS: Record<string, BadgeProps['variant']> = {
  'A':  'success',
  'B':  'info',
  'C+': 'warning',
  'C':  'warning',
  'D':  'danger',
  'E':  'danger',
};

/* Choice chip: tinted when selected (never a solid primary fill), state-layer hover, 44px target. */
function filterChipClass(active: boolean) {
  return `pressable inline-flex min-h-11 items-center rounded-control border px-3 text-xs font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${active ? 'border-primary bg-primary/10 text-primary' : 'border-border bg-surface text-navy hover:border-border-hover hover-primary'}`;
}

export default function ScoreCalculatorPage() {
  const [data, setData] = useState<EquivalenceData | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const [oetScore, setOetScore] = useState<number>(350);
  const [countryFilter, setCountryFilter] = useState<string | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setLoadFailed(false);
    apiRequest<EquivalenceData>('/v1/reference/score-equivalences')
      .then(setData)
      .catch(() => setLoadFailed(true))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    analytics.track('score_calculator_viewed');
    load();
  }, [load]);

  /* find matching row for the score input */
  const matchedRow = [...(data?.equivalences ?? [])].sort((x, y) => y.oetScoreMin - x.oetScoreMin).find(e => oetScore >= e.oetScoreMin);

  /* filter requirements */
  const countries = data ? Array.from(new Set(data.commonRequirements.map(r => r.country))).sort() : [];
  const filteredReqs = data?.commonRequirements.filter(r => !countryFilter || r.country === countryFilter) || [];

  return (
    <>
      <LearnerPageHero
        icon={Calculator}
        title="Score Cross-Reference Calculator"
        description="Compare OET scores with IELTS, PTE, and CEFR equivalents · Check institution requirements"
      />

      {loading ? (
        <div className="space-y-4" aria-hidden="true">
          <Skeleton className="h-32 rounded-2xl" /><Skeleton className="h-64 rounded-2xl" />
        </div>
      ) : (
        <>
          {loadFailed ? (
            <ErrorState
              title="Score equivalences unavailable"
              message="The equivalence table could not be loaded. Retry to see IELTS, PTE and CEFR comparisons."
              onRetry={load}
            />
          ) : null}

          {/* ── Score Input ────────────────────── */}
          <MotionSection>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader icon={Calculator} title="Enter Your OET Score" className="mb-4" />
              <div className="space-y-3">
                <input
                  type="range"
                  min={0} max={500} step={10}
                  value={oetScore}
                  onChange={e => setOetScore(Number(e.target.value))}
                  aria-label="OET score"
                  className="h-2 w-full accent-primary"
                />
                <div className="flex items-center justify-between">
                  <span className="text-xs tabular-nums text-muted">0</span>
                  <span className="text-2xl font-bold tabular-nums text-primary">{oetScore}</span>
                  <span className="text-xs tabular-nums text-muted">500</span>
                </div>
              </div>

              {matchedRow && (
                <div className="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-4">
                  <div className="rounded-xl bg-background-light p-3 text-center">
                    <p className="tile-label mb-1 text-muted">OET Grade</p>
                    <Badge variant={GRADE_VARIANTS[matchedRow.oetGrade] ?? 'outline'} size="md">
                      {matchedRow.oetGrade}
                    </Badge>
                  </div>
                  <div className="rounded-xl bg-background-light p-3 text-center">
                    <p className="tile-label mb-1 text-muted">IELTS</p>
                    <p className="text-xl font-bold tabular-nums text-navy">{matchedRow.ielts}</p>
                  </div>
                  <div className="rounded-xl bg-background-light p-3 text-center">
                    <p className="tile-label mb-1 text-muted">PTE</p>
                    <p className="text-xl font-bold tabular-nums text-navy">{matchedRow.pte}</p>
                  </div>
                  <div className="rounded-xl bg-background-light p-3 text-center">
                    <p className="tile-label mb-1 text-muted">CEFR</p>
                    <p className="text-xl font-bold text-navy">{matchedRow.cefr}</p>
                  </div>
                </div>
              )}
            </Card>
          </MotionSection>

          {/* ── Equivalence Table ──────────────── */}
          <MotionSection>
            <Card padding="none" className="overflow-hidden">
              <div className="border-b border-border p-4">
                <LearnerSurfaceSectionHeader icon={ArrowLeftRight} title="Full Equivalence Table" />
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="bg-background-light text-start text-muted">
                      <th className="eyebrow px-4 py-2 text-start">OET Grade</th>
                      <th className="eyebrow px-4 py-2 text-start">OET Score</th>
                      <th className="eyebrow px-4 py-2 text-start">IELTS</th>
                      <th className="eyebrow px-4 py-2 text-start">PTE</th>
                      <th className="eyebrow px-4 py-2 text-start">CEFR</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data?.equivalences.map((row, i) => {
                      const isMatch = matchedRow === row;
                      return (
                        <tr key={i} className={isMatch ? 'bg-primary/5 font-medium' : 'hover:bg-background-light'}>
                          <td className="px-4 py-2.5">
                            <Badge variant={GRADE_VARIANTS[row.oetGrade] ?? 'outline'}>
                              {row.oetGrade}
                            </Badge>
                          </td>
                          <td className="px-4 py-2.5 tabular-nums">{row.oetScoreMin}–{row.oetScoreMax}</td>
                          <td className="px-4 py-2.5 tabular-nums">{row.ielts}</td>
                          <td className="px-4 py-2.5 tabular-nums">{row.pte}</td>
                          <td className="px-4 py-2.5">{row.cefr}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </Card>
          </MotionSection>

          {/* ── Institution Requirements ────────── */}
          <section>
            <LearnerSurfaceSectionHeader
              icon={Globe}
              title="Institution Requirements"
              action={(
                <div className="flex flex-wrap gap-2" role="group" aria-label="Filter by country">
                  <button
                    type="button"
                    aria-pressed={!countryFilter}
                    onClick={() => setCountryFilter(null)}
                    className={filterChipClass(!countryFilter)}
                  >
                    All
                  </button>
                  {countries.map(c => (
                    <button
                      key={c}
                      type="button"
                      aria-pressed={countryFilter === c}
                      onClick={() => setCountryFilter(countryFilter === c ? null : c)}
                      className={filterChipClass(countryFilter === c)}
                    >
                      {c}
                    </button>
                  ))}
                </div>
              )}
              className="mb-4"
            />

            <div className="space-y-2">
              {filteredReqs.map((req, i) => {
                const meetsReq = oetScore >= req.oetMinScore;
                return (
                  <MotionItem key={i} delayIndex={Math.min(i, 5)}>
                    <Card padding="sm" className={`transition-colors ${meetsReq ? 'border-success/30' : ''}`}>
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div className="min-w-0 flex-1">
                          <div className="mb-1 flex flex-wrap items-center gap-2">
                            <GraduationCap className="h-4 w-4 shrink-0 text-muted" aria-hidden="true" />
                            <span className="text-sm font-medium text-navy">{req.body}</span>
                            <Badge variant="outline">{req.country}</Badge>
                          </div>
                          <p className="ms-6 text-xs tabular-nums text-muted">
                            Min OET: <strong>{req.oetMinGrade}</strong> ({req.oetMinScore}) · Min IELTS: <strong>{req.ieltsMin}</strong>
                          </p>
                        </div>
                        <Badge variant={meetsReq ? 'success' : 'danger'} className="shrink-0">
                          {meetsReq ? 'Meets Requirement' : 'Below Minimum'}
                        </Badge>
                      </div>
                    </Card>
                  </MotionItem>
                );
              })}
            </div>
          </section>

          {/* disclaimer */}
          <InlineAlert variant="info">
            Score equivalences are approximate and based on publicly available official guidance. Always verify requirements directly with the accepting institution or regulatory body.
          </InlineAlert>
        </>
      )}
    </>
  );
}
