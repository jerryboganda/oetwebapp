'use client';

import { useState } from 'react';
import { cn } from '@/lib/utils';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge, type StatusType, StatusBadge } from '@/components/ui/badge';
import { Calendar, Clock, Play, Check, RotateCcw, ArrowLeftRight, Info } from 'lucide-react';

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
  onMarkDone?: (feedbackRating?: number) => void;
  onReschedule?: () => void;
  onSwap?: () => void;
  className?: string;
}

export function StudyPlanItem({
  title, subtest, type, duration, rationale, status,
  scheduledDate, onStart, onMarkDone, onReschedule, onSwap, className,
}: StudyPlanItemProps) {
  const isActionable = status === 'not_started' || status === 'in_progress';
  const [showRationale, setShowRationale] = useState(false);
  const [showFeedback, setShowFeedback] = useState(false);

  const handleDone = (feedbackRating?: number) => {
    setShowFeedback(false);
    onMarkDone?.(feedbackRating);
  };

  return (
    <Card hoverable={isActionable} className={cn(status === 'completed' && 'opacity-70', className)}>
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 mb-1">
            <StatusBadge status={status} />
            <Badge variant="muted" size="sm">{subtest}</Badge>
            <Badge variant="muted" size="sm">{type}</Badge>
            {rationale && (
              <button
                type="button"
                onClick={() => setShowRationale((v) => !v)}
                aria-label="Why this task?"
                className="inline-flex items-center justify-center w-5 h-5 rounded-full hover:bg-muted/30"
              >
                <Info className="w-3.5 h-3.5 text-muted" />
              </button>
            )}
          </div>
          <h4 className={cn('text-sm font-bold text-navy', status === 'completed' && 'line-through decoration-muted')}>{title}</h4>
          {rationale && showRationale && (
            <div className="mt-1.5 p-2 bg-blue-50 border border-blue-200 rounded text-xs text-blue-900">
              <strong>Why this task:</strong> {rationale}
            </div>
          )}
          <div className="flex items-center gap-3 mt-1.5 text-xs text-muted">
            {duration && <span className="flex items-center gap-1"><Clock className="w-3 h-3" />{duration}</span>}
            {scheduledDate && <span className="flex items-center gap-1"><Calendar className="w-3 h-3" />{scheduledDate}</span>}
          </div>
        </div>
        {isActionable && (
          <div className="flex flex-wrap items-center gap-2 sm:shrink-0">
            {onStart && <Button size="sm" onClick={onStart}><Play className="w-3.5 h-3.5" /> Start</Button>}
            {onMarkDone && (
              <Button size="sm" variant="ghost" onClick={() => setShowFeedback(true)}>
                <Check className="w-3.5 h-3.5" /> Done
              </Button>
            )}
            {onReschedule && <Button size="sm" variant="ghost" onClick={onReschedule}><RotateCcw className="w-3.5 h-3.5" /></Button>}
            {onSwap && <Button size="sm" variant="ghost" onClick={onSwap}><ArrowLeftRight className="w-3.5 h-3.5" /></Button>}
          </div>
        )}
      </div>
      {showFeedback && onMarkDone && (
        <div className="mt-3 pt-3 border-t flex items-center gap-3 flex-wrap">
          <span className="text-xs text-muted">How was this task?</span>
          <Button size="sm" variant="ghost" onClick={() => handleDone(1)}>Too easy</Button>
          <Button size="sm" variant="ghost" onClick={() => handleDone(2)}>Just right</Button>
          <Button size="sm" variant="ghost" onClick={() => handleDone(3)}>Too hard</Button>
          <button
            onClick={() => handleDone(undefined)}
            className="text-xs text-muted underline ml-auto"
          >
            Skip feedback
          </button>
        </div>
      )}
    </Card>
  );
}
