'use client';

import { cn } from '@/lib/utils';
import type { GrammarContentBlockLearner } from '@/lib/grammar/types';
import { MarkdownContent } from '@/components/ui/markdown-content';

/**
 * Grammar content is now rendered by the shared markdown component so table
 * blocks and other structured content stay consistent with the rest of the
 * learner surfaces.
 */
export function SafeRichText({ markdown, className }: { markdown: string; className?: string }) {
  return <MarkdownContent markdown={markdown} className={cn('text-sm leading-6 text-muted', className)} />;
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
  const baseCard = 'rounded-2xl border border-border bg-surface p-4 shadow-sm';

  switch (block.type) {
    case 'callout':
      return (
        <aside className="rounded-2xl border border-primary/20 bg-primary/5 p-4 text-sm leading-6 text-primary-dark">
          <SafeRichText markdown={block.contentMarkdown} className="text-primary-dark" />
        </aside>
      );
    case 'example':
      return (
        <div className="rounded-2xl border border-success/30 bg-success/10 p-4 text-sm leading-6 text-navy">
          <p className="mb-1 eyebrow text-success">Example</p>
          <SafeRichText markdown={block.contentMarkdown} className="text-navy" />
        </div>
      );
    case 'note':
      return (
        <div className="rounded-2xl border border-warning/30 bg-warning/10 p-4 text-sm leading-6 text-navy">
          <p className="mb-1 eyebrow text-warning">Note</p>
          <SafeRichText markdown={block.contentMarkdown} className="text-navy" />
        </div>
      );
    default:
      return (
        <div className={baseCard}>
          <SafeRichText markdown={block.contentMarkdown} className="text-navy" />
        </div>
      );
  }
}
