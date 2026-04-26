'use client';

import { LearnerPageHero } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { fetchProgramsBrowser } from '@/lib/api';
import type { BrowsableProgramItem, PaginatedResponse } from '@/lib/types/content-hierarchy';
import { BookOpen, ChevronRight, Clock, Lock, Unlock } from 'lucide-react';
import Link from 'next/link';
import { useEffect, useState } from 'react';

const TYPE_LABELS: Record<string, string> = {
  full_course: 'Full Course',
  crash_course: 'Crash Course',
  foundation: 'Foundation',
  combo: 'Combo',
};

export default function ProgramBrowserPage() {
  const [programs, setPrograms] = useState<BrowsableProgramItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [typeFilter, setTypeFilter] = useState('');

  const changeFilter = (value: string) => {
    setLoading(true);
    setTypeFilter(value);
  };

  useEffect(() => {
    analytics.track('program_browser_viewed');
  }, []);

  useEffect(() => {
    let cancelled = false;
    fetchProgramsBrowser({ type: typeFilter || undefined })
      .then((data) => {
        if (cancelled) return;
        const response = data as PaginatedResponse<BrowsableProgramItem>;
        setPrograms(response.items ?? []);
        setLoading(false);
      })
      .catch(() => {
        if (cancelled) return;
        setError('Unable to load programs.');
        setLoading(false);
      });
    return () => { cancelled = true; };
  }, [typeFilter]);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Learning Programs"
        description="Structured courses designed by OET experts to maximise your exam readiness."
      />

      {/* Filters */}
      <div className="flex gap-2 mb-6 flex-wrap">
        <button
          onClick={() => changeFilter('')}
          className={`px-3 py-1.5 rounded-full text-sm font-medium transition-colors ${!typeFilter ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground hover:bg-muted/80'}`}
        >
          All
        </button>
        {Object.entries(TYPE_LABELS).map(([key, label]) => (
          <button
            key={key}
            onClick={() => changeFilter(key)}
            className={`px-3 py-1.5 rounded-full text-sm font-medium transition-colors ${typeFilter === key ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground hover:bg-muted/80'}`}
          >
            {label}
          </button>
        ))}
      </div>

      {error && <InlineAlert variant="error">{error}</InlineAlert>}

      {loading ? (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-52 rounded-xl" />
          ))}
        </div>
      ) : programs.length === 0 ? (
        <div className="text-center py-12 text-muted-foreground">
          <BookOpen className="w-12 h-12 mx-auto mb-3 opacity-40" />
          <p className="text-lg font-medium">No programs available</p>
          <p className="text-sm mt-1">Check back soon for new learning programs.</p>
        </div>
      ) : (
        <MotionSection>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {programs.map((program) => (
              <MotionItem key={program.id}>
                <Link
                  href={`/lessons/programs/${program.id}`}
                  className="group block rounded-xl border bg-card p-5 hover:shadow-md transition-shadow"
                >
                  <div className="flex items-start justify-between mb-3">
                    <Badge variant="muted" className="text-xs">
                      {TYPE_LABELS[program.programType] ?? program.programType}
                    </Badge>
                    {program.isAccessible ? (
                      <Unlock className="w-4 h-4 text-success" />
                    ) : (
                      <Lock className="w-4 h-4 text-warning" />
                    )}
                  </div>

                  <h3 className="text-base font-semibold leading-tight mb-1 group-hover:text-primary transition-colors">
                    {program.title}
                  </h3>

                  {program.description && (
                    <p className="text-sm text-muted-foreground line-clamp-2 mb-3">
                      {program.description}
                    </p>
                  )}

                  <div className="flex items-center gap-3 text-xs text-muted-foreground mt-auto">
                    <span className="flex items-center gap-1">
                      <BookOpen className="w-3.5 h-3.5" /> {program.trackCount} track{program.trackCount !== 1 ? 's' : ''}
                    </span>
                    <span className="flex items-center gap-1">
                      <Clock className="w-3.5 h-3.5" /> {Math.round(program.estimatedDurationMinutes / 60)}h
                    </span>
                    <ChevronRight className="w-3.5 h-3.5 ml-auto opacity-0 group-hover:opacity-100 transition-opacity" />
                  </div>
                </Link>
              </MotionItem>
            ))}
          </div>
        </MotionSection>
      )}
    </LearnerDashboardShell>
  );
}
