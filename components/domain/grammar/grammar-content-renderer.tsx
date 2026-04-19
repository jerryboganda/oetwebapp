'use client';

import { cn } from '@/lib/utils';
import type { GrammarContentBlockLearner } from '@/lib/grammar/types';

/**
 * Minimal safe renderer for admin-authored grammar content. Accepts a
 * subset of Markdown-ish formatting (bold, italics, inline code, line
 * breaks) and renders paragraphs. Anything more exotic is treated as
 * plain text. Combined with server-side sanitisation in
 * `GrammarContentSanitiser.cs`, this keeps the `/grammar/[lessonId]`
 * page XSS-free without pulling in a full Markdown renderer bundle.
 */
export function SafeRichText({ markdown, className }: { markdown: string; className?: string }) {
  const paragraphs = markdown.split(/\n\s*\n/).filter(Boolean);

  return (
    <div className={cn('space-y-3 text-sm leading-6 text-muted', className)}>
      {paragraphs.map((p, i) => (
        <p key={i} className="whitespace-pre-wrap">
          {renderInline(p)}
        </p>
      ))}
    </div>
  );
}

function renderInline(text: string): React.ReactNode {
  const tokens: React.ReactNode[] = [];
  const regex = /(\*\*[^*\n]+\*\*|\*[^*\n]+\*|`[^`\n]+`)/g;
  let last = 0;
  let match: RegExpExecArray | null;
  let key = 0;
  while ((match = regex.exec(text)) !== null) {
    if (match.index > last) tokens.push(text.slice(last, match.index));
    const token = match[0];
    if (token.startsWith('**')) tokens.push(<strong key={key++}>{token.slice(2, -2)}</strong>);
    else if (token.startsWith('*')) tokens.push(<em key={key++}>{token.slice(1, -1)}</em>);
    else if (token.startsWith('`')) tokens.push(<code key={key++} className="rounded bg-background-light px-1 py-0.5 text-[0.85em] text-navy">{token.slice(1, -1)}</code>);
    last = regex.lastIndex;
  }
  if (last < text.length) tokens.push(text.slice(last));
  return tokens;
}

export function GrammarContentRenderer({ blocks }: { blocks: GrammarContentBlockLearner[] }) {
  if (!blocks || blocks.length === 0) return null;
  return (
    <div className="space-y-4">
      {blocks.map((b) => (
        <GrammarContentBlockView key={b.id} block={b} />
      ))}
    </div>
  );
}

function GrammarContentBlockView({ block }: { block: GrammarContentBlockLearner }) {
  const baseCard = 'rounded-2xl border border-border bg-white p-4 shadow-sm dark:border-gray-700 dark:bg-gray-800';

  switch (block.type) {
    case 'callout':
      return (
        <aside className="rounded-2xl border border-primary/20 bg-primary/5 p-4 text-sm leading-6 text-primary-dark">
          <SafeRichText markdown={block.contentMarkdown} className="text-primary-dark" />
        </aside>
      );
    case 'example':
      return (
        <div className="rounded-2xl border border-emerald-200 bg-emerald-50 p-4 text-sm leading-6 text-emerald-900 dark:border-emerald-900/40 dark:bg-emerald-900/20 dark:text-emerald-100">
          <p className="mb-1 text-[11px] font-semibold uppercase tracking-[0.15em] opacity-70">Example</p>
          <SafeRichText markdown={block.contentMarkdown} className="text-emerald-900 dark:text-emerald-100" />
        </div>
      );
    case 'note':
      return (
        <div className="rounded-2xl border border-amber-200 bg-amber-50 p-4 text-sm leading-6 text-amber-900 dark:border-amber-900/40 dark:bg-amber-900/20 dark:text-amber-100">
          <p className="mb-1 text-[11px] font-semibold uppercase tracking-[0.15em] opacity-70">Note</p>
          <SafeRichText markdown={block.contentMarkdown} className="text-amber-900 dark:text-amber-100" />
        </div>
      );
    default:
      return (
        <div className={baseCard}>
          <SafeRichText markdown={block.contentMarkdown} className="text-gray-800 dark:text-gray-200" />
        </div>
      );
  }
}
