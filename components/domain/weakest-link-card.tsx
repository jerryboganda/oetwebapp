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
        'relative overflow-hidden border-violet-300/70 bg-gradient-to-br from-primary via-violet-700 to-primary-dark text-white shadow-lg shadow-violet-950/20 ring-1 ring-white/10 dark:border-violet-300/30 dark:shadow-violet-950/40',
        'before:absolute before:inset-y-0 before:left-0 before:w-1 before:bg-amber-300',
        className,
      )}
    >
      <div className="flex items-start gap-3">
        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-amber-200/20 text-amber-100 ring-1 ring-amber-100/35 backdrop-blur-sm">
          <AlertTriangle className="h-5 w-5" />
        </div>
        <div className="min-w-0 flex-1">
          <p className="text-xs font-black uppercase tracking-wide text-amber-100">{subtest} · Weakest Area</p>
          <p className="mt-0.5 text-sm font-black text-white">{criterion}</p>
          {description && <p className="mt-1 text-xs font-semibold text-violet-50/95">{description}</p>}
          {score && <p className="mt-1 text-xs font-bold text-amber-100">Current: {score}</p>}
        </div>
      </div>
    </Card>
  );
}
