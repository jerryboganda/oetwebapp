'use client';

import { Badge } from '@/components/ui/badge';
import { Card, CardContent } from '@/components/ui/card';
import type { Drill, DrillGradeResult } from '@/lib/writing-drills/types';

interface DrillResultPanelProps {
  drill: Drill;
  result: DrillGradeResult;
}

function findItemLabel(drill: Drill, itemId: string): string {
  switch (drill.type) {
    case 'relevance':
      return drill.notes.find((n) => n.id === itemId)?.text ?? itemId;
    case 'opening':
      return drill.choices.find((c) => c.id === itemId)?.text ?? itemId;
    case 'ordering':
      return drill.items.find((i) => i.id === itemId)?.text ?? itemId;
    case 'expansion':
      return drill.targets.find((t) => t.id === itemId)?.noteForm ?? itemId;
    case 'tone':
      return drill.items.find((i) => i.id === itemId)?.informal ?? itemId;
    case 'abbreviation': {
      const item = drill.items.find((i) => i.id === itemId);
      return item ? `${item.abbreviation} — ${item.context}` : itemId;
    }
    default:
      return itemId;
  }
}

export function DrillResultPanel({ drill, result }: DrillResultPanelProps) {
  return (
    <Card className="mt-6">
      <CardContent className="p-6 space-y-5">
        <div className="flex items-center justify-between flex-wrap gap-3">
          <div>
            <h2 className="text-xl font-semibold text-navy">Result</h2>
            <p className="text-sm text-muted">{result.summary}</p>
          </div>
          <div className="flex items-center gap-3">
            <span className="text-3xl font-bold tabular-nums">{result.scorePercent}%</span>
            <Badge variant={result.passed ? 'success' : 'danger'}>
              {result.passed ? 'Pass' : 'Review needed'}
            </Badge>
          </div>
        </div>

        {result.errorTags.length > 0 && (
          <div className="rounded-lg border border-amber-300 bg-amber-50 p-3">
            <p className="text-xs uppercase tracking-wide font-semibold text-amber-900 mb-2">
              Areas to work on
            </p>
            <div className="flex flex-wrap gap-2">
              {result.errorTags.map((tag) => (
                <Badge key={tag} variant="warning" size="sm">
                  {tag.replaceAll('_', ' ')}
                </Badge>
              ))}
            </div>
          </div>
        )}

        <ul className="space-y-3">
          {result.findings.map((f) => (
            <li
              key={f.itemId}
              className={`rounded-lg border p-3 ${f.correct ? 'border-emerald-200 bg-emerald-50' : 'border-rose-200 bg-rose-50'}`}
            >
              <p className="text-xs font-medium text-muted mb-1">
                {findItemLabel(drill, f.itemId)}
              </p>
              <p className={`text-sm ${f.correct ? 'text-emerald-900' : 'text-rose-900'}`}>
                {f.feedback}
              </p>
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  );
}
