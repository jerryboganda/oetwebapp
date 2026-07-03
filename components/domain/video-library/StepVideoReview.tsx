'use client';

/**
 * Video wizard — step 5: review & publish.
 * Read-only recap, the client readiness checklist (hard/soft rules), the
 * server publish gate, and the lifecycle actions
 * (Publish / Schedule / Unpublish / Archive / Restore). Publish 422s surface
 * the gate's fieldErrors — "not ready to publish" alone gives the admin
 * nothing to act on (same treatment as the mocks page).
 */

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { AlertTriangle, Archive, CalendarClock, Check, ExternalLink, RotateCcw, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Input } from '@/components/ui/form-controls';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { ApiError } from '@/lib/api';
import {
  adminArchiveVideo,
  adminGetVideoPublishGate,
  adminPublishVideo,
  adminRestoreVideo,
  adminUnpublishVideo,
  type AdminVideoDetail,
  type VideoPublishGate,
} from '@/lib/api/video-library';
import { getVideoReadiness } from './use-video-readiness';
import { EncodeStatusBadge } from './EncodeStatusBadge';

function describeError(err: unknown, fallback: string): string {
  // Surface the gate's field errors — same pattern as the mocks list page.
  const details =
    err instanceof ApiError && err.fieldErrors.length > 0
      ? ` ${err.fieldErrors.map((f) => f.message).join(' ')}`
      : '';
  const message = err instanceof Error ? err.message : fallback;
  return `${message}${details}`;
}

function formatDuration(totalSeconds: number | null): string {
  if (totalSeconds == null) return '—';
  const s = Math.max(0, Math.round(totalSeconds));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}

export function StepVideoReview() {
  const wizard = useAdminWizard<AdminVideoDetail>();
  const router = useRouter();
  const video = wizard.entity;
  const readiness = getVideoReadiness(video);

  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [gate, setGate] = useState<VideoPublishGate | null>(null);
  const [scheduleAt, setScheduleAt] = useState('');

  const status = video.status;
  const isPublished = status === 'Published';
  const isArchived = status === 'Archived';

  useEffect(() => {
    let active = true;
    if (isArchived) return undefined;
    adminGetVideoPublishGate(video.videoId)
      .then((g) => {
        if (active) setGate(g);
      })
      .catch(() => {
        if (active) setGate(null);
      });
    return () => {
      active = false;
    };
  }, [video.videoId, video.updatedAt, isArchived]);

  async function runAction(key: string, action: () => Promise<unknown>, fallback: string) {
    setBusy(key);
    setError(null);
    try {
      await action();
      await wizard.refresh();
    } catch (e) {
      setError(describeError(e, fallback));
    } finally {
      setBusy(null);
    }
  }

  function handleSchedule() {
    if (!scheduleAt) {
      setError('Pick a date and time to schedule the publish.');
      return;
    }
    const when = new Date(scheduleAt);
    if (Number.isNaN(when.getTime()) || when.getTime() <= Date.now()) {
      setError('The scheduled publish time must be in the future.');
      return;
    }
    void runAction(
      'schedule',
      () => adminPublishVideo(video.videoId, when.toISOString()),
      'Could not schedule the publish.',
    );
  }

  const summary: Array<{ label: string; value: string }> = [
    { label: 'Title', value: video.title || '—' },
    { label: 'Categories', value: (video.categoryNames ?? []).join(', ') || '—' },
    { label: 'Subtest', value: video.subtestCode || '—' },
    { label: 'Difficulty', value: video.difficulty || '—' },
    { label: 'Access', value: video.accessTier === 'premium' ? 'Premium' : 'Free' },
    {
      label: 'Professions',
      value: (video.targetProfessionIds ?? []).length === 0 ? 'All' : `${video.targetProfessionIds.length} selected`,
    },
    { label: 'Duration', value: formatDuration(video.durationSeconds) },
    { label: 'Captions', value: String((video.captions ?? []).length) },
    { label: 'Chapters', value: String((video.chapters ?? []).length) },
    { label: 'Attachments', value: String((video.attachments ?? []).length) },
    { label: 'Featured', value: video.isFeatured ? 'Yes' : 'No' },
    {
      label: 'Scheduled publish',
      value: video.publishAt ? new Date(video.publishAt).toLocaleString() : '—',
    },
  ];

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div className="space-y-1">
          <h2 className="text-lg font-bold text-navy">Review &amp; publish</h2>
          <p className="text-sm text-muted">Confirm the video is complete, then publish it for learners.</p>
        </div>
        <div className="flex items-center gap-2">
          <EncodeStatusBadge status={video.encodeStatus} />
          <Badge variant={isPublished ? 'success' : 'muted'}>{video.status}</Badge>
        </div>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <div className="grid gap-2 rounded-2xl border border-border bg-background-light p-4 sm:grid-cols-2 lg:grid-cols-3">
        {summary.map((s) => (
          <div key={s.label}>
            <p className="text-[10px] font-bold uppercase tracking-widest text-muted">{s.label}</p>
            <p className="truncate text-sm text-navy">{s.value}</p>
          </div>
        ))}
      </div>

      <div className="space-y-2 rounded-2xl border border-border bg-surface p-4">
        <p className="text-sm font-bold text-navy">Publish readiness</p>
        <ul className="space-y-1.5">
          {readiness.items.map((item) => (
            <li key={item.label} className="flex items-center gap-2 text-sm">
              {item.ok ? (
                <Check className="h-4 w-4 text-emerald-600" />
              ) : item.hard ? (
                <X className="h-4 w-4 text-red-600" />
              ) : (
                <AlertTriangle className="h-4 w-4 text-amber-600" />
              )}
              <span className={item.ok ? 'text-navy' : item.hard ? 'text-red-700' : 'text-amber-700'}>
                {item.label}
                {!item.ok && !item.hard ? ' (recommended)' : ''}
              </span>
            </li>
          ))}
        </ul>

        {gate && (gate.errors.length > 0 || gate.warnings.length > 0) ? (
          <div className="space-y-1.5 border-t border-border pt-2">
            <p className="text-xs font-bold uppercase tracking-widest text-muted">Server publish gate</p>
            {gate.errors.map((message) => (
              <p key={message} className="flex items-center gap-2 text-sm text-red-700">
                <X className="h-4 w-4 shrink-0" /> {message}
              </p>
            ))}
            {gate.warnings.map((message) => (
              <p key={message} className="flex items-center gap-2 text-sm text-amber-700">
                <AlertTriangle className="h-4 w-4 shrink-0" /> {message}
              </p>
            ))}
          </div>
        ) : null}
      </div>

      {isArchived ? (
        <div className="flex flex-wrap items-center gap-3 border-t border-border pt-4">
          <InlineAlert variant="warning">This video is archived and hidden from learners.</InlineAlert>
          <Button
            variant="primary"
            onClick={() => void runAction('restore', () => adminRestoreVideo(video.videoId), 'Could not restore this video.')}
            loading={busy === 'restore'}
            disabled={busy !== null || !wizard.canWrite}
          >
            <RotateCcw className="mr-1 h-4 w-4" /> Restore
          </Button>
        </div>
      ) : isPublished ? (
        <div className="space-y-3 border-t border-border pt-4">
          <InlineAlert variant="success">This video is published and visible to learners.</InlineAlert>
          <div className="flex flex-wrap items-center gap-3">
            <Button
              variant="outline"
              onClick={() => void runAction('unpublish', () => adminUnpublishVideo(video.videoId), 'Could not unpublish this video.')}
              loading={busy === 'unpublish'}
              disabled={busy !== null || !wizard.canPublish}
            >
              Unpublish
            </Button>
            <Button
              variant="ghost"
              onClick={() => void runAction('archive', () => adminArchiveVideo(video.videoId), 'Could not archive this video.')}
              loading={busy === 'archive'}
              disabled={busy !== null || !wizard.canWrite}
            >
              <Archive className="mr-1 h-4 w-4" /> Archive
            </Button>
          </div>
        </div>
      ) : (
        <div className="space-y-3 border-t border-border pt-4">
          <div className="flex flex-wrap items-center gap-3">
            <Button
              variant="primary"
              onClick={() => void runAction('publish', () => adminPublishVideo(video.videoId), 'Could not publish this video.')}
              loading={busy === 'publish'}
              disabled={busy !== null || !wizard.canPublish || !readiness.hardReady}
              title={readiness.hardReady ? 'Publish video' : 'Complete the hard readiness checks first'}
            >
              Publish now
            </Button>
            <Button
              variant="ghost"
              onClick={() => void runAction('archive', () => adminArchiveVideo(video.videoId), 'Could not archive this video.')}
              loading={busy === 'archive'}
              disabled={busy !== null || !wizard.canWrite}
            >
              <Archive className="mr-1 h-4 w-4" /> Archive
            </Button>
            {!wizard.canPublish ? (
              <span className="text-xs text-muted">You do not have publish permission.</span>
            ) : !readiness.hardReady ? (
              <span className="text-xs text-amber-700">Resolve the hard checks above to enable publishing.</span>
            ) : null}
          </div>

          <div className="flex flex-wrap items-end gap-2">
            <div className="w-64">
              <Input
                label="Schedule publish for"
                type="datetime-local"
                value={scheduleAt}
                onChange={(e) => setScheduleAt(e.target.value)}
              />
            </div>
            <Button
              variant="outline"
              onClick={handleSchedule}
              loading={busy === 'schedule'}
              disabled={busy !== null || !wizard.canPublish || !readiness.hardReady || !scheduleAt}
            >
              <CalendarClock className="mr-1 h-4 w-4" /> Schedule
            </Button>
          </div>
        </div>
      )}

      <div>
        <Button variant="ghost" size="sm" onClick={() => router.push('/admin/content/videos')}>
          <ExternalLink className="mr-1 h-4 w-4" /> Back to Video Library
        </Button>
      </div>
    </div>
  );
}
