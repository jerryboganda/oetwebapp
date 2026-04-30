'use client';

import { useMemo, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent } from '@/components/ui/card';
import { gradeDrill } from '@/lib/writing-drills/grader';
import type { DrillGradeResult, RelevanceDrill } from '@/lib/writing-drills/types';
import { DrillResultPanel } from './drill-result';

interface RelevanceDrillProps {
  drill: RelevanceDrill;
  onGraded?: (result: DrillGradeResult) => void;
}

type Selection = 'relevant' | 'irrelevant';

export function RelevanceDrillComponent({ drill, onGraded }: RelevanceDrillProps) {
  const [selections, setSelections] = useState<Record<string, Selection>>({});
  const [result, setResult] = useState<DrillGradeResult | null>(null);

  const allAnswered = useMemo(
    () => drill.notes.every((n) => selections[n.id] !== undefined),
    [drill.notes, selections],
  );

  const choose = (noteId: string, value: Selection) => {
    if (result) return; // lock after grading
    setSelections((prev) => ({ ...prev, [noteId]: value }));
  };

  const submit = () => {
    const r = gradeDrill(drill, { type: 'relevance', selections });
    setResult(r);
    onGraded?.(r);
  };

  const reset = () => {
    setSelections({});
    setResult(null);
  };

  return (
    <div>
      <Card>
        <CardContent className="p-6 space-y-4">
          <header>
            <h2 className="text-lg font-semibold text-navy">Scenario</h2>
            <dl className="mt-2 grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-1 text-sm">
              <div>
                <dt className="text-muted text-xs uppercase">Patient</dt>
                <dd className="font-medium">{drill.scenario.patient}</dd>
              </div>
              <div>
                <dt className="text-muted text-xs uppercase">Writer</dt>
                <dd className="font-medium">{drill.scenario.writerRole}</dd>
              </div>
              <div>
                <dt className="text-muted text-xs uppercase">Recipient</dt>
                <dd className="font-medium">{drill.scenario.recipientRole}</dd>
              </div>
              <div>
                <dt className="text-muted text-xs uppercase">Purpose</dt>
                <dd className="font-medium">{drill.scenario.purpose}</dd>
              </div>
            </dl>
          </header>

          <div className="border-t border-border pt-4">
            <p className="text-sm text-muted mb-3">
              For each case note, decide whether the recipient needs it.
            </p>
            <ul className="space-y-2">
              {drill.notes.map((note) => {
                const selected = selections[note.id];
                return (
                  <li
                    key={note.id}
                    className="rounded-lg border border-border p-3 flex flex-col sm:flex-row sm:items-center gap-3"
                  >
                    <div className="flex-1">
                      <div className="text-xs text-muted uppercase tracking-wide">
                        {note.category}
                      </div>
                      <div className="text-sm">{note.text}</div>
                    </div>
                    <div className="flex gap-2">
                      <Button
                        type="button"
                        variant={selected === 'relevant' ? 'primary' : 'outline'}
                        size="sm"
                        onClick={() => choose(note.id, 'relevant')}
                        disabled={!!result}
                        aria-pressed={selected === 'relevant'}
                      >
                        Relevant
                      </Button>
                      <Button
                        type="button"
                        variant={selected === 'irrelevant' ? 'primary' : 'outline'}
                        size="sm"
                        onClick={() => choose(note.id, 'irrelevant')}
                        disabled={!!result}
                        aria-pressed={selected === 'irrelevant'}
                      >
                        Irrelevant
                      </Button>
                    </div>
                  </li>
                );
              })}
            </ul>
          </div>

          <div className="flex items-center justify-between gap-3 pt-2">
            <Badge variant="muted">
              {Object.keys(selections).length} / {drill.notes.length} answered
            </Badge>
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
