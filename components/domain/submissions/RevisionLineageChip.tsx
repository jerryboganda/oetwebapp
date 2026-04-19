'use client';

import React from 'react';
import { ChevronRight } from 'lucide-react';
import Link from 'next/link';
import { cn } from '@/lib/utils';
import type { RevisionNode } from '@/lib/mock-data';
import { OET_SCALED_MAX } from '@/lib/scoring';

/**
 * Horizontal chain widget showing Original → Revision 1 → Revision 2 …
 * Highlights the node representing the current attempt. Each node deep-links
 * to its own detail page so learners can navigate the revision chain without
 * leaving history context.
 */
export function RevisionLineageChip({ nodes }: { nodes: RevisionNode[] }) {
  if (!nodes || nodes.length <= 1) return null;
  return (
    <section className="rounded-[24px] border border-gray-200 bg-surface p-5 shadow-sm">
      <header className="mb-3">
        <p className="text-xs font-black uppercase tracking-widest text-muted">Revision lineage</p>
        <h3 className="mt-1 text-sm font-bold text-navy">
          Track score movement across the {nodes.length} attempt{nodes.length === 1 ? '' : 's'} in this chain
        </h3>
      </header>
      <ol className="flex items-center flex-wrap gap-2">
        {nodes.map((node, idx) => {
          const delta = idx > 0 && node.scaledScore !== null && nodes[idx - 1].scaledScore !== null
            ? node.scaledScore! - nodes[idx - 1].scaledScore!
            : null;
          return (
            <React.Fragment key={node.attemptId}>
              <li>
                <Link
                  href={`/submissions/${node.attemptId}`}
                  className={cn(
                    'block rounded-2xl px-3 py-2 border text-sm transition-colors',
                    node.isCurrent
                      ? 'bg-primary text-white border-primary'
                      : 'bg-white text-navy border-gray-200 hover:border-primary/50',
                  )}
                  aria-current={node.isCurrent ? 'true' : undefined}
                >
                  <div className="flex items-center gap-2">
                    <span className="text-xs font-black uppercase tracking-widest opacity-80">{node.label}</span>
                  </div>
                  <div className="mt-0.5 flex items-baseline gap-1">
                    <span className="text-lg font-black">
                      {node.scaledScore !== null ? node.scaledScore : '—'}
                    </span>
                    <span className="text-xs opacity-80">/ {OET_SCALED_MAX}</span>
                    {delta !== null ? (
                      <span className={cn(
                        'ml-2 text-xs font-bold',
                        node.isCurrent ? 'opacity-90' : delta > 0 ? 'text-emerald-600' : delta < 0 ? 'text-rose-600' : 'text-muted',
                      )}>
                        {delta > 0 ? '+' : ''}{delta}
                      </span>
                    ) : null}
                  </div>
                </Link>
              </li>
              {idx < nodes.length - 1 ? (
                <ChevronRight className="w-4 h-4 text-muted" aria-hidden />
              ) : null}
            </React.Fragment>
          );
        })}
      </ol>
    </section>
  );
}
