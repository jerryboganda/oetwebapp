'use client';

import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { gradeDrill } from '@/lib/writing-drills/grader';
import type { AbbreviationDrill, DrillGradeResult } from '@/lib/writing-drills/types';
import { DrillResultPanel } from './drill-result';

interface AbbreviationDrillProps {
  drill: AbbreviationDrill;
  onGraded?: (result: DrillGradeResult) => void;
}

type Choice = 'expand' | 'keep';

export function AbbreviationDrillComponent({ drill, onGraded }: AbbreviationDrillProps) {
  const [answers, setAnswers] = useState<Record<string, Choice>>({});
  const [result, setResult] = useState<DrillGradeResult | null>(null);

  const allAnswered = drill.items.every((i) => answers[i.id] !== undefined);

  const submit = () => {
    const r = gradeDrill(drill, { type: 'abbreviation', answers });
    setResult(r);
    onGraded?.(r);
  };
  const reset = () => {
    setAnswers({});
    setResult(null);
  };

  return (
    <div>
      <Card>
        <CardContent className="p-6 space-y-4">
          <p className="text-sm text-muted">
            For each abbreviation, decide whether to expand it or keep it given the recipient.
          </p>
          <ul className="space-y-2">
            {drill.items.map((item) => {
              const chosen = answers[item.id];
              return (
                <li
                  key={item.id}
                  className="rounded-lg border border-border p-3 flex flex-col sm:flex-row sm:items-center gap-3"
                >
                  <div className="flex-1">
                    <div className="text-xs uppercase tracking-wide text-muted">{item.context}</div>
                    <div className="font-mono text-sm">{item.abbreviation}</div>
                  </div>
                  <div className="flex gap-2">
                    <Button
                      type="button"
                      variant={chosen === 'expand' ? 'primary' : 'outline'}
                      size="sm"
                      onClick={() =>
                        !result && setAnswers((prev) => ({ ...prev, [item.id]: 'expand' }))
                      }
                      disabled={!!result}
                      aria-pressed={chosen === 'expand'}
                    >
                      Expand
                    </Button>
                    <Button
                      type="button"
                      variant={chosen === 'keep' ? 'primary' : 'outline'}
                      size="sm"
                      onClick={() =>
                        !result && setAnswers((prev) => ({ ...prev, [item.id]: 'keep' }))
                      }
                      disabled={!!result}
                      aria-pressed={chosen === 'keep'}
                    >
                      Keep
                    </Button>
                  </div>
                </li>
              );
            })}
          </ul>

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
