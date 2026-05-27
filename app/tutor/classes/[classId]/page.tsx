'use client';

import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import { useParams } from 'next/navigation';
import { ArrowLeft, CalendarPlus, Users, Video } from 'lucide-react';

import { TutorRouteHero, TutorRouteSectionHeader, TutorRouteWorkspace } from '@/components/domain/tutor-route-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { SessionEditorRow } from '@/components/tutor/SessionEditorRow';
import {
  addTutorClassSession,
  cancelTutorClassSession,
  fetchTutorClasses,
  updateTutorClassSession,
  type LiveClassListItem,
} from '@/lib/api';

function toLocalInputValue(date: Date): string {
  const copy = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return copy.toISOString().slice(0, 16);
}

const defaultNewStart = toLocalInputValue(new Date(Date.now() + 24 * 60 * 60 * 1000));

export default function TutorClassDetailPage() {
  const params = useParams();
  const classId = typeof params?.classId === 'string' ? params.classId : null;

  const [item, setItem] = useState<LiveClassListItem | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [addingSession, setAddingSession] = useState(false);
  const [newStartsAt, setNewStartsAt] = useState(defaultNewStart);
  const [newDuration, setNewDuration] = useState(60);
  const [newCapacity, setNewCapacity] = useState<number | null>(null);

  async function reload() {
    if (!classId) return;
    setLoading(true);
    try {
      const list = await fetchTutorClasses();
      const match = list.find((c) => c.id === classId) ?? null;
      setItem(match);
      if (!match) setError('Class not found.');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not load class.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void reload();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [classId]);

  const sortedSessions = useMemo(() => {
    if (!item) return [];
    return [...item.sessions].sort(
      (a, b) => new Date(a.scheduledStartAt).getTime() - new Date(b.scheduledStartAt).getTime(),
    );
  }, [item]);

  async function handleAddSession(e: React.FormEvent) {
    e.preventDefault();
    if (!classId) return;
    if (new Date(newStartsAt).getTime() <= Date.now()) {
      setError('New session start time must be in the future.');
      return;
    }
    setAddingSession(true);
    setError(null);
    try {
      await addTutorClassSession(classId, {
        scheduledStartAt: new Date(newStartsAt).toISOString(),
        durationMinutes: newDuration,
        capacity: newCapacity,
      });
      await reload();
      setNewStartsAt(defaultNewStart);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not add session.');
    } finally {
      setAddingSession(false);
    }
  }

  async function handleSessionUpdate(sessionId: string, payload: { scheduledStartAt?: string; capacity?: number }) {
    try {
      await updateTutorClassSession(sessionId, payload);
      await reload();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not update session.');
    }
  }

  async function handleSessionCancel(sessionId: string) {
    try {
      await cancelTutorClassSession(sessionId);
      await reload();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not cancel session.');
    }
  }

  if (!classId) {
    return (
      <TutorRouteWorkspace>
        <InlineAlert variant="warning">Invalid class id.</InlineAlert>
      </TutorRouteWorkspace>
    );
  }

  if (loading) {
    return (
      <TutorRouteWorkspace>
        <Skeleton className="h-24 rounded-2xl" />
        <Skeleton className="h-40 rounded-2xl" />
      </TutorRouteWorkspace>
    );
  }

  if (!item) {
    return (
      <TutorRouteWorkspace>
        <Link href="/tutor/classes" className="inline-flex items-center gap-1.5 text-sm text-muted hover:text-navy">
          <ArrowLeft className="h-4 w-4" /> Back to classes
        </Link>
        <InlineAlert variant="warning">{error ?? 'Class not found.'}</InlineAlert>
      </TutorRouteWorkspace>
    );
  }

  return (
    <TutorRouteWorkspace>
      <Link href="/tutor/classes" className="inline-flex items-center gap-1.5 text-sm text-muted hover:text-navy">
        <ArrowLeft className="h-4 w-4" /> Back to classes
      </Link>

      <TutorRouteHero
        title={item.title}
        description={item.description}
        icon={Video}
      />

      <div className="flex flex-wrap gap-2">
        <Badge variant="info">{item.type}</Badge>
        <Badge variant="default">{item.professionTrack}</Badge>
        <Badge variant="outline">{item.level}</Badge>
        <Badge variant={item.status === 'Published' ? 'success' : 'muted'}>{item.status}</Badge>
      </div>

      {item.titleAr || item.descriptionAr ? (
        <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm" dir="rtl" lang="ar">
          {item.titleAr ? <h2 className="text-base font-semibold text-navy">{item.titleAr}</h2> : null}
          {item.descriptionAr ? <p className="mt-2 text-sm leading-7 text-muted">{item.descriptionAr}</p> : null}
        </div>
      ) : null}

      {error ? (
        <InlineAlert variant="warning" className="flex items-center justify-between gap-3">
          <span>{error}</span>
          <Button type="button" variant="ghost" size="sm" onClick={() => setError(null)}>
            Dismiss
          </Button>
        </InlineAlert>
      ) : null}

      <section className="space-y-3">
        <TutorRouteSectionHeader
          eyebrow="Sessions"
          title="Schedule"
          description="Add, edit, or cancel sessions for this class."
        />

        <form onSubmit={(e) => void handleAddSession(e)} className="grid gap-3 rounded-2xl border border-border bg-surface p-4 shadow-sm sm:grid-cols-[1.5fr_1fr_1fr_auto] sm:items-end">
          <Input
            type="datetime-local"
            label="Start"
            value={newStartsAt}
            onChange={(e) => setNewStartsAt(e.target.value)}
            min={toLocalInputValue(new Date(Date.now() + 60 * 60 * 1000))}
            required
          />
          <Input
            type="number"
            label="Duration (min)"
            min={15}
            max={480}
            step={15}
            value={newDuration}
            onChange={(e) => setNewDuration(Number(e.target.value))}
          />
          <Input
            type="number"
            label="Capacity"
            min={1}
            max={500}
            placeholder="Default"
            value={newCapacity ?? ''}
            onChange={(e) => {
              const v = e.target.value;
              setNewCapacity(v === '' ? null : Number(v));
            }}
          />
          <Button type="submit" variant="primary" size="sm" loading={addingSession}>
            <CalendarPlus className="h-4 w-4" /> Add session
          </Button>
        </form>

        {sortedSessions.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-border bg-surface p-8 text-center">
            <Users className="mx-auto mb-3 h-8 w-8 text-muted/50" />
            <p className="text-sm font-medium text-navy">No sessions scheduled.</p>
            <p className="mt-1 text-sm text-muted">Add a session using the form above.</p>
          </div>
        ) : (
          <div className="overflow-hidden rounded-2xl border border-border bg-surface shadow-sm">
            <table className="min-w-full divide-y divide-border text-sm">
              <thead className="bg-background-light text-left">
                <tr>
                  <th scope="col" className="px-4 py-3 font-semibold text-navy">Start</th>
                  <th scope="col" className="px-4 py-3 font-semibold text-navy">Capacity</th>
                  <th scope="col" className="px-4 py-3 font-semibold text-navy">Enrolled</th>
                  <th scope="col" className="px-4 py-3 font-semibold text-navy">Status</th>
                  <th scope="col" className="px-4 py-3 font-semibold text-navy">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {sortedSessions.map((session) => (
                  <SessionEditorRow
                    key={session.id}
                    session={session}
                    onSave={(payload) => handleSessionUpdate(session.id, payload)}
                    onCancel={() => handleSessionCancel(session.id)}
                  />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </TutorRouteWorkspace>
  );
}
