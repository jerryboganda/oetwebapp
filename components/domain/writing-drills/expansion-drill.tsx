'use client';

import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { gradeDrill } from '@/lib/writing-drills/grader';
import type { DrillGradeResult, ExpansionDrill } from '@/lib/writing-drills/types';
import { DrillResultPanel } from './drill-result';

interface ExpansionDrillProps {
  drill: ExpansionDrill;
  onGraded?: (result: DrillGradeResult) => void;
}

export function ExpansionDrillComponent({ drill, onGraded }: ExpansionDrillProps) {
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [result, setResult] = useState<DrillGradeResult | null>(null);

  const submit = () => {
    const r = gradeDrill(drill, { type: 'expansion', answers });
    setResult(r);
    onGraded?.(r);
  };
  const reset = () => {
    setAnswers({});
    setResult(null);
  };

  const allAnswered = drill.targets.every(
    (t) => (answers[t.id] ?? '').trim().length > 0,
  );

  return (
    <div>
      <Card>
        <CardContent className="p-6 space-y-4">
          <p className="text-sm text-muted">
            Convert each note-form line into a complete, professional sentence.
          </p>
          <div className="space-y-4">
            {drill.targets.map((target) => (
              <div key={target.id} className="rounded-lg border border-border p-3">
                <div className="text-xs uppercase tracking-wide text-muted mb-1">Note form</div>
                <p className="text-sm font-mono bg-background-light px-2 py-1 rounded mb-3">
                  {target.noteForm}
                </p>
                <label
                  htmlFor={`expansion-${target.id}`}
                  className="block text-xs uppercase tracking-wide text-muted mb-1"
                >
                  Your sentence
                </label>
                <textarea
                  id={`expansion-${target.id}`}
                  value={answers[target.id] ?? ''}
                  onChange={(e) =>
                    !result &&
                    setAnswers((prev) => ({ ...prev, [target.id]: e.target.value }))
                  }
                  disabled={!!result}
                  rows={2}
                  className="w-full rounded-md border border-border p-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary disabled:bg-background-light"
                  placeholder="Write a complete clinical sentence..."
                />
                <div className="text-xs text-muted mt-1">
                  Target: {target.minWords}–{target.maxWords} words.
                </div>
              </div>
            ))}
          </div>

          <div className="flex justify-end gap-3 pt-2">
            {result ? (
              <Button variant="secondary" onClick={reset}>
                Try again
              </Button>
            ) : (
              <Button onClick={submit} disabled={!allAnswered}>
                Submit
              </Button>
            )}
          </div>
        </CardContent>
      </Card>

      {result && <DrillResultPanel drill={drill} result={result} />}
    </div>
  );
}
