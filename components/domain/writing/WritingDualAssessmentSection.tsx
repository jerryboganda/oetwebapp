'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { Sparkles, UserCheck, Info } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import {
  fetchWritingDualAssessment,
  type WritingDualAssessment,
  type WritingDualCriterionCode,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';

interface Props {
  evaluationId: string;
  /**
   * Polling cadence in ms while the tutor track is still null. Defaults to
   * 30 seconds — fast enough to feel responsive when an expert submits,
   * slow enough to avoid hammering the API for the common case of no review.
   */
  tutorPollMs?: number;
}

const CRITERIA: { code: WritingDualCriterionCode; label: string }[] = [
  { code: 'purpose', label: 'Purpose' },
  { code: 'content', label: 'Content' },
  { code: 'conciseness', label: 'Conciseness & Clarity' },
  { code: 'genre', label: 'Genre & Style' },
  { code: 'organization', label: 'Organisation & Layout' },
  { code: 'language', label: 'Language' },
];

/**
 * Spec §12.E: show the AI estimate and the tutor (expert) score as separate,
 * clearly labelled columns so the learner always knows which number came
 * from which source. Both tracks are advisory; only an OET centre can issue
 * an official score.
 */
export function WritingDualAssessmentSection({ evaluationId, tutorPollMs = 30000 }: Props) {
  const [data, setData] = useState<WritingDualAssessment | null>(null);
  const [error, setError] = useState<string | null>(null);
  const trackedTutorArrival = useRef(false);

  useEffect(() => {
    if (!evaluationId) return;
    let cancelled = false;
    let timeout: ReturnType<typeof setTimeout> | null = null;

    const load = async () => {
      try {
        const response = await fetchWritingDualAssessment(evaluationId);
        if (cancelled) return;
        setData(response);
        // Keep polling until either the tutor track arrives or 10 minutes pass.
        if (response && response.tutor === null) {
          timeout = setTimeout(load, tutorPollMs);
        }
      } catch (e) {
        if (cancelled) return;
        setError((e as Error).message || 'Failed to load dual assessment.');
      }
    };
    void load();
    return () => {
      cancelled = true;
      if (timeout !== null) clearTimeout(timeout);
    };
  }, [evaluationId, tutorPollMs]);

  // Fire `writing_tutor_score_received` exactly once when the tutor track flips on.
  useEffect(() => {
    if (!data || trackedTutorArrival.current) return;
    if (data.tutor) {
      analytics.track('writing_tutor_score_received', {
        evaluationId: data.evaluationId,
        tutorId: data.tutor.tutorId,
        agreementBand: data.divergence?.agreementBand ?? null,
      });
      trackedTutorArrival.current = true;
      if (data.divergence?.agreementBand === 'wide') {
        analytics.track('writing_dual_divergence_wide_observed', {
          evaluationId: data.evaluationId,
          scaledDelta: data.divergence.scaledDelta,
        });
      }
    }
  }, [data]);

  useEffect(() => {
    if (!data) return;
    analytics.track('writing_dual_assessment_viewed', {
      evaluationId: data.evaluationId,
      tutorPresent: data.tutor !== null,
    });
  }, [data]);

  const rows = useMemo(() => {
    if (!data) return [];
    return CRITERIA.map(({ code, label }) => {
      const ai = data.ai.criterionScores[code];
      const tutor = data.tutor?.criterionScores[code] ?? null;
      const delta = data.divergence?.perCriterion[code] ?? null;
      return { code, label, ai, tutor, delta };
    });
  }, [data]);

  if (error) {
    return (
      <Card className="border-danger/40 bg-danger/5 p-6 text-sm text-danger">
        <p className="font-semibold">Failed to load dual assessment.</p>
        <p className="mt-1 text-xs">{error}</p>
      </Card>
    );
  }

  if (!data) {
    return (
      <Card className="border-border bg-surface p-6">
        <p className="text-sm text-muted">Loading AI and tutor assessment…</p>
      </Card>
    );
  }

  const divergenceTone =
    data.divergence?.agreementBand === 'close' ? 'success'
    : data.divergence?.agreementBand === 'moderate' ? 'warning'
    : data.divergence?.agreementBand === 'wide' ? 'danger'
    : 'default';

  return (
    <Card className="border-border bg-surface p-6">
      <div className="mb-5 flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="text-xs font-bold uppercase tracking-wider text-muted">Dual assessment</p>
          <h2 className="mt-1 text-lg font-bold text-navy">AI estimate vs. tutor (expert) score</h2>
          <p className="mt-1 text-xs text-muted">
            Both are advisory. Only an OET test centre can issue an official score.
          </p>
        </div>
        {data.divergence && (
          <Badge variant={divergenceTone} size="sm">
            Agreement: {data.divergence.agreementBand}
            {data.divergence.scaledDelta !== 0
              ? ` (tutor ${data.divergence.scaledDelta > 0 ? '+' : ''}${data.divergence.scaledDelta})`
              : ''}
          </Badge>
        )}
      </div>

      <div className="overflow-hidden rounded-2xl border border-border">
        <table className="w-full text-sm">
          <thead>
            <tr className="bg-background-light text-xs uppercase tracking-wider text-muted">
              <th className="px-4 py-2 text-left font-semibold">Criterion</th>
              <th className="px-4 py-2 text-center font-semibold">
                <span className="inline-flex items-center gap-1.5">
                  <Sparkles className="h-3.5 w-3.5" /> AI estimate
                </span>
              </th>
              <th className="px-4 py-2 text-center font-semibold">
                <span className="inline-flex items-center gap-1.5">
                  <UserCheck className="h-3.5 w-3.5" /> Tutor score
                </span>
              </th>
              <th className="px-4 py-2 text-center font-semibold">Δ</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.code} className="border-t border-border">
                <td className="px-4 py-2 text-navy">{row.label}</td>
                <td className="px-4 py-2 text-center text-navy">
                  <span className="font-bold tabular-nums">{row.ai.score}</span>
                  <span className="text-muted">/{row.ai.maxScore}</span>
                </td>
                <td className="px-4 py-2 text-center">
                  {row.tutor ? (
                    <>
                      <span className="font-bold tabular-nums text-emerald-700">{row.tutor.score}</span>
                      <span className="text-muted">/{row.tutor.maxScore}</span>
                    </>
                  ) : (
                    <span className="text-xs text-muted">Pending</span>
                  )}
                </td>
                <td className="px-4 py-2 text-center text-xs">
                  {row.delta === null
                    ? <span className="text-muted">—</span>
                    : row.delta === 0
                      ? <span className="text-muted">0</span>
                      : <span className={row.delta > 0 ? 'text-emerald-700 font-semibold' : 'text-danger font-semibold'}>
                          {row.delta > 0 ? `+${row.delta}` : row.delta}
                        </span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {data.tutor && data.tutor.overallFeedback && (
        <div className="mt-4 rounded-xl border border-emerald-200 bg-emerald-50/40 p-4">
          <p className="text-xs font-bold uppercase tracking-wider text-emerald-800">
            Tutor feedback — {data.tutor.tutorName}
          </p>
          <p className="mt-2 whitespace-pre-line text-sm text-navy">{data.tutor.overallFeedback}</p>
        </div>
      )}

      {!data.tutor && (
        <div className="mt-4 flex items-start gap-2 rounded-xl border border-border bg-background-light p-4 text-sm text-muted">
          <Info className="mt-0.5 h-4 w-4 shrink-0" />
          <p>
            Tutor (expert) score is pending. Once an expert reviewer submits, their assessment will appear
            here alongside the AI estimate so you can compare both.
          </p>
        </div>
      )}
    </Card>
  );
}
