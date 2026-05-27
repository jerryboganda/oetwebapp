'use client';

import { useMemo, useState, type ReactNode } from 'react';
import { Clock, BookOpen, CheckCircle2 } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';

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
 * Minimal CommonMark renderer. Lesson bodies are admin-authored
 * (Dr Ahmed / tutor team) so a small safe subset is acceptable.
 * Supports: H1-H3, paragraphs, bold, italic, code spans, lists,
 * blockquotes, links. NO raw HTML pass-through.
 *
 * If a richer feature set is needed later, swap to react-markdown
 * + remark-gfm — the prop interface here stays stable.
 */
function renderInline(text: string): ReactNode[] {
  const nodes: ReactNode[] = [];
  let cursor = 0;
  let idx = 0;
  // Matches code spans, bold, italic, links — in that order of precedence.
  const pattern = /(`([^`]+)`)|(\*\*([^*]+)\*\*)|(\*([^*]+)\*)|(\[([^\]]+)\]\(([^)]+)\))/g;
  let match: RegExpExecArray | null;
  while ((match = pattern.exec(text)) !== null) {
    if (match.index > cursor) nodes.push(text.slice(cursor, match.index));
    if (match[1]) {
      nodes.push(
        <code key={`c-${idx++}`} className="rounded bg-slate-100 dark:bg-slate-800 px-1 py-0.5 text-xs">
          {match[2]}
        </code>,
      );
    } else if (match[3]) {
      nodes.push(<strong key={`b-${idx++}`}>{match[4]}</strong>);
    } else if (match[5]) {
      nodes.push(<em key={`i-${idx++}`}>{match[6]}</em>);
    } else if (match[7]) {
      nodes.push(
        <a key={`a-${idx++}`} href={match[9]} className="underline text-primary" target="_blank" rel="noreferrer">
          {match[8]}
        </a>,
      );
    }
    cursor = match.index + match[0].length;
  }
  if (cursor < text.length) nodes.push(text.slice(cursor));
  return nodes;
}

function renderMarkdown(md: string): ReactNode {
  const lines = md.split(/\r?\n/);
  const blocks: ReactNode[] = [];
  let i = 0;
  let key = 0;

  while (i < lines.length) {
    const line = lines[i];
    if (/^\s*$/.test(line)) {
      i++;
      continue;
    }
    // H1-H3
    const heading = /^(#{1,3})\s+(.*)$/.exec(line);
    if (heading) {
      const level = heading[1].length;
      const text = heading[2];
      const className =
        level === 1
          ? 'text-2xl font-extrabold mt-4 mb-2'
          : level === 2
            ? 'text-xl font-bold mt-4 mb-2'
            : 'text-lg font-bold mt-3 mb-1';
      const Tag = (level === 1 ? 'h1' : level === 2 ? 'h2' : 'h3') as 'h1' | 'h2' | 'h3';
      blocks.push(
        <Tag key={`h-${key++}`} className={className}>
          {renderInline(text)}
        </Tag>,
      );
      i++;
      continue;
    }
    // Unordered list
    if (/^\s*[-*]\s+/.test(line)) {
      const items: string[] = [];
      while (i < lines.length && /^\s*[-*]\s+/.test(lines[i])) {
        items.push(lines[i].replace(/^\s*[-*]\s+/, ''));
        i++;
      }
      blocks.push(
        <ul key={`ul-${key++}`} className="list-disc list-inside space-y-1 my-2 ml-2">
          {items.map((it, idx) => (
            <li key={idx}>{renderInline(it)}</li>
          ))}
        </ul>,
      );
      continue;
    }
    // Ordered list
    if (/^\s*\d+\.\s+/.test(line)) {
      const items: string[] = [];
      while (i < lines.length && /^\s*\d+\.\s+/.test(lines[i])) {
        items.push(lines[i].replace(/^\s*\d+\.\s+/, ''));
        i++;
      }
      blocks.push(
        <ol key={`ol-${key++}`} className="list-decimal list-inside space-y-1 my-2 ml-2">
          {items.map((it, idx) => (
            <li key={idx}>{renderInline(it)}</li>
          ))}
        </ol>,
      );
      continue;
    }
    // Blockquote
    if (/^>\s?/.test(line)) {
      const quoted: string[] = [];
      while (i < lines.length && /^>\s?/.test(lines[i])) {
        quoted.push(lines[i].replace(/^>\s?/, ''));
        i++;
      }
      blocks.push(
        <blockquote
          key={`bq-${key++}`}
          className="border-l-4 border-primary/40 bg-primary/5 pl-3 py-1 italic my-2 text-sm"
        >
          {renderInline(quoted.join(' '))}
        </blockquote>,
      );
      continue;
    }
    // Paragraph — coalesce contiguous non-blank lines
    const para: string[] = [line];
    i++;
    while (i < lines.length && !/^\s*$/.test(lines[i]) && !/^#{1,3}\s/.test(lines[i]) && !/^\s*[-*]\s/.test(lines[i]) && !/^\s*\d+\.\s/.test(lines[i]) && !/^>\s?/.test(lines[i])) {
      para.push(lines[i]);
      i++;
    }
    blocks.push(
      <p key={`p-${key++}`} className="my-2 leading-relaxed">
        {renderInline(para.join(' '))}
      </p>,
    );
  }
  return blocks;
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

  const rendered = useMemo(() => renderMarkdown(lesson.bodyMarkdown), [lesson.bodyMarkdown]);

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
          <div className="my-4 aspect-video w-full overflow-hidden rounded-lg border border-border bg-black">
            <iframe
              src={lesson.videoUrl}
              title={`${lesson.title} video lesson`}
              className="w-full h-full"
              allow="accelerometer; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
              allowFullScreen
            />
          </div>
        ) : null}
        <article
          className="prose prose-sm dark:prose-invert max-w-none mt-3 text-sm text-navy dark:text-white"
        >
          {rendered}
        </article>
        <footer className="mt-5 flex items-center justify-end">
          {isConsumed ? (
            <span className="inline-flex items-center gap-1.5 text-sm font-bold text-emerald-700 dark:text-emerald-300">
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
