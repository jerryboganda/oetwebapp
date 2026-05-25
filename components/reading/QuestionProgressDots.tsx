'use client';

import { cn } from '@/lib/utils';

interface QuestionProgressDotsProps {
  total: number;
  current: number;
  answered: Set<string>;
  marked: Set<string>;
  questionIds: string[];
}

export default function QuestionProgressDots({
  total,
  current,
  answered,
  marked,
  questionIds,
}: QuestionProgressDotsProps) {
  return (
    <div className="flex flex-wrap gap-1.5" role="list" aria-label="Question progress">
      {Array.from({ length: total }, (_, i) => {
        const qid = questionIds[i];
        const isAnswered = qid ? answered.has(qid) : false;
        const isMarked = qid ? marked.has(qid) : false;
        const isCurrent = i === current;

        return (
          <div
            key={i}
            role="listitem"
            aria-label={`Question ${i + 1}${isAnswered ? ' answered' : ''}${isMarked ? ' marked for review' : ''}${isCurrent ? ' current' : ''}`}
            className={cn(
              'h-4 w-4 rounded-full transition-all',
              isCurrent && 'ring-2 ring-primary ring-offset-1',
              isMarked
                ? 'bg-orange-400'
                : isAnswered
                  ? 'bg-emerald-500'
                  : 'bg-slate-200 dark:bg-slate-700',
            )}
          />
        );
      })}
    </div>
  );
}
