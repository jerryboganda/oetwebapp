'use client';

import { cn } from '@/lib/utils';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge, type StatusType, StatusBadge } from '@/components/ui/badge';
import { Calendar, Clock, Play, Check, RotateCcw, ArrowLeftRight } from 'lucide-react';

interface StudyPlanItemProps {
  id: string;
  title: string;
  subtest: string;
  type: string; // task | drill | checkpoint | mock | review
  duration?: string;
  rationale?: string;
  status: StatusType;
  scheduledDate?: string;
  onStart?: () => void;
  onMarkDone?: () => void;
  onReschedule?: () => void;
  onSwap?: () => void;
  className?: string;
}

export function StudyPlanItem({
  title, subtest, type, duration, rationale, status,
  scheduledDate, onStart, onMarkDone, onReschedule, onSwap, className,
}: StudyPlanItemProps) {
  const isActionable = status === 'not_started' || status === 'in_progress';

  return (
    <Card hoverable={isActionable} className={cn(status === 'completed' && 'opacity-70', className)}>
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 mb-1">
            <StatusBadge status={status} />
            <Badge variant="muted" size="sm">{subtest}</Badge>
            <Badge variant="muted" size="sm">{type}</Badge>
          </div>
          <h4 className={cn('text-sm font-bold text-navy', status === 'completed' && 'line-through decoration-muted')}>{title}</h4>
          {rationale && <p className="text-xs text-muted mt-0.5">{rationale}</p>}
          <div className="flex items-center gap-3 mt-1.5 text-xs text-muted">
            {duration && <span className="flex items-center gap-1"><Clock className="w-3 h-3" />{duration}</span>}
            {scheduledDate && <span className="flex items-center gap-1"><Calendar className="w-3 h-3" />{scheduledDate}</span>}
          </div>
        </div>
        {isActionable && (
          <div className="flex flex-wrap items-center gap-2 sm:shrink-0">
            {onStart && <Button size="sm" onClick={onStart}><Play className="w-3.5 h-3.5" /> Start</Button>}
            {onMarkDone && <Button size="sm" variant="ghost" onClick={onMarkDone}><Check className="w-3.5 h-3.5" /> Done</Button>}
            {onReschedule && <Button size="sm" variant="ghost" onClick={onReschedule}><RotateCcw className="w-3.5 h-3.5" /></Button>}
            {onSwap && <Button size="sm" variant="ghost" onClick={onSwap}><ArrowLeftRight className="w-3.5 h-3.5" /></Button>}
          </div>
        )}
      </div>
    </Card>
  );
}
