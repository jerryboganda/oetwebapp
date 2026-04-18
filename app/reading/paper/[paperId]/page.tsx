'use client';

import { use, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { AlertCircle, Clock, Loader2, Save } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Modal } from '@/components/ui/modal';
import {
  getReadingStructureLearner,
  saveReadingAnswer,
  startReadingAttempt,
  submitReadingAttempt,
  type ReadingAttemptStarted,
  type ReadingLearnerStructureDto,
  type ReadingPartCode,
  type ReadingQuestionLearnerDto,
} from '@/lib/reading-authoring-api';
import { sanitizeRichHtml } from '@/lib/sanitize-html';

export default function ReadingPaperPlayerPage({ params }: { params: Promise<{ paperId: string }> }) {
  const { paperId } = use(params);
  const router = useRouter();

  const [structure, setStructure] = useState<ReadingLearnerStructureDto | null>(null);
  const [attempt, setAttempt] = useState<ReadingAttemptStarted | null>(null);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(true);
  const [starting, setStarting] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [activePart, setActivePart] = useState<ReadingPartCode>('A');
  const [nowMs, setNowMs] = useState(() => Date.now());
  const [showConfirm, setShowConfirm] = useState(false);

  // Poll the clock every second for the countdown display.
  useEffect(() => {
    const h = setInterval(() => setNowMs(Date.now()), 1000);
    return () => clearInterval(h);
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    setAttempt(null);
    setAnswers({});
    setShowConfirm(false);
    setActivePart('A');
    try {
      const s = await getReadingStructureLearner(paperId);
      setStructure(s);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [paperId]);

  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  const start = async () => {
    setStarting(true);
    try {
      const a = await startReadingAttempt(paperId);
      setAttempt(a);
      setAnswers({});
    } catch (e) {
      const detail = (e as Error & { detail?: { error?: string } }).detail;
      setError(detail?.error ?? (e as Error).message);
    } finally { setStarting(false); }
  };

  // Debounced autosave on answer change.
  const saveTimers = useRef<Record<string, ReturnType<typeof setTimeout>>>({});
  const setAnswer = (questionId: string, value: unknown) => {
    const json = JSON.stringify(value);
    setAnswers((prev) => ({ ...prev, [questionId]: json }));
    if (!attempt) return;
    if (saveTimers.current[questionId]) clearTimeout(saveTimers.current[questionId]);
    saveTimers.current[questionId] = setTimeout(() => {
      void saveReadingAnswer(attempt.attemptId, questionId, json).catch(() => { /* best-effort */ });
    }, 400);
  };

  const submit = async () => {
    if (!attempt) return;
    setSubmitting(true);
    try {
      // Flush any pending autosaves
      Object.values(saveTimers.current).forEach(clearTimeout);
      await Promise.all(Object.entries(answers).map(([qId, json]) =>
        saveReadingAnswer(attempt.attemptId, qId, json).catch(() => null)));
      const result = await submitReadingAttempt(attempt.attemptId);
      // Route to results
      router.push(`/reading/paper/${paperId}/results?attemptId=${attempt.attemptId}&scaled=${result.scaledScore}&raw=${result.rawScore}`);
    } catch (e) {
      setError((e as Error).message);
    } finally { setSubmitting(false); }
  };

  const partBucket = (code: ReadingPartCode) => structure?.parts.find((p) => p.partCode === code);

  const countdownSec = useMemo(() => {
    if (!attempt) return null;
    const deadline = new Date(attempt.deadlineAt).getTime();
    return Math.max(0, Math.floor((deadline - nowMs) / 1000));
  }, [attempt, nowMs]);

  const answeredCount = Object.keys(answers).filter(k => (answers[k] ?? '').length > 2).length;
  const totalQuestions = structure?.parts.reduce((sum, p) => sum + p.questions.length, 0) ?? 0;

  if (loading) {
    return <LearnerDashboardShell pageTitle="Reading"><Skeleton className="h-64" /></LearnerDashboardShell>;
  }

  if (!structure) {
    return <LearnerDashboardShell pageTitle="Reading">
      <InlineAlert variant="error">{error ?? 'Paper not found.'}</InlineAlert>
    </LearnerDashboardShell>;
  }

  return (
    <LearnerDashboardShell pageTitle={structure.paper.title} backHref="/reading">
      {error && <InlineAlert variant="error">{error}</InlineAlert>}

      {!attempt ? (
        <div className="bg-surface rounded-[24px] border border-gray-200 p-6 text-center space-y-4">
          <h2 className="text-2xl font-black text-navy">{structure.paper.title}</h2>
          <p className="text-muted">
            You will have a strict Part A timer followed by a shared Part B+C window.
            Autosave runs continuously. Submit any time before the deadline.
          </p>
          <Button variant="primary" onClick={() => void start()} loading={starting}>
            Start attempt
          </Button>
        </div>
      ) : (
        <div className="space-y-4">
          <div className="flex items-center justify-between gap-4 bg-surface rounded-[20px] border border-gray-200 p-4">
            <div className="flex items-center gap-2">
              <Clock className="w-5 h-5 text-primary" />
              <span className="font-mono text-lg font-bold">
                {formatCountdown(countdownSec ?? 0)}
              </span>
              {countdownSec !== null && countdownSec <= 60 && countdownSec > 0 && (
                <Badge variant="warning">Final minute</Badge>
              )}
              {countdownSec === 0 && <Badge variant="danger">Time expired</Badge>}
            </div>
            <div className="text-sm text-muted">
              {answeredCount}/{totalQuestions} answered
            </div>
            <Button variant="primary" onClick={() => setShowConfirm(true)} disabled={submitting}>
              Submit
            </Button>
          </div>

          {/* Part tabs */}
          <div className="flex gap-2 border-b border-gray-200">
            {(['A', 'B', 'C'] as const).map((code) => {
              const p = partBucket(code);
              if (!p) return null;
              const partAnswered = p.questions.filter(q => (answers[q.id] ?? '').length > 2).length;
              return (
                <button
                  key={code}
                  type="button"
                  onClick={() => setActivePart(code)}
                  className={`px-4 py-2 border-b-2 -mb-px font-bold text-sm transition
                    ${activePart === code ? 'border-primary text-primary' : 'border-transparent text-muted hover:text-navy'}`}
                >
                  Part {code}
                  <span className="ml-2 text-xs text-muted">{partAnswered}/{p.questions.length}</span>
                </button>
              );
            })}
          </div>

          {/* Active part body */}
          {partBucket(activePart) && (
            <PartBody
              part={partBucket(activePart)!}
              answers={answers}
              onAnswerChange={setAnswer}
            />
          )}
        </div>
      )}

      <Modal open={showConfirm} onClose={() => setShowConfirm(false)} title="Submit attempt?">
        <p className="text-sm mb-4">
          You&apos;ve answered <strong>{answeredCount}</strong> of <strong>{totalQuestions}</strong> questions.
          Unanswered questions count as zero. This action is final.
        </p>
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={() => setShowConfirm(false)}>Keep working</Button>
          <Button variant="primary" onClick={() => { setShowConfirm(false); void submit(); }} loading={submitting}>
            Submit now
          </Button>
        </div>
      </Modal>
    </LearnerDashboardShell>
  );
}

// ── Part body ────────────────────────────────────────────────────────────

function PartBody({
  part, answers, onAnswerChange,
}: {
  part: ReadingLearnerStructureDto['parts'][number];
  answers: Record<string, string>;
  onAnswerChange: (qId: string, value: unknown) => void;
}) {
  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
      {/* Passages */}
      <section className="bg-surface rounded-[20px] border border-gray-200 p-5 space-y-4 max-h-[70vh] overflow-y-auto">
        <h3 className="font-black text-sm uppercase text-muted tracking-widest">Passages</h3>
        {part.texts.map((t) => (
          <article key={t.id}>
            <h4 className="font-bold text-navy">{t.title}</h4>
            {t.source && <p className="text-xs text-muted mb-2">Source: {t.source}</p>}
            <div
              className="prose prose-sm max-w-none"
              dangerouslySetInnerHTML={{ __html: sanitizeRichHtml(t.bodyHtml) }}
            />
          </article>
        ))}
      </section>

      {/* Questions */}
      <section className="bg-surface rounded-[20px] border border-gray-200 p-5 space-y-4 max-h-[70vh] overflow-y-auto">
        <h3 className="font-black text-sm uppercase text-muted tracking-widest">Questions</h3>
        {part.questions.map((q) => (
          <QuestionInput key={q.id} q={q} valueJson={answers[q.id] ?? ''} onChange={(v) => onAnswerChange(q.id, v)} />
        ))}
      </section>
    </div>
  );
}

// ── One question input, dispatched by type ───────────────────────────────

function QuestionInput({ q, valueJson, onChange }: {
  q: ReadingQuestionLearnerDto;
  valueJson: string;
  onChange: (value: unknown) => void;
}) {
  const options = Array.isArray(q.options) ? (q.options as string[]) : [];
  const current = useMemo(() => {
    if (!valueJson) return null;
    try { return JSON.parse(valueJson); } catch { return null; }
  }, [valueJson]);

  return (
    <div className="border-b border-gray-100 pb-3 last:border-0">
      <p className="font-medium text-sm mb-2">
        <span className="mr-2 text-muted">{q.displayOrder}.</span>
        {q.stem}
      </p>

      {q.questionType === 'MultipleChoice3' || q.questionType === 'MultipleChoice4' ? (
        <div className="space-y-1">
          {options.map((opt, i) => {
            const letter = String.fromCharCode(65 + i);
            return (
              <label key={letter} className="flex items-start gap-2 text-sm cursor-pointer">
                <input
                  type="radio"
                  name={q.id}
                  checked={current === letter}
                  onChange={() => onChange(letter)}
                />
                <span className="font-mono">{letter}.</span>
                <span>{opt}</span>
              </label>
            );
          })}
        </div>
      ) : q.questionType === 'MatchingTextReference' ? (
        <input
          className="w-full border rounded-lg p-2 text-sm font-mono"
          placeholder='["1","3"]'
          value={typeof current === 'string' ? current : Array.isArray(current) ? JSON.stringify(current) : ''}
          onChange={(e) => {
            try { onChange(JSON.parse(e.target.value)); }
            catch { onChange(e.target.value); }
          }}
        />
      ) : (
        <input
          className="w-full border rounded-lg p-2 text-sm"
          placeholder="Your answer…"
          value={typeof current === 'string' ? current : ''}
          onChange={(e) => onChange(e.target.value)}
        />
      )}
    </div>
  );
}

function formatCountdown(totalSec: number): string {
  const m = Math.floor(totalSec / 60);
  const s = totalSec % 60;
  return `${m}:${String(s).padStart(2, '0')}`;
}
