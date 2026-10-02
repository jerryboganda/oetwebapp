'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowRight, BarChart3, BookOpen, Clock, ListChecks } from 'lucide-react';
import { ResultsScorePanel } from '@/components/domain/results/results-score-panel';
import { ScoreBandGraph } from '@/components/domain/results/score-band-graph';
import { ScoreConversionEvidence } from '@/components/domain/results/score-conversion-evidence';
import { TimeUsedSummary } from '@/components/domain/results/time-used-summary';
import { AnswerComparisonCard } from '@/components/domain/results/answer-comparison-card';
import { LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, cardClassName } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { TabPanel, Tabs } from '@/components/ui/tabs';
import { getMockResults, type MockResultDto } from '@/lib/reading-pathway-api';

type Tab = 'score' | 'sections' | 'skills' | 'time' | 'next';

const TABS: { id: Tab; label: string }[] = [
  { id: 'score', label: 'Score' },
  { id: 'sections', label: 'Section Breakdown' },
  { id: 'skills', label: 'Sub-skills' },
  { id: 'time', label: 'Time Map' },
  { id: 'next', label: 'Next Steps' },
];

// Grade colours keep their meaning: A/B pass shades, C caution, below C a miss.
const GRADE_BADGE: Record<string, 'success' | 'info' | 'warning'> = { A: 'success', B: 'info', C: 'warning' };

function GradeBadge({ grade }: { grade: string }) {
  return (
    <Badge variant={GRADE_BADGE[grade] ?? 'danger'} size="md">
      Grade {grade}
    </Badge>
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

  if (loading) {
    return (
      <>
        {[...Array(4)].map((_, i) => (
          <Skeleton key={i} className="h-16 rounded-xl" />
        ))}
      </>
    );
  }

  if (error) return <ErrorState message={error} />;
  if (!result) {
    // Same wording as the Listening mock results page; the link is the one this page always offered.
    return (
      <EmptyState
        icon={<BookOpen className="h-8 w-8" aria-hidden />}
        title="Mock results not yet available"
        action={{ label: 'Back to Mocks', href: '/reading/mocks' }}
      />
    );
  }

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
      <MotionSection>
        <ResultsScorePanel
          eyebrow="Reading mock"
          icon={BookOpen}
          title="Mock result"
          subtitle={`Session ${sessionId}`}
          gaugeValue={hasConversion ? (scaledScore / 500) * 100 : 0}
          gaugeCenter={<span className="text-2xl font-black text-navy">{hasConversion ? grade : '—'}</span>}
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
      </MotionSection>

      {/* The page's two disclosures, as one callout under the result they qualify. */}
      <InlineAlert variant="warning" live="polite" data-testid="reading-mock-practice-score-disclosure" title="AI Practice Score — not an official OET result.">
        <span data-testid="reading-mock-marking-strictness-disclosure">
          This platform grades minor spelling variations strictly to build exam-safe habits — some real OET examiners may allow minor variants at their discretion.
        </span>
      </InlineAlert>

      <MotionSection>
        <ScoreConversionEvidence
          assessment="Reading"
          rawScore={result.rawScore}
          maxRawScore={maxRawScore}
          scaledScore={hasConversion ? scaledScore : null}
          passed={hasConversion ? result.scoreConversionPassed : null}
          grade={hasConversion ? grade : null}
          tableVersion={hasConversion ? result.scoreConversionTableVersionKey : null}
        />
      </MotionSection>
      <MotionSection>
        <TimeUsedSummary
          totalMilliseconds={result.timeUsed.totalMilliseconds}
          sections={result.timeUsed.sections.map((section) => ({
            label: `Part ${section.sectionCode}`,
            milliseconds: section.elapsedMilliseconds,
          }))}
          description="Time is reported from persisted mock telemetry. Missing section telemetry is shown as not recorded."
        />
      </MotionSection>

      <MotionSection>
        <Tabs tabs={TABS} activeTab={activeTab} onChange={(id) => setActiveTab(id as Tab)} />
        <Card padding="lg" className="mt-3">
          <TabPanel id="score" activeTab={activeTab}>
            <div className="flex flex-col items-center gap-4 py-4 text-center">
              <p className="text-6xl font-bold tabular-nums text-navy">
                {result.scaledScore === null ? '—' : <CountUp value={result.scaledScore} />}
              </p>
              <p className="text-sm text-muted">{result.scaledScore === null ? 'Owner-approved scaled conversion is unavailable.' : 'Scaled score (out of 500)'}</p>
              {result.grade ? <GradeBadge grade={result.grade} /> : null}
              <p className="text-sm tabular-nums text-muted">Raw score: {result.rawScore}</p>
            </div>
          </TabPanel>

          <TabPanel id="sections" activeTab={activeTab}>
            <h2 className="mb-3 text-sm font-semibold text-navy">Part Scores</h2>
            {result.partBreakdown.length > 0 ? (
              <ul className="divide-y divide-border">
                {result.partBreakdown.map((part) => (
                  <li key={part.partCode} className="py-3 first:pt-0 last:pb-0">
                    <div className="flex items-center justify-between gap-3">
                      <span className="text-sm font-medium text-navy">Part {part.partCode}</span>
                      <span className="text-sm font-bold tabular-nums text-navy">{part.rawScore}/{part.maxRawScore}</span>
                    </div>
                    <p className="mt-1 text-xs tabular-nums text-muted">
                      {part.correctCount} correct | {part.incorrectCount} wrong | {part.unansweredCount} unanswered
                    </p>
                    <p className="mt-1 text-xs font-semibold tabular-nums text-primary">Accuracy {part.accuracyPercentage.toFixed(1)}%</p>
                  </li>
                ))}
              </ul>
            ) : (
              <EmptyState className="py-6" icon={<BarChart3 className="h-7 w-7" aria-hidden />} title="No section data available." />
            )}
          </TabPanel>

          <TabPanel id="skills" activeTab={activeTab}>
            <h2 className="mb-3 text-sm font-semibold text-navy">Sub-skill Scores</h2>
            {Object.entries(result.skillBreakdown).length > 0 ? (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-border">
                      <th className="pb-2 text-start text-xs font-semibold text-muted">Skill</th>
                      <th className="pb-2 text-end text-xs font-semibold text-muted">Score</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border">
                    {Object.entries(result.skillBreakdown).map(([skill, score]) => (
                      <tr key={skill}>
                        <td className="py-2.5 text-navy">{skill}</td>
                        <td className="py-2.5 text-end font-bold tabular-nums text-navy">{score}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : (
              <EmptyState className="py-6" icon={<ListChecks className="h-7 w-7" aria-hidden />} title="No skill data available." />
            )}
          </TabPanel>

          <TabPanel id="time" activeTab={activeTab}>
            <h2 className="mb-3 text-sm font-semibold text-navy">Time per Section (seconds)</h2>
            {Object.entries(result.timeMap).length > 0 ? (
              <ul className="divide-y divide-border">
                {Object.entries(result.timeMap).map(([section, seconds]) => {
                  const minutes = Math.floor(seconds / 60);
                  const secs = seconds % 60;
                  return (
                    <li key={section} className="flex items-center justify-between gap-3 py-3 first:pt-0 last:pb-0">
                      <span className="text-sm font-medium text-navy">Part {section}</span>
                      <span className="text-sm tabular-nums text-muted">
                        {minutes}m {secs}s
                      </span>
                    </li>
                  );
                })}
              </ul>
            ) : (
              <EmptyState className="py-6" icon={<Clock className="h-7 w-7" aria-hidden />} title="No time data available." />
            )}
          </TabPanel>

          <TabPanel id="next" activeTab={activeTab}>
            <div className="space-y-4">
              <h2 className="text-sm font-semibold text-navy">Next Steps</h2>
              {result.nextStep ? (
                <>
                  <p className="text-sm text-muted">{result.nextStep.description}</p>
                  <div className="flex flex-wrap gap-3">
                    <Button asChild>
                      <Link href={result.nextStep.route}>
                        {result.nextStep.title}
                        <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden />
                      </Link>
                    </Button>
                    {result.studyPlanRoute ? (
                      <Button asChild variant="outline">
                        <Link href={result.studyPlanRoute}>
                          Edit this plan
                          <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden />
                        </Link>
                      </Button>
                    ) : null}
                  </div>
                </>
              ) : (
                <p className="text-sm text-muted">No missed items were recorded. Continue with your personalised study plan.</p>
              )}
            </div>
          </TabPanel>
        </Card>
      </MotionSection>

      <MotionSection>
        <section className={cardClassName({})} aria-labelledby="reading-mock-error-summary-title">
          <h2 id="reading-mock-error-summary-title" className="text-base font-bold text-navy">Error pattern summary</h2>
          {result.errorSummary.length > 0 ? (
            <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
              {result.errorSummary.map((summary) => (
                <div key={summary.errorCategory} className="rounded-xl border border-border bg-background-light px-4 py-3">
                  <p className="text-sm font-semibold capitalize text-navy">{summary.errorCategory.replace(/_/g, ' ')}</p>
                  <p className="mt-1 text-xs tabular-nums text-muted">{summary.count} item{summary.count === 1 ? '' : 's'} · Questions {summary.questionIds.map((id) => result.itemReview.find((item) => item.questionId === id)?.number ?? id).join(', ')}</p>
                </div>
              ))}
            </div>
          ) : (
            <EmptyState className="mt-3 py-6" icon={<ListChecks className="h-7 w-7" aria-hidden />} title="No error patterns were recorded." />
          )}
        </section>
      </MotionSection>

      <section className="space-y-4" aria-label="Question-by-question review">
        <LearnerSurfaceSectionHeader
          title="Question-by-question review"
          description="Post-submit answers, marks, explanations, and passage evidence from the persisted mock attempt."
        />
        {result.itemReview.length > 0 ? result.itemReview.map((item, index) => (
          <MotionItem key={item.questionId} delayIndex={Math.min(index, 5)}>
            <AnswerComparisonCard
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
          </MotionItem>
        )) : (
          <EmptyState icon={<ListChecks className="h-8 w-8" aria-hidden />} title="No item review data is recorded for this mock." />
        )}
      </section>
    </>
  );
}
