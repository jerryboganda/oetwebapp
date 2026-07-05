'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, ArrowRight, BookOpen } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { ResultsScorePanel } from '@/components/domain/results/results-score-panel';
import { getMockResults, type MockResultDto } from '@/lib/reading-pathway-api';

type Tab = 'score' | 'sections' | 'skills' | 'time' | 'next';

const TABS: { id: Tab; label: string }[] = [
  { id: 'score', label: 'Score' },
  { id: 'sections', label: 'Section Breakdown' },
  { id: 'skills', label: 'Sub-skills' },
  { id: 'time', label: 'Time Map' },
  { id: 'next', label: 'Next Steps' },
];

function GradeBadge({ grade }: { grade: string }) {
  const color =
    grade === 'A' ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-300' :
    grade === 'B' ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300' :
    grade === 'C' ? 'bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300' :
    'bg-rose-100 text-rose-700 dark:bg-rose-900/30 dark:text-rose-300';
  return (
    <span className={`inline-block rounded-full px-3 py-1 text-sm font-bold ${color}`}>
      Grade {grade}
    </span>
  );
}

export default function MockResultsPage() {
  const params = useParams<{ sessionId: string }>();
  const sessionId = params?.sessionId ?? '';
  const [result, setResult] = useState<MockResultDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<Tab>('score');

  useEffect(() => {
    if (!sessionId) {
      setLoading(false);
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        const data = await getMockResults(sessionId);
        if (!cancelled) setResult(data);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Failed to load results.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [sessionId]);

  return (
    <LearnerDashboardShell pageTitle="Mock Results">
      <main className="space-y-5 sm:space-y-8">
        <Link
          href="/reading/mocks"
          className="inline-flex items-center gap-1.5 text-sm text-muted hover:text-navy"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden />
          Back to Mocks
        </Link>

        {loading ? (
          <div className="space-y-3">
            {[...Array(4)].map((_, i) => (
              <div key={i} className="h-16 animate-pulse rounded-xl bg-border/80 dark:bg-border/50" />
            ))}
          </div>
        ) : error ? (
          <div className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
            {error}
          </div>
        ) : result ? (
          <>
            <ResultsScorePanel
              eyebrow="Reading mock"
              icon={BookOpen}
              title="Mock result"
              subtitle={`Session ${sessionId}`}
              gaugeValue={(result.scaledScore / 500) * 100}
              gaugeCenter={<span className="text-2xl font-black text-navy dark:text-white">{result.grade}</span>}
              gaugeLabel={`${result.scaledScore}/500`}
              gaugeColor={
                result.grade === 'A' || result.grade === 'B'
                  ? 'var(--color-success)'
                  : result.grade === 'C'
                    ? 'var(--color-warning)'
                    : 'var(--color-danger)'
              }
              grade={{
                label: `Grade ${result.grade}`,
                tone: result.grade === 'A' || result.grade === 'B' ? 'success' : result.grade === 'C' ? 'warning' : 'danger',
              }}
              stats={[
                { label: 'Scaled', value: `${result.scaledScore}/500`, tone: 'info' },
                { label: 'Raw score', value: result.rawScore, tone: 'default' },
                {
                  label: 'Grade',
                  value: result.grade,
                  tone: result.grade === 'A' || result.grade === 'B' ? 'success' : result.grade === 'C' ? 'warning' : 'danger',
                },
              ]}
            />

            {/* Tab bar */}
            <div className="flex gap-1 overflow-x-auto rounded-xl border border-border bg-surface p-1">
              {TABS.map((tab) => (
                <button
                  key={tab.id}
                  type="button"
                  onClick={() => setActiveTab(tab.id)}
                  className={[
                    'flex-shrink-0 rounded-lg px-4 py-2 text-xs font-semibold transition-colors',
                    activeTab === tab.id
                      ? 'bg-primary text-white dark:bg-violet-700'
                      : 'text-muted hover:text-navy hover:bg-background-light dark:hover:bg-background-dark',
                  ].join(' ')}
                >
                  {tab.label}
                </button>
              ))}
            </div>

            {/* Tab content */}
            <div className="rounded-xl border border-border bg-surface p-6">
              {activeTab === 'score' && (
                <div className="flex flex-col items-center gap-4 py-4">
                  <p className="text-6xl font-bold tabular-nums text-navy">
                    {result.scaledScore}
                  </p>
                  <p className="text-sm text-muted">Scaled score (out of 500)</p>
                  <GradeBadge grade={result.grade} />
                  <p className="text-sm text-muted">Raw score: {result.rawScore}</p>
                </div>
              )}

              {activeTab === 'sections' && (
                <div className="space-y-3">
                  <h2 className="text-sm font-semibold text-navy mb-4">Part Scores</h2>
                  {Object.entries(result.sectionBreakdown).length > 0 ? (
                    Object.entries(result.sectionBreakdown).map(([section, score]) => (
                      <div key={section} className="flex items-center justify-between rounded-lg border border-border px-4 py-3">
                        <span className="text-sm font-medium text-navy">Part {section}</span>
                        <span className="text-sm font-bold tabular-nums text-navy">{score}</span>
                      </div>
                    ))
                  ) : (
                    <p className="text-sm text-muted">No section data available.</p>
                  )}
                </div>
              )}

              {activeTab === 'skills' && (
                <div className="space-y-3">
                  <h2 className="text-sm font-semibold text-navy mb-4">Sub-skill Scores</h2>
                  {Object.entries(result.skillBreakdown).length > 0 ? (
                    <div className="overflow-x-auto">
                      <table className="w-full text-sm">
                        <thead>
                          <tr className="border-b border-border">
                            <th className="pb-2 text-left text-xs font-semibold text-muted">Skill</th>
                            <th className="pb-2 text-right text-xs font-semibold text-muted">Score</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-border">
                          {Object.entries(result.skillBreakdown).map(([skill, score]) => (
                            <tr key={skill}>
                              <td className="py-2.5 text-navy">{skill}</td>
                              <td className="py-2.5 text-right font-bold tabular-nums text-navy">{score}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  ) : (
                    <p className="text-sm text-muted">No skill data available.</p>
                  )}
                </div>
              )}

              {activeTab === 'time' && (
                <div className="space-y-3">
                  <h2 className="text-sm font-semibold text-navy mb-4">Time per Section (seconds)</h2>
                  {Object.entries(result.timeMap).length > 0 ? (
                    Object.entries(result.timeMap).map(([section, seconds]) => {
                      const minutes = Math.floor(seconds / 60);
                      const secs = seconds % 60;
                      return (
                        <div key={section} className="flex items-center justify-between rounded-lg border border-border px-4 py-3">
                          <span className="text-sm font-medium text-navy">Part {section}</span>
                          <span className="text-sm tabular-nums text-muted">
                            {minutes}m {secs}s
                          </span>
                        </div>
                      );
                    })
                  ) : (
                    <p className="text-sm text-muted">No time data available.</p>
                  )}
                </div>
              )}

              {activeTab === 'next' && (
                <div className="space-y-4">
                  <h2 className="text-sm font-semibold text-navy">Next Steps</h2>
                  <p className="text-sm text-muted">
                    Review your weak areas and continue with your personalised study plan.
                  </p>
                  <Link
                    href="/reading"
                    className="inline-flex items-center gap-2 rounded-lg bg-primary px-5 py-2.5 text-sm font-semibold text-white hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600 transition-[color,background-color,transform] duration-200"
                  >
                    Continue your plan
                    <ArrowRight className="h-4 w-4" aria-hidden />
                  </Link>
                </div>
              )}
            </div>
          </>
        ) : null}
      </main>
    </LearnerDashboardShell>
  );
}
