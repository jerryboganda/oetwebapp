'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, ArrowRight, BookOpen } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { ResultsScorePanel } from '@/components/domain/results/results-score-panel';
import { ScoreBandGraph } from '@/components/domain/results/score-band-graph';
import { ScoreConversionEvidence } from '@/components/domain/results/score-conversion-evidence';
import { TimeUsedSummary } from '@/components/domain/results/time-used-summary';
import { AnswerComparisonCard } from '@/components/domain/results/answer-comparison-card';
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
            <p
              data-testid="reading-mock-practice-score-disclosure"
              className="rounded-xl border border-warning/30 bg-warning/10 px-4 py-3 text-sm font-semibold text-warning"
            >
              AI Practice Score — not an official OET result.
            </p>
            <p
              data-testid="reading-mock-marking-strictness-disclosure"
              className="rounded-xl border border-border bg-surface px-4 py-3 text-sm leading-6 text-muted"
            >
              This platform grades minor spelling variations strictly to build exam-safe habits — some real OET examiners may allow minor variants at their discretion.
            </p>
            {(() => {
              const scaledScore = result.scaledScore;
              const maxRawScore = result.totalQuestions ?? 0;
              const hasConversion = maxRawScore === 42
                && scaledScore !== null
                && result.scoreConversionTableVersionKey != null
                && result.scoreConversionPassed != null;
              const grade = result.grade;
              const gradeTone: 'success' | 'warning' | 'danger' | 'info' = !hasConversion ? 'info' : grade === 'A' || grade === 'B' ? 'success' : grade === 'C' ? 'warning' : 'danger';
                return (
                  <>
            <ResultsScorePanel
              eyebrow="Reading mock"
              icon={BookOpen}
              title="Mock result"
              subtitle={`Session ${sessionId}`}
              gaugeValue={hasConversion ? (scaledScore / 500) * 100 : 0}
              gaugeCenter={<span className="text-2xl font-black text-navy dark:text-white">{hasConversion ? grade : '—'}</span>}
              gaugeLabel={hasConversion ? `${scaledScore}/500` : 'Owner table unavailable'}
              gaugeColor={
                !hasConversion ? 'var(--color-info)' : grade === 'A' || grade === 'B'
                  ? 'var(--color-success)'
                  : grade === 'C'
                    ? 'var(--color-warning)'
                    : 'var(--color-danger)'
              }
              grade={{
                label: hasConversion ? `Grade ${grade}` : 'Scaled score unavailable',
                tone: gradeTone,
              }}
              stats={[
                { label: 'Scaled', value: hasConversion ? `${scaledScore}/500` : 'Unavailable', tone: 'info' },
                { label: 'Raw score', value: `${result.rawScore}/${maxRawScore || 'unknown'}`, tone: 'default' },
                {
                  label: 'Grade',
                  value: hasConversion ? grade : '—',
                  tone: gradeTone,
                },
              ]}
              chartSlot={(
                <ScoreBandGraph
                  rawScore={result.rawScore}
                  maxRawScore={maxRawScore}
                  scaledScore={hasConversion ? scaledScore : null}
                  passed={hasConversion ? result.scoreConversionPassed : null}
                  grade={hasConversion ? grade : null}
                  tableVersion={hasConversion ? result.scoreConversionTableVersionKey : null}
                />
              )}
            />
            <ScoreConversionEvidence
              assessment="Reading"
              rawScore={result.rawScore}
              maxRawScore={maxRawScore}
              scaledScore={hasConversion ? scaledScore : null}
              passed={hasConversion ? result.scoreConversionPassed : null}
              grade={hasConversion ? grade : null}
              tableVersion={hasConversion ? result.scoreConversionTableVersionKey : null}
            />
            <TimeUsedSummary
              totalMilliseconds={result.timeUsed.totalMilliseconds}
              sections={result.timeUsed.sections.map((section) => ({
                label: `Part ${section.sectionCode}`,
                milliseconds: section.elapsedMilliseconds,
              }))}
              description="Time is reported from persisted mock telemetry. Missing section telemetry is shown as not recorded."
            />
                  </>
              );
            })()}

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
                    {result.scaledScore ?? '—'}
                  </p>
                  <p className="text-sm text-muted">{result.scaledScore === null ? 'Owner-approved scaled conversion is unavailable.' : 'Scaled score (out of 500)'}</p>
                  {result.grade ? <GradeBadge grade={result.grade} /> : null}
                  <p className="text-sm text-muted">Raw score: {result.rawScore}</p>
                </div>
              )}

              {activeTab === 'sections' && (
                <div className="space-y-3">
                  <h2 className="text-sm font-semibold text-navy mb-4">Part Scores</h2>
                  {result.partBreakdown.length > 0 ? (
                    result.partBreakdown.map((part) => (
                      <div key={part.partCode} className="rounded-lg border border-border px-4 py-3">
                        <div className="flex items-center justify-between">
                          <span className="text-sm font-medium text-navy">Part {part.partCode}</span>
                          <span className="text-sm font-bold tabular-nums text-navy">{part.rawScore}/{part.maxRawScore}</span>
                        </div>
                        <p className="mt-1 text-xs text-muted">
                          {part.correctCount} correct | {part.incorrectCount} wrong | {part.unansweredCount} unanswered
                        </p>
                        <p className="mt-1 text-xs font-semibold text-primary">Accuracy {part.accuracyPercentage.toFixed(1)}%</p>
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
                  {result.nextStep ? (
                    <>
                      <p className="text-sm text-muted">{result.nextStep.description}</p>
                      <Link
                        href={result.nextStep.route}
                        className="inline-flex items-center gap-2 rounded-lg bg-primary px-5 py-2.5 text-sm font-semibold text-white hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600 transition-[color,background-color,transform] duration-200"
                      >
                        {result.nextStep.title}
                        <ArrowRight className="h-4 w-4" aria-hidden />
                      </Link>
                      {result.studyPlanRoute ? (
                        <Link
                          href={result.studyPlanRoute}
                          className="ml-3 inline-flex items-center gap-2 rounded-lg border border-primary px-5 py-2.5 text-sm font-semibold text-primary hover:bg-primary/10"
                        >
                          Edit this plan
                          <ArrowRight className="h-4 w-4" aria-hidden />
                        </Link>
                      ) : null}
                    </>
                  ) : (
                    <p className="text-sm text-muted">No missed items were recorded. Continue with your personalised study plan.</p>
                  )}
                </div>
              )}
            </div>
            <section className="rounded-xl border border-border bg-surface p-5" aria-labelledby="reading-mock-error-summary-title">
              <h2 id="reading-mock-error-summary-title" className="text-base font-black text-navy">Error pattern summary</h2>
              {result.errorSummary.length > 0 ? (
                <div className="mt-3 grid gap-3 sm:grid-cols-2">
                  {result.errorSummary.map((summary) => (
                    <div key={summary.errorCategory} className="rounded-lg border border-border px-4 py-3">
                      <p className="text-sm font-semibold capitalize text-navy">{summary.errorCategory.replace(/_/g, ' ')}</p>
                      <p className="mt-1 text-xs text-muted">{summary.count} item{summary.count === 1 ? '' : 's'} · Questions {summary.questionIds.map((id) => result.itemReview.find((item) => item.questionId === id)?.number ?? id).join(', ')}</p>
                    </div>
                  ))}
                </div>
              ) : <p className="mt-2 text-sm text-muted">No error patterns were recorded.</p>}
            </section>
            <section className="space-y-4" aria-labelledby="reading-mock-item-review-title">
              <div>
                <h2 id="reading-mock-item-review-title" className="text-base font-black text-navy">Question-by-question review</h2>
                <p className="mt-1 text-sm text-muted">Post-submit answers, marks, explanations, and passage evidence from the persisted mock attempt.</p>
              </div>
              {result.itemReview.length > 0 ? result.itemReview.map((item) => (
                <AnswerComparisonCard
                  key={item.questionId}
                  testId={`reading-mock-review-item-${item.questionId}`}
                  label={`Part ${item.partCode} · Question ${item.number}`}
                  stem={item.stem}
                  isCorrect={item.isCorrect}
                  unanswered={item.isUnanswered}
                  yourAnswer={item.learnerAnswer ?? 'No answer recorded'}
                  correctAnswer={item.correctAnswer}
                  pointsEarned={item.pointsEarned}
                  maxPoints={item.maxPoints}
                  missReason={item.errorCategory ? { title: `Result: ${item.errorCategory.replace(/_/g, ' ')}` } : null}
                  explanation={item.explanation ? <p>{item.explanation}</p> : null}
                >
                  {item.evidence ? <div className="rounded-xl border border-info/30 bg-info/10 p-3 text-sm text-info">Evidence: {item.evidence}</div> : null}
                </AnswerComparisonCard>
              )) : <p className="rounded-xl border border-border bg-surface px-4 py-3 text-sm text-muted">No item review data is recorded for this mock.</p>}
            </section>
          </>
        ) : null}
      </main>
    </LearnerDashboardShell>
  );
}
