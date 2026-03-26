'use client';

import { cn } from '@/lib/utils';
import { Card } from '@/components/ui/card';
import { Badge, StatusBadge, type StatusType } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { ArrowRight, Clock, User } from 'lucide-react';

/* ─── Task Card ─── */
interface TaskCardProps {
  id: string;
  title: string;
  subtest?: string;
  profession?: string;
  duration?: string;
  difficulty?: string;
  description?: string;
  tags?: string[];
  onStart?: () => void;
  className?: string;
}

export function TaskCard({ id, title, subtest, profession, duration, difficulty, description, tags, onStart, className }: TaskCardProps) {
  return (
    <Card hoverable className={cn('', className)}>
      <div className="flex flex-col gap-2">
        <div className="flex items-center gap-2 flex-wrap">
          {subtest && <Badge variant="default" size="sm">{subtest}</Badge>}
          {difficulty && <Badge variant={difficulty === 'Hard' ? 'danger' : difficulty === 'Medium' ? 'warning' : 'success'} size="sm">{difficulty}</Badge>}
          {profession && <Badge variant="muted" size="sm">{profession}</Badge>}
        </div>
        <h4 className="text-sm font-bold text-navy">{title}</h4>
        {description && <p className="text-xs text-muted line-clamp-2">{description}</p>}
        <div className="flex items-center justify-between mt-1">
          <div className="flex items-center gap-3 text-xs text-muted">
            {duration && <span className="flex items-center gap-1"><Clock className="w-3 h-3" />{duration}</span>}
          </div>
          {onStart && (
            <Button size="sm" onClick={onStart}>
              Start <ArrowRight className="w-3.5 h-3.5" />
            </Button>
          )}
        </div>
      </div>
    </Card>
  );
}

/* ─── Submission Card ─── */
interface SubmissionCardProps {
  id: string;
  title: string;
  subtest: string;
  date: string;
  status: StatusType;
  score?: string;
  grade?: string;
  reviewStatus?: string;
  onViewFeedback?: () => void;
  onRequestReview?: () => void;
  className?: string;
}

export function SubmissionCard({
  id, title, subtest, date, status, score, grade, reviewStatus,
  onViewFeedback, onRequestReview, className,
}: SubmissionCardProps) {
  return (
    <Card className={cn('', className)}>
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 mb-1">
            <StatusBadge status={status} />
            <Badge variant="muted" size="sm">{subtest}</Badge>
          </div>
          <h4 className="text-sm font-bold text-navy">{title}</h4>
          <div className="flex items-center gap-3 mt-1 text-xs text-muted">
            <span>{date}</span>
            {score && <span className="font-semibold text-navy">{score}</span>}
            {grade && <Badge size="sm">{grade}</Badge>}
            {reviewStatus && <span className="text-primary font-semibold">{reviewStatus}</span>}
          </div>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          {onViewFeedback && <Button size="sm" variant="outline" onClick={onViewFeedback}>View Feedback</Button>}
          {onRequestReview && <Button size="sm" variant="ghost" onClick={onRequestReview}>Request Review</Button>}
        </div>
      </div>
    </Card>
  );
}
