'use client';

import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { gradeDrill } from '@/lib/writing-drills/grader';
import type { DrillGradeResult, OpeningDrill } from '@/lib/writing-drills/types';
import { DrillResultPanel } from './drill-result';

interface OpeningDrillProps {
  drill: OpeningDrill;
  onGraded?: (result: DrillGradeResult) => void;
}

export function OpeningDrillComponent({ drill, onGraded }: OpeningDrillProps) {
  const [choiceId, setChoiceId] = useState<string | null>(null);
  const [result, setResult] = useState<DrillGradeResult | null>(null);

  const submit = () => {
    if (!choiceId) return;
    const r = gradeDrill(drill, { type: 'opening', choiceId });
    setResult(r);
    onGraded?.(r);
  };
  const reset = () => {
    setChoiceId(null);
    setResult(null);
  };

  return (
    <div>
      <Card>
        <CardContent className="p-6 space-y-4">
          <header>
            <h2 className="text-lg font-semibold text-navy">Scenario</h2>
            <p className="text-sm text-muted mt-1">
              {drill.scenario.writerRole} writing to {drill.scenario.recipientRole}. Patient:{' '}
              {drill.scenario.patient}. Purpose: {drill.scenario.purpose}.
            </p>
          </header>

          <fieldset className="space-y-2">
            <legend className="text-sm font-medium text-muted mb-2">
              Pick the strongest opening sentence:
            </legend>
            {drill.choices.map((choice) => {
              const selected = choiceId === choice.id;
              return (
                <label
                  key={choice.id}
                  className={`block rounded-lg border p-3 cursor-pointer transition-colors ${
                    selected
                      ? 'border-primary bg-primary/5'
                      : 'border-border hover:border-primary/40'
                  } ${result ? 'cursor-default' : ''}`}
                >
                  <input
                    type="radio"
                    name={`opening-${drill.id}`}
                    value={choice.id}
                    checked={selected}
                    onChange={() => !result && setChoiceId(choice.id)}
                    disabled={!!result}
                    className="sr-only"
                  />
                  <p className="text-sm">{choice.text}</p>
                </label>
              );
            })}
          </fieldset>

          <div className="flex items-center justify-end gap-3 pt-2">
            {result ? (
              <Button variant="secondary" onClick={reset}>
                Try again
              </Button>
            ) : (
              <Button onClick={submit} disabled={!choiceId}>
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
