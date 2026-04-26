'use client';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { getScoreEquivalencesData } from '@/lib/learner-data';
import type { ScoreEquivalencesData } from '@/lib/types/learner';
import { Building2, Calculator, Globe } from 'lucide-react';
import { useEffect, useState } from 'react';

export default function ScoreCalculatorPage() {
  const [data, setData] = useState<ScoreEquivalencesData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [highlight, setHighlight] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { page: 'score-calculator' });
    getScoreEquivalencesData()
      .then(setData)
      .catch(() => setError('Unable to load score equivalences.'))
      .finally(() => setLoading(false));
  }, []);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Score Cross-Reference Calculator"
        description="Compare your OET score to IELTS, PTE, and CEFR levels and check institution requirements."
        icon={<Calculator className="w-7 h-7" />}
      />

      {loading && (
        <div className="space-y-4 p-6">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-48 w-full" />
        </div>
      )}

      {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

      {data && (
        <>
          {/* Equivalence Table */}
          <MotionSection>
            <LearnerSurfaceSectionHeader
              icon={<Globe className="w-5 h-5" />}
              title="Score Equivalence Table"
              description="See how OET grades map to other major English proficiency exams."
            />
            <div className="overflow-x-auto rounded-xl border border-border">
              <table className="min-w-full divide-y divide-border">
                <thead className="bg-background-light">
                  <tr>
                    {['OET Grade', 'OET Score', 'IELTS', 'PTE Academic', 'CEFR'].map((h) => (
                      <th key={h} className="px-4 py-3 text-left text-xs font-semibold text-muted uppercase tracking-wider">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {data.equivalences.map((row) => (
                    <MotionItem
                      key={row.oetGrade}
                      className={`cursor-pointer transition-colors ${
                        highlight === row.oetGrade
                          ? 'bg-primary/10'
                          : 'hover:bg-background-light'
                      }`}
                      onClick={() => setHighlight(highlight === row.oetGrade ? null : row.oetGrade)}
                    >
                      <td className="px-4 py-3 text-sm font-semibold text-primary">{row.oetGrade}</td>
                      <td className="px-4 py-3 text-sm text-navy">{row.oetScore}</td>
                      <td className="px-4 py-3 text-sm text-navy">{row.ielts}</td>
                      <td className="px-4 py-3 text-sm text-navy">{row.pte}</td>
                      <td className="px-4 py-3 text-sm text-navy">{row.cefr}</td>
                    </MotionItem>
                  ))}
                </tbody>
              </table>
            </div>
          </MotionSection>

          {/* Institution Requirements */}
          <MotionSection className="mt-8">
            <LearnerSurfaceSectionHeader
              icon={<Building2 className="w-5 h-5" />}
              title="Institution Requirements"
              description="Minimum OET grades accepted by major healthcare regulators worldwide."
            />
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {data.institutions.map((inst) => (
                <MotionItem
                  key={`${inst.institution}-${inst.profession}`}
                  className="rounded-xl border border-border p-4 bg-surface"
                >
                  <h3 className="font-semibold text-navy text-sm">{inst.institution}</h3>
                  <p className="text-xs text-muted mt-1">{inst.country} &middot; {inst.profession}</p>
                  <div className="mt-3 inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-success/10 text-success text-xs font-medium">
                    Min. Grade {inst.minimumOetGrade}
                  </div>
                </MotionItem>
              ))}
            </div>
          </MotionSection>
        </>
      )}
    </LearnerDashboardShell>
  );
}
