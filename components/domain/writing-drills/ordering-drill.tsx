'use client';

import { useState } from 'react';
import { ArrowDown, ArrowUp } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { gradeDrill } from '@/lib/writing-drills/grader';
import type { DrillGradeResult, OrderingDrill } from '@/lib/writing-drills/types';
import { DrillResultPanel } from './drill-result';

interface OrderingDrillProps {
  drill: OrderingDrill;
  onGraded?: (result: DrillGradeResult) => void;
}

// Deterministic shuffle so server-render and client-render match.
function deterministicShuffle<T extends { id: string }>(arr: T[]): T[] {
  return [...arr].sort((a, b) => a.id.localeCompare(b.id));
}

export function OrderingDrillComponent({ drill, onGraded }: OrderingDrillProps) {
  const [order, setOrder] = useState<string[]>(() =>
    deterministicShuffle(drill.items).map((i) => i.id),
  );
  const [result, setResult] = useState<DrillGradeResult | null>(null);

  const move = (idx: number, delta: number) => {
    if (result) return;
    const next = [...order];
    const target = idx + delta;
    if (target < 0 || target >= next.length) return;
    [next[idx], next[target]] = [next[target], next[idx]];
    setOrder(next);
  };

  const submit = () => {
    const r = gradeDrill(drill, { type: 'ordering', order });
    setResult(r);
    onGraded?.(r);
  };

  const reset = () => {
    setOrder(deterministicShuffle(drill.items).map((i) => i.id));
    setResult(null);
  };

  return (
    <div>
      <Card>
        <CardContent className="p-6 space-y-4">
          <p className="text-sm text-muted">
            Re-order the paragraphs into a logical letter sequence (purpose first, action last).
          </p>
          <ol className="space-y-2">
            {order.map((id, idx) => {
              const item = drill.items.find((i) => i.id === id);
              if (!item) return null;
              return (
                <li
                  key={id}
                  className="rounded-lg border border-border p-3 flex items-start gap-3"
                >
                  <div className="flex flex-col gap-1 pt-1">
                    <button
                      type="button"
                      onClick={() => move(idx, -1)}
                      disabled={idx === 0 || !!result}
                      className="p-1 rounded border border-border hover:bg-background-light disabled:opacity-30 disabled:cursor-not-allowed"
                      aria-label={`Move paragraph ${idx + 1} up`}
                    >
                      <ArrowUp className="w-3 h-3" />
                    </button>
                    <button
                      type="button"
                      onClick={() => move(idx, 1)}
                      disabled={idx === order.length - 1 || !!result}
                      className="p-1 rounded border border-border hover:bg-background-light disabled:opacity-30 disabled:cursor-not-allowed"
                      aria-label={`Move paragraph ${idx + 1} down`}
                    >
                      <ArrowDown className="w-3 h-3" />
                    </button>
                  </div>
                  <div className="flex-1">
                    <div className="text-xs uppercase tracking-wide text-muted mb-1">
                      Paragraph {idx + 1}
                    </div>
                    <p className="text-sm">{item.text}</p>
                  </div>
                </li>
              );
            })}
          </ol>

          <div className="flex justify-end gap-3 pt-2">
            {result ? (
              <Button variant="secondary" onClick={reset}>
                Try again
              </Button>
            ) : (
              <Button onClick={submit}>Submit order</Button>
            )}
          </div>
        </CardContent>
      </Card>

      {result && <DrillResultPanel drill={drill} result={result} />}
    </div>
  );
}
