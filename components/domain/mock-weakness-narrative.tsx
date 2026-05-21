'use client';

import React from 'react';
import Link from 'next/link';
import { AlertTriangle, Sparkles, ArrowRight, Target } from 'lucide-react';

export interface WeaknessNarrativeTag {
  tag: string;
  subtest: string;
  description: string;
  drillId?: string | null;
  drillRouteHref?: string | null;
}

export interface WeaknessNarrativeFallback {
  subtest: string;
  criterion: string;
  description: string;
}

export interface WeaknessNarrativeProps {
  headline?: string | null;
  body?: string | null;
  tags?: WeaknessNarrativeTag[];
  fallback: WeaknessNarrativeFallback;
  className?: string;
}

/**
 * WeaknessNarrative — V1 payload renderer for the aggregated weakness story.
 *
 * Behaviour:
 *  - When `headline` / `body` / `tags` are populated by the Phase 1.5
 *    aggregator, render the rich narrative card with clickable drill chips.
 *  - When the V1 payload has not yet been populated (pre-Phase 1.5 reports),
 *    fall back to the legacy single-criterion card driven by `fallback`.
 *
 * The component intentionally mirrors the visual idiom of the existing
 * "Area for Improvement" block on the mock report page (danger-tinted card,
 * AlertTriangle icon, uppercase muted micro-label) so the insert reads as a
 * natural evolution of the legacy section, not a separate widget.
 */
export function WeaknessNarrative({
  headline,
  body,
  tags,
  fallback,
  className,
}: WeaknessNarrativeProps) {
  const hasNarrative = Boolean(
    (headline && headline.trim().length > 0) || (body && body.trim().length > 0),
  );
  const hasTags = Array.isArray(tags) && tags.length > 0;

  // Fallback path — V1 narrative not yet aggregated.
  if (!hasNarrative && !hasTags) {
    return (
      <div
        className={
          'bg-danger/10 rounded-2xl border border-danger/30 p-6 sm:p-8 ' +
          (className ?? '')
        }
      >
        <div className="flex items-start gap-4">
          <div className="w-12 h-12 rounded-2xl bg-danger/10 flex items-center justify-center shrink-0">
            <AlertTriangle className="w-6 h-6 text-danger" />
          </div>
          <div>
            <div className="flex items-center gap-2 mb-1">
              <span className="text-xs font-black text-danger uppercase tracking-widest">
                {fallback.subtest}
              </span>
              <span className="text-danger/40">•</span>
              <span className="text-xs font-black text-danger uppercase tracking-widest">
                Weakest Criterion
              </span>
            </div>
            <h3 className="text-lg font-black text-danger mb-2">{fallback.criterion}</h3>
            <p className="text-sm text-danger/80 leading-relaxed">{fallback.description}</p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div
      className={
        'rounded-2xl border border-danger/30 bg-danger/5 p-6 sm:p-8 shadow-sm ' +
        (className ?? '')
      }
    >
      <div className="flex items-start gap-4">
        <div className="w-12 h-12 rounded-2xl bg-danger/10 flex items-center justify-center shrink-0">
          <Sparkles className="w-6 h-6 text-danger" />
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 mb-1">
            <span className="text-xs font-black text-danger uppercase tracking-widest">
              Personalised weakness analysis
            </span>
          </div>
          {headline ? (
            <h3 className="text-lg font-black text-navy mb-2 leading-tight">{headline}</h3>
          ) : null}
          {body ? (
            <p className="text-sm text-navy/80 leading-relaxed">{body}</p>
          ) : null}

          {hasTags ? (
            <div className="mt-5">
              <div className="mb-2 flex items-center gap-2 text-xs font-black uppercase tracking-widest text-muted">
                <Target className="w-3.5 h-3.5" />
                Targeted drills
              </div>
              <ul className="grid gap-2 sm:grid-cols-2">
                {tags!.map((t, idx) => {
                  const href = t.drillRouteHref ?? null;
                  const chipBase =
                    'group flex h-full items-start gap-2 rounded-xl border border-border bg-surface p-3 text-left transition-colors';
                  const chipInteractive =
                    'hover:border-primary/40 hover:bg-primary/5 focus:outline-none focus:ring-2 focus:ring-primary/40';
                  const inner = (
                    <>
                      <span className="mt-0.5 inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-md bg-primary/10 text-primary">
                        <ArrowRight className="h-3.5 w-3.5" />
                      </span>
                      <span className="min-w-0 flex-1">
                        <span className="flex flex-wrap items-center gap-x-2 gap-y-0.5">
                          <span className="text-sm font-black text-navy">{t.tag}</span>
                          {t.subtest ? (
                            <span className="text-[10px] font-black uppercase tracking-widest text-muted">
                              {t.subtest}
                            </span>
                          ) : null}
                        </span>
                        <span className="mt-1 block text-xs leading-5 text-muted">
                          {t.description}
                        </span>
                      </span>
                    </>
                  );
                  const key = `${t.tag || 'tag'}-${t.subtest || 'subtest'}-${idx}`;
                  return (
                    <li key={key}>
                      {href ? (
                        <Link href={href} className={`${chipBase} ${chipInteractive}`}>
                          {inner}
                        </Link>
                      ) : (
                        <div className={chipBase}>{inner}</div>
                      )}
                    </li>
                  );
                })}
              </ul>
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
}

export default WeaknessNarrative;
