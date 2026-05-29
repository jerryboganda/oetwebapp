'use client';

import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import { CalendarPlus, Video } from 'lucide-react';

import { TutorRouteHero, TutorRouteWorkspace } from '@/components/domain/tutor-route-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button, buttonClassName } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Select } from '@/components/ui/form-controls';
import { fetchTutorClasses, type LiveClassListItem } from '@/lib/api';

const STATUS_OPTIONS = [
  { value: 'all', label: 'All statuses' },
  { value: 'Draft', label: 'Draft' },
  { value: 'Published', label: 'Published' },
  { value: 'Cancelled', label: 'Cancelled' },
  { value: 'Archived', label: 'Archived' },
];

function nextSessionDate(item: LiveClassListItem): string | null {
  const next = item.sessions
    .filter((s) => s.status !== 'Cancelled')
    .sort((a, b) => new Date(a.scheduledStartAt).getTime() - new Date(b.scheduledStartAt).getTime())[0];
  return next?.scheduledStartAt ?? null;
}

export default function TutorClassesPage() {
  const [classes, setClasses] = useState<LiveClassListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState('all');

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    fetchTutorClasses()
      .then((data) => {
        if (!cancelled) setClasses(data);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load classes.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const filtered = useMemo(() => {
    if (statusFilter === 'all') return classes;
    return classes.filter((c) => c.status === statusFilter);
  }, [classes, statusFilter]);

  return (
    <TutorRouteWorkspace>
      <TutorRouteHero
        title="My Classes"
        description="Every class you own. Click a row to manage sessions, view attendance, or edit details."
        icon={Video}
      />

      {error ? (
        <InlineAlert variant="warning" className="flex items-center justify-between gap-3">
          <span>{error}</span>
          <Button type="button" variant="ghost" size="sm" onClick={() => setError(null)}>
            Dismiss
          </Button>
        </InlineAlert>
      ) : null}

      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div className="max-w-xs">
          <Select
            label="Filter by status"
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            options={STATUS_OPTIONS}
          />
        </div>
        <Link href="/tutor/classes/new" className={buttonClassName({ variant: 'primary', size: 'sm' })}>
          <CalendarPlus className="h-4 w-4" /> New class
        </Link>
      </div>

      {loading ? (
        <div className="space-y-3">
          <Skeleton className="h-20 rounded-2xl" />
          <Skeleton className="h-20 rounded-2xl" />
          <Skeleton className="h-20 rounded-2xl" />
        </div>
      ) : filtered.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-border bg-surface p-10 text-center">
          <Video className="mx-auto mb-3 h-8 w-8 text-muted/50" />
          <p className="text-sm font-medium text-navy">
            {statusFilter === 'all' ? 'You haven’t scheduled any classes yet.' : `No classes with status "${statusFilter}".`}
          </p>
          {statusFilter === 'all' ? (
            <Link
              href="/tutor/classes/new"
              className={buttonClassName({ variant: 'primary', size: 'sm' }) + ' mt-4 inline-flex'}
            >
              <CalendarPlus className="h-4 w-4" /> Schedule your first class
            </Link>
          ) : null}
        </div>
      ) : (
        <div className="overflow-hidden rounded-2xl border border-border bg-surface shadow-sm">
          <table className="min-w-full divide-y divide-border text-sm">
            <thead className="bg-background-light text-left">
              <tr>
                <th scope="col" className="px-4 py-3 font-semibold text-navy">Title</th>
                <th scope="col" className="px-4 py-3 font-semibold text-navy">Type</th>
                <th scope="col" className="px-4 py-3 font-semibold text-navy">Status</th>
                <th scope="col" className="px-4 py-3 font-semibold text-navy">Next session</th>
                <th scope="col" className="px-4 py-3 font-semibold text-navy">Sessions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {filtered.map((item) => {
                const next = nextSessionDate(item);
                return (
                  <tr key={item.id} className="hover:bg-background-light">
                    <td className="px-4 py-3">
                      <Link href={`/tutor/classes/${item.id}`} className="font-medium text-navy hover:text-primary">
                        {item.title}
                      </Link>
                      {item.titleAr ? (
                        <span lang="ar" dir="rtl" className="ml-2 text-xs text-muted">
                          {item.titleAr}
                        </span>
                      ) : null}
                    </td>
                    <td className="px-4 py-3 text-muted">{item.type}</td>
                    <td className="px-4 py-3">
                      <Badge
                        variant={
                          item.status === 'Published' ? 'success'
                          : item.status === 'Draft' ? 'info'
                          : item.status === 'Cancelled' ? 'danger'
                          : 'muted'
                        }
                      >
                        {item.status}
                      </Badge>
                    </td>
                    <td className="px-4 py-3 text-muted">
                      {next
                        ? new Intl.DateTimeFormat('en-AU', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }).format(new Date(next))
                        : 'N/A'}
                    </td>
                    <td className="px-4 py-3 text-muted">{item.sessions.length}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </TutorRouteWorkspace>
  );
}
