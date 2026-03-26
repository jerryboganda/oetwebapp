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
    <Card className={cn('border-amber-200 bg-amber-50/50', className)}>
      <div className="flex items-start gap-3">
        <div className="w-10 h-10 rounded-full bg-amber-100 flex items-center justify-center text-amber-600 shrink-0">
          <AlertTriangle className="w-5 h-5" />
        </div>
        <div className="flex-1 min-w-0">
          <p className="text-xs text-amber-700 font-semibold uppercase tracking-wide">{subtest} · Weakest Area</p>
          <p className="text-sm font-bold text-navy mt-0.5">{criterion}</p>
          {description && <p className="text-xs text-muted mt-1">{description}</p>}
          {score && <p className="text-xs font-semibold text-amber-700 mt-1">Current: {score}</p>}
        </div>
      </div>
    </Card>
  );
}
