'use client';

import Link from 'next/link';
import { AlertTriangle, ArrowRight, FileText, Headphones, PenTool, Mic, BookOpen } from 'lucide-react';
import type { SubTestReadiness } from '@/lib/mock-data';

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

export function ReadinessSubtestCard({ test, href }: ReadinessSubtestCardProps) {
  const Icon = ICONS[test.id?.toLowerCase()] ?? FileText;
  const target = test.target ?? 70;
  const value = Math.max(0, Math.min(100, Number(test.readiness ?? 0)));
  const linkHref = href ?? `/${test.id?.toLowerCase()}`;

  return (
    <Link href={linkHref} className="block group focus:outline-none focus:ring-2 focus:ring-primary rounded-2xl">
      <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm transition-shadow hover:shadow-md">
        <div className="flex items-center justify-between mb-3">
          <div className="flex items-center gap-3">
            <div className={`w-10 h-10 rounded-xl ${test.bg} flex items-center justify-center shrink-0`}>
              <Icon className={`w-5 h-5 ${test.color}`} />
            </div>
            <div>
              <h3 className="text-base font-bold text-navy flex items-center gap-2">
                {test.name}
                {test.isWeakest && (
                  <span className="bg-danger/10 text-danger text-[10px] uppercase tracking-widest px-2 py-0.5 rounded-full font-bold flex items-center gap-1">
                    <AlertTriangle className="w-3 h-3" /> Weakest
                  </span>
                )}
              </h3>
              <p className="text-xs text-muted">{test.status}</p>
            </div>
          </div>
          <div className="text-right">
            <span className="text-lg font-bold text-navy">{Math.round(value)}%</span>
            <span className="text-xs text-muted ml-1">/ {target}% target</span>
          </div>
        </div>
        <div className="h-3 w-full bg-background-light rounded-full overflow-hidden relative">
          <div className="absolute top-0 bottom-0 w-0.5 bg-border z-10" style={{ left: `${target}%` }} />
          <div className={`h-full rounded-full ${test.barColor}`} style={{ width: `${value}%` }} />
        </div>
        <div className="mt-3 flex items-center justify-between text-[11px] text-muted">
          <span>
            {test.confidenceBand ? `Confidence ${test.confidenceBand}` : ''}
            {test.dataPoints != null ? ` · ${test.dataPoints} data points` : ''}
          </span>
          <span className="inline-flex items-center gap-1 text-primary font-bold group-hover:underline">
            Practice <ArrowRight className="w-3.5 h-3.5" />
          </span>
        </div>
      </div>
    </Link>
  );
}
