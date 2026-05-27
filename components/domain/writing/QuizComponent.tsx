'use client';

import { useState } from 'react';
import { CheckCircle2, XCircle } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';

export interface QuizQuestion {
  id: string;
  question: string;
  options: string[];
  correctIndex: number;
  explanation: string;
}

export interface QuizComponentProps {
  questions: QuizQuestion[];
  /**
   * Required pass percent (0-100). Default 80 per spec §7.x — lesson
   * marked complete only when learner clears the threshold.
   */
  passPercent?: number;
  /**
   * Called after submit with the achieved score (0-100). Parent uses
   * this to call `completeWritingLesson()` if the threshold is met
   * and otherwise allow a retake.
   */
  onSubmit: (score: number, perQuestion: { questionId: string; selectedIndex: number | null; correct: boolean }[]) => void;
  className?: string;
}

/**
 * Mini 5-question MCQ. Each question shows 2-5 options as radio
 * buttons. Submit reveals correct answers + explanations and reports
 * a score back to the parent.
 *
 * Accessible: each question is a `fieldset` with `legend`; radio
 * group has explicit `name` so keyboard navigation works.
 */
export function QuizComponent({ questions, passPercent = 80, onSubmit, className }: QuizComponentProps) {
  const [answers, setAnswers] = useState<Record<string, number | null>>(() =>
    Object.fromEntries(questions.map((q) => [q.id, null])),
  );
  const [submitted, setSubmitted] = useState(false);

  const allAnswered = questions.every((q) => answers[q.id] !== null && answers[q.id] !== undefined);

  const computeScore = () => {
    let correct = 0;
    for (const q of questions) {
      if (answers[q.id] === q.correctIndex) correct++;
    }
    return questions.length > 0 ? Math.round((correct / questions.length) * 100) : 0;
  };

  const handleSubmit = () => {
    if (submitted) return;
    if (!allAnswered) return;
    const score = computeScore();
    const perQuestion = questions.map((q) => ({
      questionId: q.id,
      selectedIndex: answers[q.id] ?? null,
      correct: answers[q.id] === q.correctIndex,
    }));
    setSubmitted(true);
    onSubmit(score, perQuestion);
  };

  const score = submitted ? computeScore() : null;
  const passed = score !== null && score >= passPercent;

  return (
    <div className={cn('flex flex-col gap-3', className)}>
      {questions.map((q, idx) => {
        const selected = answers[q.id];
        const isCorrect = submitted && selected === q.correctIndex;
        return (
          <Card key={q.id} padding="md">
            <CardContent>
              <fieldset>
                <legend className="font-bold text-sm mb-3">
                  Q{idx + 1}. <span className="font-normal">{q.question}</span>
                </legend>
                <div className="space-y-2">
                  {q.options.map((opt, optIdx) => {
                    const isThisSelected = selected === optIdx;
                    const isThisCorrect = optIdx === q.correctIndex;
                    let optTone = 'border-border hover:border-primary/50';
                    if (submitted) {
                      if (isThisCorrect) {
                        optTone = 'border-emerald-400 bg-emerald-50 dark:bg-emerald-950/40';
                      } else if (isThisSelected && !isThisCorrect) {
                        optTone = 'border-red-400 bg-red-50 dark:bg-red-950/40';
                      } else {
                        optTone = 'border-border opacity-70';
                      }
                    } else if (isThisSelected) {
                      optTone = 'border-primary bg-primary/5';
                    }
                    return (
                      <label
                        key={optIdx}
                        className={cn(
                          'flex items-start gap-2 rounded-lg border p-2.5 cursor-pointer transition-colors',
                          optTone,
                          submitted && 'cursor-default',
                        )}
                      >
                        <input
                          type="radio"
                          name={`quiz-${q.id}`}
                          value={optIdx}
                          checked={isThisSelected}
                          disabled={submitted}
                          onChange={() => setAnswers((a) => ({ ...a, [q.id]: optIdx }))}
                          className="mt-0.5 accent-primary"
                          aria-label={opt}
                        />
                        <span className="text-sm flex-1">{opt}</span>
                        {submitted && isThisCorrect ? (
                          <CheckCircle2 className="w-4 h-4 text-emerald-600" aria-label="Correct answer" />
                        ) : null}
                        {submitted && isThisSelected && !isThisCorrect ? (
                          <XCircle className="w-4 h-4 text-red-600" aria-label="Your answer" />
                        ) : null}
                      </label>
                    );
                  })}
                </div>
                {submitted ? (
                  <div className="mt-3 rounded border border-border bg-surface p-2 text-xs">
                    <span className={cn('font-bold mr-1', isCorrect ? 'text-emerald-700' : 'text-red-700')}>
                      {isCorrect ? 'Correct.' : 'Incorrect.'}
                    </span>
                    <span>{q.explanation}</span>
                  </div>
                ) : null}
              </fieldset>
            </CardContent>
          </Card>
        );
      })}

      {submitted ? (
        <Card padding="md" className={cn(passed ? 'border-emerald-400' : 'border-amber-400')}>
          <CardContent>
            <p className="font-bold text-sm">
              Score: <span className="text-lg">{score}%</span> — {passed ? 'Passed!' : `Need ${passPercent}% to mark this lesson complete. Retake the quiz to try again.`}
            </p>
          </CardContent>
        </Card>
      ) : (
        <div className="flex justify-end">
          <Button
            type="button"
            variant="primary"
            size="md"
            onClick={handleSubmit}
            disabled={!allAnswered}
            aria-disabled={!allAnswered}
          >
            Submit quiz
          </Button>
        </div>
      )}
    </div>
  );
}
