import { cn } from '@/lib/utils';
import { AlertTriangle } from 'lucide-react';
import { Card } from '@/components/ui/card';

interface WeakestLinkCardProps {
  criterion: string;
  subtest: string;
  description?: string;
  score?: string;
  className?: string;
}

export function WeakestLinkCard({ criterion, subtest, description, score, className }: WeakestLinkCardProps) {
  return (
    <Card
      className={cn(
        'relative overflow-hidden border-border bg-surface shadow-sm',
        className,
      )}
    >
      <div className="flex items-start gap-3">
        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-warning/10 text-warning">
          <AlertTriangle className="h-5 w-5" />
        </div>
        <div className="min-w-0 flex-1">
          <p className="text-xs font-bold uppercase tracking-wide text-warning">{subtest} · Weakest Area</p>
          <p className="mt-0.5 text-sm font-bold text-navy">{criterion}</p>
          {description && <p className="mt-1 text-xs font-semibold text-muted">{description}</p>}
          {score && <p className="mt-1 text-xs font-bold text-muted">Current: {score}</p>}
        </div>
      </div>
    </Card>
  );
}
