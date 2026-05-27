'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ClipboardCheck, UserRoundCheck } from 'lucide-react';
import { TutorRouteHero, TutorRouteWorkspace } from '@/components/domain/tutor-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { CriteriaRadar } from '@/components/domain/writing/CriteriaRadar';
import { CanonViolationCard } from '@/components/domain/writing/CanonViolationCard';
import { apiClient } from '@/lib/api';
import { getWritingSubmission, getWritingSubmissionGrade } from '@/lib/writing/api';
import type {
  WritingCriteriaScoresDto,
  WritingCriterionCode,
  WritingGradeDto,
  WritingSubmissionDto,
} from '@/lib/writing/types';

const CRITERIONS: WritingCriterionCode[] = ['c1', 'c2', 'c3', 'c4', 'c5', 'c6'];
const CRITERION_LABEL: Record<WritingCriterionCode, string> = {
  c1: 'C1 Purpose',
  c2: 'C2 Content',
  c3: 'C3 Conciseness',
  c4: 'C4 Genre',
  c5: 'C5 Organisation',
  c6: 'C6 Language',
};
const MAX_SCORE: Record<WritingCriterionCode, number> = {
  c1: 3, c2: 7, c3: 7, c4: 7, c5: 7, c6: 7,
};

function gradeToScores(g: WritingGradeDto): WritingCriteriaScoresDto {
  return {
    c1: g.c1Purpose,
    c2: g.c2Content,
    c3: g.c3Conciseness,
    c4: g.c4Genre,
    c5: g.c5Organisation,
    c6: g.c6Language,
  };
}

export default function TutorWritingReviewPage() {
  const params = useParams<{ submissionId: string }>();
  const router = useRouter();
  const submissionId = String(params?.submissionId ?? '');

  const [submission, setSubmission] = useState<WritingSubmissionDto | null>(null);
  const [grade, setGrade] = useState<WritingGradeDto | null>(null);
  const [freeText, setFreeText] = useState('');
  const [perCriterion, setPerCriterion] = useState<Record<WritingCriterionCode, string>>({ c1: '', c2: '', c3: '', c4: '', c5: '', c6: '' });
  const [scoreOverride, setScoreOverride] = useState<Record<WritingCriterionCode, string>>({ c1: '', c2: '', c3: '', c4: '', c5: '', c6: '' });
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!submissionId) return;
    void Promise.all([getWritingSubmission(submissionId), getWritingSubmissionGrade(submissionId)])
      .then(([s, g]) => {
        setSubmission(s);
        setGrade(g);
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load submission.'));
  }, [submissionId]);

  const submit = useCallback(async () => {
    if (!submissionId) return;
    setBusy(true);
    setError(null);
    try {
      const cleanedComments: Record<string, string> = {};
      for (const c of CRITERIONS) {
        if (perCriterion[c].trim()) cleanedComments[c] = perCriterion[c].trim();
      }
      const cleanedOverride: Record<string, number> = {};
      for (const c of CRITERIONS) {
        const v = scoreOverride[c].trim();
        if (v !== '') {
          const num = Number(v);
          if (Number.isFinite(num)) cleanedOverride[c] = Math.max(0, Math.min(MAX_SCORE[c], num));
        }
      }
      await apiClient.post('/v1/tutors/writing/reviews', {
        submissionId,
        freeTextFeedback: freeText.trim() || null,
        perCriterionComments: Object.keys(cleanedComments).length > 0 ? cleanedComments : null,
        scoreOverride: Object.keys(cleanedOverride).length > 0 ? cleanedOverride : null,
      });
      router.push('/tutor/writing/queue');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Submission failed.');
    } finally {
      setBusy(false);
    }
  }, [submissionId, freeText, perCriterion, scoreOverride, router]);

  const scores = grade ? gradeToScores(grade) : null;

  return (
    <TutorRouteWorkspace>
      <TutorRouteHero
        eyebrow="Tutor review"
        icon={UserRoundCheck}
        title="Review a learner letter"
        description="Read the AI grade and canon violations on the left; add per-criterion comments and an optional score override on the right."
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <div className="mt-4 grid gap-4 lg:grid-cols-2">
        <section aria-labelledby="learner-side" className="space-y-3">
          <h2 id="learner-side" className="text-base font-bold text-navy">AI grade reference</h2>
          {scores ? (
            <Card padding="md">
              <CardContent>
                <p className="text-sm text-navy">Estimated band: <span className="font-bold">{grade?.bandLabel}</span> · raw {grade?.rawTotal}/38</p>
                <CriteriaRadar scores={scores} className="mt-2" />
              </CardContent>
            </Card>
          ) : null}
          {grade?.canonViolations?.length ? (
            <Card padding="md">
              <CardContent>
                <h3 className="text-sm font-bold text-navy">Canon violations</h3>
                <div className="mt-2 grid gap-2">
                  {grade.canonViolations.map((v) => <CanonViolationCard key={v.id} violation={v} />)}
                </div>
              </CardContent>
            </Card>
          ) : null}
          {submission ? (
            <Card padding="md">
              <CardContent>
                <h3 className="text-sm font-bold text-navy">Learner letter</h3>
                <pre className="mt-2 whitespace-pre-wrap rounded border border-border bg-background p-3 text-xs leading-relaxed font-sans">
                  {submission.letterContent}
                </pre>
                <p className="mt-2 text-xs text-muted">Word count: {submission.wordCount} · Mode: <Badge variant="muted" size="sm">{submission.mode}</Badge></p>
              </CardContent>
            </Card>
          ) : null}
        </section>

        <section aria-labelledby="review-form" className="space-y-3">
          <h2 id="review-form" className="text-base font-bold text-navy">Your review</h2>
          <Card padding="md">
            <CardContent>
              <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Free-text feedback
                <textarea
                  rows={6}
                  value={freeText}
                  onChange={(e) => setFreeText(e.target.value)}
                  className="rounded border border-border bg-background p-2 text-sm"
                  placeholder="Overall feedback for the learner — what to focus on next."
                  aria-describedby="free-helper"
                />
              </label>
              <span id="free-helper" className="text-xs text-muted">Optional but recommended; ≤4,000 chars.</span>
            </CardContent>
          </Card>

          <Card padding="md">
            <CardContent>
              <h3 className="text-sm font-bold text-navy">Per-criterion comments and score overrides</h3>
              <p className="mt-1 text-xs text-muted">Leave score blank to keep the AI value; out-of-range values are clamped.</p>
              <ul className="mt-3 space-y-3">
                {CRITERIONS.map((c) => (
                  <li key={c} className="rounded-lg border border-border bg-background p-3">
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-sm font-bold text-navy">{CRITERION_LABEL[c]}</span>
                      <label className="flex items-center gap-2 text-xs">
                        Override score (0-{MAX_SCORE[c]}):
                        <input
                          type="number"
                          min={0}
                          max={MAX_SCORE[c]}
                          step={c === 'c1' ? 1 : 0.5}
                          value={scoreOverride[c]}
                          onChange={(e) => setScoreOverride((prev) => ({ ...prev, [c]: e.target.value }))}
                          className="w-20 rounded border border-border bg-surface px-2 py-1 text-xs"
                          aria-label={`${CRITERION_LABEL[c]} override score`}
                        />
                      </label>
                    </div>
                    <textarea
                      rows={2}
                      value={perCriterion[c]}
                      onChange={(e) => setPerCriterion((prev) => ({ ...prev, [c]: e.target.value }))}
                      placeholder="Per-criterion note"
                      className="mt-2 w-full rounded border border-border bg-surface p-2 text-xs"
                      aria-label={`${CRITERION_LABEL[c]} comment`}
                    />
                  </li>
                ))}
              </ul>
            </CardContent>
          </Card>

          <div className="flex items-center justify-end gap-2">
            <Button variant="outline" onClick={() => router.push('/tutor/writing/queue')}>Cancel</Button>
            <Button onClick={() => void submit()} loading={busy}>
              <ClipboardCheck className="h-4 w-4" aria-hidden="true" />
              Submit review
            </Button>
          </div>
        </section>
      </div>
    </TutorRouteWorkspace>
  );
}
