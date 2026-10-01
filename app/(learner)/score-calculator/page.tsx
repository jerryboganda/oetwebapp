'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { Calculator, Building2, Globe, Target } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { Card, cardClassName } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { ErrorState } from '@/components/ui/empty-error';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { getScoreEquivalencesData } from '@/lib/learner-data';
import { analytics } from '@/lib/analytics';
import type { ScoreEquivalencesData } from '@/lib/types/learner';
import { cn } from '@/lib/utils';

export default function ScoreCalculatorPage() {
  const [data, setData] = useState<ScoreEquivalencesData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [highlight, setHighlight] = useState<string | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    getScoreEquivalencesData()
      .then(setData)
      .catch(() => setError('Unable to load score equivalences.'))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    analytics.track('content_view', { page: 'score-calculator' });
    load();
  }, [load]);

  return (
    <>
      <LearnerPageHero
        title="Score Cross-Reference Calculator"
        description="Compare your OET score to IELTS, PTE, and CEFR levels and check institution requirements."
        icon={Calculator}
      />

      {loading && (
        <div className="space-y-4" aria-hidden="true">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-48 w-full" />
        </div>
      )}

      {error && <ErrorState title="Score equivalences unavailable" message={error} onRetry={load} />}

      {data && (
        <>
          {/* Equivalence Table */}
          <MotionSection>
            <LearnerSurfaceSectionHeader
              icon={Globe}
              title="Score Equivalence Table"
              description="See how OET grades map to other major English proficiency exams."
              className="mb-4"
            />
            <Card padding="none" className="overflow-x-auto">
              <table className="min-w-full divide-y divide-border">
                <thead className="bg-background-light">
                  <tr>
                    {['OET Grade', 'OET Score', 'IELTS', 'PTE Academic', 'CEFR'].map((h) => (
                      <th key={h} className="eyebrow px-4 py-3 text-start text-muted">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {data.equivalences.map((row) => {
                    const highlighted = highlight === row.oetGrade;
                    return (
                      <tr
                        key={row.oetGrade}
                        className={`cursor-pointer transition-colors ${highlighted ? 'bg-primary/10' : 'hover:bg-background-light'}`}
                        onClick={() => setHighlight(highlighted ? null : row.oetGrade)}
                      >
                        <td className="px-4 py-3 text-sm font-semibold text-primary">
                          {/* Keyboard path for the row highlight: Enter/Space click the button, which bubbles to the row. */}
                          <button
                            type="button"
                            aria-pressed={highlighted}
                            className="rounded-control focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                          >
                            {row.oetGrade}
                          </button>
                        </td>
                        <td className="px-4 py-3 text-sm tabular-nums text-navy">{row.oetScore}</td>
                        <td className="px-4 py-3 text-sm tabular-nums text-navy">{row.ielts}</td>
                        <td className="px-4 py-3 text-sm tabular-nums text-navy">{row.pte}</td>
                        <td className="px-4 py-3 text-sm text-navy">{row.cefr}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </Card>
          </MotionSection>

          {/* Institution Requirements */}
          <MotionSection>
            <LearnerSurfaceSectionHeader
              icon={Building2}
              title="Institution Requirements"
              description="Minimum OET grades accepted by major healthcare regulators worldwide."
              className="mb-4"
            />
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {data.institutions.map((inst, index) => (
                <MotionItem
                  key={`${inst.institution}-${inst.profession}`}
                  delayIndex={Math.min(index, 5)}
                  className={cn(cardClassName({ padding: 'md' }), 'flex h-full flex-col')}
                >
                  <h3 className="text-sm font-semibold text-navy">{inst.institution}</h3>
                  <p className="mt-1 text-xs text-muted">{inst.country} &middot; {inst.profession}</p>
                  <Badge variant="success" className="mt-3 self-start">
                    Min. Grade {inst.minimumOetGrade}
                  </Badge>
                  <div className="mt-auto pt-1">
                    <Link
                      href={`/goals?targetGrade=${encodeURIComponent(inst.minimumOetGrade)}`}
                      className="inline-flex min-h-11 items-center gap-1 rounded-control text-xs font-semibold text-primary transition-colors hover:text-primary-dark focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                      onClick={() => analytics.track('score_calculator_target_set', { institution: inst.institution, grade: inst.minimumOetGrade })}
                    >
                      <Target className="h-3 w-3" aria-hidden="true" /> Target this score
                    </Link>
                  </div>
                </MotionItem>
              ))}
            </div>
          </MotionSection>
        </>
      )}
    </>
  );
}
