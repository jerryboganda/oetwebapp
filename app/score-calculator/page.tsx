'use client';

import { useEffect, useState } from 'react';
import { Calculator, Building2, Globe } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { getScoreEquivalencesData } from '@/lib/learner-data';
import { analytics } from '@/lib/analytics';
import type { ScoreEquivalencesData } from '@/lib/types/learner';

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
        subtitle="Compare your OET score to IELTS, PTE, and CEFR levels and check institution requirements."
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
              subtitle="See how OET grades map to other major English proficiency exams."
            />
            <div className="overflow-x-auto rounded-xl border border-gray-200 dark:border-gray-700">
              <table className="min-w-full divide-y divide-gray-200 dark:divide-gray-700">
                <thead className="bg-gray-50 dark:bg-gray-800/50">
                  <tr>
                    {['OET Grade', 'OET Score', 'IELTS', 'PTE Academic', 'CEFR'].map((h) => (
                      <th key={h} className="px-4 py-3 text-left text-xs font-semibold text-gray-600 dark:text-gray-300 uppercase tracking-wider">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-200 dark:divide-gray-700">
                  {data.equivalences.map((row) => (
                    <MotionItem
                      key={row.oetGrade}
                      as="tr"
                      className={`cursor-pointer transition-colors ${
                        highlight === row.oetGrade
                          ? 'bg-indigo-50 dark:bg-indigo-900/20'
                          : 'hover:bg-gray-50 dark:hover:bg-gray-800/30'
                      }`}
                      onClick={() => setHighlight(highlight === row.oetGrade ? null : row.oetGrade)}
                    >
                      <td className="px-4 py-3 text-sm font-semibold text-indigo-700 dark:text-indigo-300">{row.oetGrade}</td>
                      <td className="px-4 py-3 text-sm text-gray-900 dark:text-gray-100">{row.oetScore}</td>
                      <td className="px-4 py-3 text-sm text-gray-700 dark:text-gray-300">{row.ielts}</td>
                      <td className="px-4 py-3 text-sm text-gray-700 dark:text-gray-300">{row.pte}</td>
                      <td className="px-4 py-3 text-sm text-gray-700 dark:text-gray-300">{row.cefr}</td>
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
              subtitle="Minimum OET grades accepted by major healthcare regulators worldwide."
            />
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {data.institutions.map((inst) => (
                <MotionItem
                  key={`${inst.institution}-${inst.profession}`}
                  className="rounded-xl border border-gray-200 dark:border-gray-700 p-4 bg-white dark:bg-gray-900"
                >
                  <h3 className="font-semibold text-gray-900 dark:text-gray-100 text-sm">{inst.institution}</h3>
                  <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">{inst.country} &middot; {inst.profession}</p>
                  <div className="mt-3 inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-green-50 dark:bg-green-900/20 text-green-700 dark:text-green-300 text-xs font-medium">
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
