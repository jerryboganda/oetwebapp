'use client';

import { cn } from '@/lib/utils';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { ArrowRight, Clock } from 'lucide-react';

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

export function TaskCard({ title, subtest, profession, duration, difficulty, description, onStart, className }: TaskCardProps) {
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
