'use client';

import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { gradeDrill } from '@/lib/writing-drills/grader';
import type { DrillGradeResult, ToneDrill } from '@/lib/writing-drills/types';
import { DrillResultPanel } from './drill-result';

interface ToneDrillProps {
  drill: ToneDrill;
  onGraded?: (result: DrillGradeResult) => void;
}

export function ToneDrillComponent({ drill, onGraded }: ToneDrillProps) {
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [result, setResult] = useState<DrillGradeResult | null>(null);

  const allAnswered = drill.items.every((i) => (answers[i.id] ?? '').trim().length > 0);

  const submit = () => {
    const r = gradeDrill(drill, { type: 'tone', answers });
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
            Rewrite each casual sentence in a professional clinical register.
          </p>
          <div className="space-y-4">
            {drill.items.map((item) => (
              <div key={item.id} className="rounded-lg border border-border p-3">
                <div className="text-xs uppercase tracking-wide text-muted mb-1">Informal</div>
                <p className="text-sm bg-background-light px-2 py-1 rounded mb-3">
                  {item.informal}
                </p>
                <label
                  htmlFor={`tone-${item.id}`}
                  className="block text-xs uppercase tracking-wide text-muted mb-1"
                >
                  Professional version
                </label>
                <textarea
                  id={`tone-${item.id}`}
                  value={answers[item.id] ?? ''}
                  onChange={(e) =>
                    !result && setAnswers((prev) => ({ ...prev, [item.id]: e.target.value }))
                  }
                  disabled={!!result}
                  rows={2}
                  className="w-full rounded-md border border-border p-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary disabled:bg-background-light"
                  placeholder="Rewrite in formal clinical register..."
                />
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
