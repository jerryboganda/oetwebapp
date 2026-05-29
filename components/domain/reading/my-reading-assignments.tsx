'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { CalendarClock, ClipboardList } from 'lucide-react';
import { cn } from '@/lib/utils';
import {
  listMyReadingAssignments,
  type ReadingAssignmentDto,
} from '@/lib/reading-tutor-api';

/**
 * MyReadingAssignments — learner-facing list of reading work assigned by a
 * tutor / expert / admin. Self-hides when the learner has no active
 * assignments, so it is safe to drop into the reading hub.
 *
 * NOTE (mount point TODO): the reading hub (`app/reading/page.tsx`) is
 * intentionally constrained to exactly four candidate-facing entries per the
 * 2026-05-27 sample-test alignment directive, and the learner player/results
 * are owned by another lane. To avoid colliding with that directive this
 * component is NOT yet mounted — wire it into the reading hub (or a learner
 * "assigned work" surface) once the owning lane confirms placement.
 *
 * Learner surface styling follows DESIGN.md: light cream/white cards with
 * subtle coloured accents, never dark fills.
 */

const KIND_LABELS: Record<string, string> = {
  retake: 'Full retake',
  part_a: 'Part A practice',
  part_bc: 'Parts B & C practice',
  drill: 'Targeted drill',
  full: 'Full reading exam',
};

function kindLabel(kind: string): string {
  return KIND_LABELS[kind] ?? kind.replace(/_/g, ' ');
}

function formatDue(dueAt: string | null): string | null {
  if (!dueAt) return null;
  try {
    return new Date(dueAt).toLocaleDateString('en-GB', {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });
  } catch {
    return null;
  }
}

export interface MyReadingAssignmentsProps {
  className?: string;
}

export function MyReadingAssignments({ className }: MyReadingAssignmentsProps) {
  const [items, setItems] = useState<ReadingAssignmentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const data = await listMyReadingAssignments();
        if (!cancelled) setItems(data.filter((item) => item.status !== 'cancelled' && item.status !== 'completed'));
      } catch {
        if (!cancelled) setError(true);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // Self-hide while loading, on error, or when there is nothing assigned.
  if (loading || error || items.length === 0) return null;

  return (
    <section
      aria-label="Assigned reading"
      className={cn(
        'rounded-2xl border border-amber-200 bg-white p-5 shadow-sm',
        className,
      )}
    >
      <div className="mb-3 flex items-center gap-2">
        <ClipboardList className="h-5 w-5 text-amber-600" aria-hidden="true" />
        <h2 className="text-base font-semibold text-navy">Assigned reading</h2>
      </div>
      <ul className="space-y-2">
        {items.map((item) => {
          const due = formatDue(item.dueAt);
          return (
            <li
              key={item.id}
              className="flex flex-col gap-2 rounded-xl border border-border bg-background-light p-4 sm:flex-row sm:items-center sm:justify-between"
            >
              <div className="min-w-0">
                <p className="text-sm font-semibold text-navy">{kindLabel(item.kind)}</p>
                {item.note ? <p className="mt-0.5 text-sm text-muted">{item.note}</p> : null}
                {due ? (
                  <p className="mt-1 inline-flex items-center gap-1 text-xs text-amber-700">
                    <CalendarClock className="h-3.5 w-3.5" aria-hidden="true" /> Due {due}
                  </p>
                ) : null}
              </div>
              <Link
                href={`/reading/paper/${item.paperId}`}
                className="inline-flex shrink-0 items-center justify-center rounded-xl bg-primary px-4 py-2 text-sm font-medium text-white transition-[color,background-color,transform] duration-200 hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100"
              >
                Start
              </Link>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
