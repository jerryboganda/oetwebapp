'use client';

import Link from 'next/link';
import { useMemo, useState } from 'react';
import { ArrowDownUp, ArrowRight, Inbox } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-error';

/**
 * Queue of mock review requests that are awaiting expert marking.
 *
 * The component is intentionally presentational so the consuming page is
 * free to source items from the live API or from synthetic mock data while
 * the backend endpoint is still under construction.
 */

export interface MockMarkingQueueItem {
  reviewRequestId: string;
  learnerName: string;
  subtest: string;
  submittedAt: string;
  ageHours: number;
  routeHref: string;
}

export interface MockMarkingQueueProps {
  items: MockMarkingQueueItem[];
  /** Optional SLA threshold (hours) used to color-code the age column. Defaults to 24h. */
  slaHours?: number;
  className?: string;
}

type SortKey = 'learner' | 'subtest' | 'age';
type SortDir = 'asc' | 'desc';

function ageVariant(ageHours: number, slaHours: number): 'success' | 'warning' | 'danger' {
  if (ageHours >= slaHours) return 'danger';
  if (ageHours >= slaHours * 0.75) return 'warning';
  return 'success';
}

function formatAge(ageHours: number): string {
  if (ageHours < 1) return `${Math.round(ageHours * 60)}m`;
  if (ageHours < 48) return `${ageHours.toFixed(1)}h`;
  return `${Math.round(ageHours / 24)}d`;
}

function formatSubmitted(submittedAt: string): string {
  const parsed = new Date(submittedAt);
  if (Number.isNaN(parsed.getTime())) {
    return submittedAt;
  }
  return parsed.toLocaleString();
}

export function MockMarkingQueue({ items, slaHours = 24, className }: MockMarkingQueueProps) {
  const [sortKey, setSortKey] = useState<SortKey>('age');
  const [sortDir, setSortDir] = useState<SortDir>('desc');

  const sortedItems = useMemo(() => {
    const list = [...items];
    list.sort((a, b) => {
      const dir = sortDir === 'asc' ? 1 : -1;
      if (sortKey === 'learner') return a.learnerName.localeCompare(b.learnerName) * dir;
      if (sortKey === 'subtest') return a.subtest.localeCompare(b.subtest) * dir;
      return (a.ageHours - b.ageHours) * dir;
    });
    return list;
  }, [items, sortKey, sortDir]);

  const toggleSort = (key: SortKey) => {
    if (sortKey === key) {
      setSortDir((prev) => (prev === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortKey(key);
      setSortDir(key === 'age' ? 'desc' : 'asc');
    }
  };

  if (!items || items.length === 0) {
    return (
      <EmptyState
        icon={<Inbox className="h-10 w-10 text-muted" />}
        title="No pending marking"
        description="Mock submissions awaiting your expert marking will appear here. Check back as learners finish their attempts."
        className={className}
      />
    );
  }

  const overdueCount = items.filter((item) => item.ageHours >= slaHours).length;
  const atRiskCount = items.filter((item) => item.ageHours >= slaHours * 0.75 && item.ageHours < slaHours).length;

  return (
    <Card padding="none" className={cn('overflow-hidden', className)}>
      <CardContent className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border bg-background-light px-4 py-3">
          <div className="flex flex-wrap items-center gap-2 text-xs text-muted">
            <span className="font-bold uppercase tracking-wide text-navy">Pending marking</span>
            <Badge variant="outline" aria-label={`${items.length} items in queue`}>{items.length} total</Badge>
            {overdueCount > 0 ? (
              <Badge variant="danger" aria-label={`${overdueCount} items overdue`}>{overdueCount} overdue</Badge>
            ) : null}
            {atRiskCount > 0 ? (
              <Badge variant="warning" aria-label={`${atRiskCount} items at risk`}>{atRiskCount} at risk</Badge>
            ) : null}
          </div>
          <span className="text-xs text-muted">SLA target: {slaHours}h from submission</span>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full min-w-[540px] text-sm">
            <thead className="bg-surface">
              <tr className="text-left text-xs uppercase tracking-wide text-muted">
                <th scope="col" className="px-4 py-3">
                  <button
                    type="button"
                    onClick={() => toggleSort('learner')}
                    className="inline-flex items-center gap-1 font-bold hover:text-navy"
                    aria-label="Sort by learner"
                  >
                    Learner <ArrowDownUp className="h-3 w-3" />
                  </button>
                </th>
                <th scope="col" className="px-4 py-3">
                  <button
                    type="button"
                    onClick={() => toggleSort('subtest')}
                    className="inline-flex items-center gap-1 font-bold hover:text-navy"
                    aria-label="Sort by sub-test"
                  >
                    Sub-test <ArrowDownUp className="h-3 w-3" />
                  </button>
                </th>
                <th scope="col" className="px-4 py-3">Submitted</th>
                <th scope="col" className="px-4 py-3">
                  <button
                    type="button"
                    onClick={() => toggleSort('age')}
                    className="inline-flex items-center gap-1 font-bold hover:text-navy"
                    aria-label="Sort by age"
                  >
                    Age <ArrowDownUp className="h-3 w-3" />
                  </button>
                </th>
                <th scope="col" className="px-4 py-3 text-right">Action</th>
              </tr>
            </thead>
            <tbody>
              {sortedItems.map((item) => {
                const variant = ageVariant(item.ageHours, slaHours);
                return (
                  <tr key={item.reviewRequestId} className="border-t border-border">
                    <td className="px-4 py-3 align-middle">
                      <div className="font-semibold text-navy">{item.learnerName}</div>
                      <div className="font-mono text-[11px] text-muted">{item.reviewRequestId}</div>
                    </td>
                    <td className="px-4 py-3 align-middle capitalize text-navy">{item.subtest}</td>
                    <td className="px-4 py-3 align-middle text-xs text-muted">{formatSubmitted(item.submittedAt)}</td>
                    <td className="px-4 py-3 align-middle">
                      <Badge variant={variant} aria-label={`${formatAge(item.ageHours)} since submission`}>
                        {formatAge(item.ageHours)}
                      </Badge>
                    </td>
                    <td className="px-4 py-3 text-right align-middle">
                      <Link
                        href={item.routeHref}
                        className="inline-flex items-center gap-1 text-sm font-semibold text-primary hover:underline"
                        aria-label={`Open marking workspace for ${item.learnerName}`}
                      >
                        Mark <ArrowRight className="h-3.5 w-3.5" />
                      </Link>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </CardContent>
    </Card>
  );
}

export default MockMarkingQueue;
