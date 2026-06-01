'use client';

import { useState } from 'react';
import { Clock, BookOpen, CheckCircle2 } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { MarkdownContent } from '@/components/ui/markdown-content';

export interface LessonViewerLessonProps {
  id: string;
  title: string;
  bodyMarkdown: string;
  estimatedMinutes: number;
  videoUrl?: string | null;
}

export interface LessonViewerProps {
  lesson: LessonViewerLessonProps;
  /**
   * Called when the learner taps "Mark as read & start quiz" or the
   * parent decides the body has been consumed. Parent decides next
   * navigation (typically: show <QuizComponent />).
   */
  onComplete?: () => void;
  /**
   * Whether the lesson body has been marked complete. The lesson body
   * stays visible either way; this just toggles the CTA state.
   */
  bodyConsumed?: boolean;
  className?: string;
}

/**
 * Lesson body viewer. Renders the markdown body and exposes a
 * "Done — start quiz" CTA. The actual quiz is rendered separately
 * via `<QuizComponent />` so pages can lay them out either stacked
 * (mobile) or side-by-side (desktop) at their discretion.
 */
export function LessonViewer({ lesson, onComplete, bodyConsumed, className }: LessonViewerProps) {
  const [localConsumed, setLocalConsumed] = useState(false);
  const isConsumed = bodyConsumed ?? localConsumed;

  const handleComplete = () => {
    setLocalConsumed(true);
    onComplete?.();
  };

  return (
    <Card padding="lg" className={cn('flex flex-col gap-4', className)} aria-label={`Lesson: ${lesson.title}`}>
      <CardContent>
        <header className="flex items-center justify-between gap-3 flex-wrap">
          <div className="flex items-center gap-2 min-w-0">
            <BookOpen className="w-5 h-5 text-primary shrink-0" aria-hidden="true" />
            <h2 className="text-xl font-extrabold truncate">{lesson.title}</h2>
          </div>
          <span className="inline-flex items-center gap-1 text-xs font-bold text-muted">
            <Clock className="w-3.5 h-3.5" aria-hidden="true" /> {lesson.estimatedMinutes} min
          </span>
        </header>
        {lesson.videoUrl ? (
          <div className="my-4 aspect-video w-full overflow-hidden rounded-lg border border-border bg-background-dark">
            <iframe
              src={lesson.videoUrl}
              title={`${lesson.title} video lesson`}
              className="w-full h-full"
              allow="accelerometer; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
              allowFullScreen
            />
          </div>
        ) : null}
        <article className="mt-3">
          <MarkdownContent markdown={lesson.bodyMarkdown} className="text-sm leading-6 text-navy dark:text-white" />
        </article>
        <footer className="mt-5 flex items-center justify-end">
          {isConsumed ? (
            <span className="inline-flex items-center gap-1.5 text-sm font-bold text-success">
              <CheckCircle2 className="w-4 h-4" aria-hidden="true" /> Marked as read
            </span>
          ) : (
            <Button type="button" variant="primary" size="md" onClick={handleComplete}>
              Mark as read &amp; start quiz
            </Button>
          )}
        </footer>
      </CardContent>
    </Card>
  );
}
