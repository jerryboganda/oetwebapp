'use client';

import { AlertTriangle, ArrowRight, FileText, Headphones, PenTool, Mic, BookOpen } from 'lucide-react';
import { CardLink } from '@/components/ui/card-link';
import type { SubTestReadiness } from '@/lib/mock-data';
import { cn } from '@/lib/utils';

interface ReadinessSubtestCardProps {
  test: SubTestReadiness;
  href?: string;
}

const ICONS: Record<string, React.ElementType> = {
  reading: FileText,
  listening: Headphones,
  writing: PenTool,
  speaking: Mic,
  vocabulary: BookOpen,
};

// Sub-test identity (DESIGN.md §2 skill tokens). The API's `color`/`bg`/`barColor` are hex values,
// not classes, so the four sub-tests never got a tint or a visible bar; other cards (vocabulary)
// pass token classes through those fields.
const SKILL_TONES: Record<string, { tile: string; bar: string }> = {
  reading: { tile: 'bg-skill-reading/10 text-skill-reading', bar: 'bg-skill-reading' },
  listening: { tile: 'bg-skill-listening/10 text-skill-listening', bar: 'bg-skill-listening' },
  writing: { tile: 'bg-skill-writing/10 text-skill-writing', bar: 'bg-skill-writing' },
  speaking: { tile: 'bg-skill-speaking/10 text-skill-speaking', bar: 'bg-skill-speaking' },
};

export function ReadinessSubtestCard({ test, href }: ReadinessSubtestCardProps) {
  const id = test.id?.toLowerCase();
  const Icon = ICONS[id] ?? FileText;
  const tone = SKILL_TONES[id] ?? { tile: cn(test.bg, test.color), bar: test.barColor };
  const target = test.target ?? 70;
  const value = Math.max(0, Math.min(100, Number(test.readiness ?? 0)));
  const linkHref = href ?? `/${id}`;
  const evidence = [
    test.confidenceBand ? `Confidence ${test.confidenceBand}` : null,
    test.dataPoints != null ? `${test.dataPoints} data points` : null,
  ].filter(Boolean).join(' · ');

  return (
    <CardLink href={linkHref} className="group flex h-full flex-col">
      {/* Wraps instead of overflowing: the score drops under the name when the card is narrow. */}
      <div className="mb-3 flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
        <div className="flex min-w-0 items-center gap-3">
          <div className={cn('flex h-10 w-10 shrink-0 items-center justify-center rounded-xl', tone.tile)}>
            <Icon className="h-5 w-5" aria-hidden="true" />
          </div>
          <div className="min-w-0">
            <h3 className="flex flex-wrap items-center gap-x-2 gap-y-1 text-base font-bold text-navy">
              {test.name}
              {test.isWeakest && (
                <span className="tile-label inline-flex items-center gap-1 rounded-full bg-danger/10 px-2 py-0.5 text-danger-strong">
                  <AlertTriangle className="h-3 w-3" aria-hidden="true" /> Weakest
                </span>
              )}
            </h3>
            <p className="text-xs text-muted">{test.status}</p>
          </div>
        </div>
        <div className="ms-auto whitespace-nowrap text-end tabular-nums">
          <span className="text-lg font-bold text-navy">{Math.round(value)}%</span>
          <span className="ms-1 text-xs text-muted">/ {target}% target</span>
        </div>
      </div>
      <div className="relative h-3 w-full overflow-hidden rounded-full bg-background-light">
        <div className="absolute inset-y-0 z-10 w-0.5 bg-border" style={{ insetInlineStart: `${target}%` }} aria-hidden="true" />
        <div className={cn('h-full rounded-full', tone.bar)} style={{ width: `${value}%` }} />
      </div>
      <div className="mt-auto flex items-center justify-between gap-2 pt-3 text-2xs text-muted">
        <span className="min-w-0 tabular-nums">{evidence}</span>
        <span className="inline-flex shrink-0 items-center gap-1 font-bold text-primary group-hover:underline">
          Practice <ArrowRight className="h-3.5 w-3.5 rtl:rotate-180" aria-hidden="true" />
        </span>
      </div>
    </CardLink>
  );
}
