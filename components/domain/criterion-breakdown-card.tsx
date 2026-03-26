import { cn } from '@/lib/utils';
import { Card } from '@/components/ui/card';
import { ProgressBar } from '@/components/ui/progress';
import { Badge } from '@/components/ui/badge';

interface CriterionBreakdownCardProps {
  criterion: string;
  score?: number; // 0-100
  grade?: string;
  explanation?: string;
  strengths?: string[];
  issues?: string[];
  className?: string;
}

export function CriterionBreakdownCard({ criterion, score, grade, explanation, strengths, issues, className }: CriterionBreakdownCardProps) {
  const color = score !== undefined
    ? score >= 70 ? 'success' : score >= 50 ? 'primary' : 'danger'
    : 'primary';

  return (
    <Card className={cn('', className)}>
      <div className="flex items-center justify-between mb-3">
        <h4 className="text-sm font-bold text-navy">{criterion}</h4>
        {grade && <Badge variant={color === 'success' ? 'success' : color === 'danger' ? 'danger' : 'default'}>{grade}</Badge>}
      </div>
      {score !== undefined && (
        <ProgressBar value={score} color={color as 'primary' | 'success' | 'danger'} className="mb-3" />
      )}
      {explanation && <p className="text-xs text-muted mb-3">{explanation}</p>}
      {strengths && strengths.length > 0 && (
        <div className="mb-2">
          <p className="text-xs font-semibold text-emerald-700 mb-1">Strengths</p>
          <ul className="text-xs text-navy space-y-0.5">
            {strengths.map((s, i) => <li key={i} className="flex items-start gap-1.5"><span className="text-emerald-500 mt-0.5">✓</span>{s}</li>)}
          </ul>
        </div>
      )}
      {issues && issues.length > 0 && (
        <div>
          <p className="text-xs font-semibold text-red-700 mb-1">Areas to Improve</p>
          <ul className="text-xs text-navy space-y-0.5">
            {issues.map((s, i) => <li key={i} className="flex items-start gap-1.5"><span className="text-red-500 mt-0.5">•</span>{s}</li>)}
          </ul>
        </div>
      )}
    </Card>
  );
}
