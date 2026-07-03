'use client';

/**
 * Shared badge for a video's Bunny encode status — used by the list page,
 * the wizard header and the upload card so the status vocabulary stays
 * consistent everywhere.
 */

import { Badge, type BadgeProps } from '@/components/admin/ui/badge';
import type { VideoEncodeStatus } from '@/lib/api/video-library';

const ENCODE_STATUS_META: Record<VideoEncodeStatus, { label: string; variant: NonNullable<BadgeProps['variant']> }> = {
  not_uploaded: { label: 'No video', variant: 'muted' },
  uploading: { label: 'Uploading', variant: 'info' },
  queued: { label: 'Queued', variant: 'info' },
  processing: { label: 'Processing', variant: 'info' },
  encoding: { label: 'Encoding', variant: 'warning' },
  ready: { label: 'Ready', variant: 'success' },
  failed: { label: 'Failed', variant: 'danger' },
};

export function EncodeStatusBadge({ status, className }: { status: VideoEncodeStatus; className?: string }) {
  const meta = ENCODE_STATUS_META[status] ?? { label: status, variant: 'muted' as const };
  return (
    <Badge variant={meta.variant} className={className}>
      {meta.label}
    </Badge>
  );
}
