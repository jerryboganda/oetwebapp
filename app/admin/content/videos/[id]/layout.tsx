'use client';

/**
 * Video wizard shell. Loads the video once (client-side, using the
 * browser-auth API client), resolves write/publish permissions, and mounts the
 * generic <AdminWizard> so the per-step pages render inside it.
 * Mirrors app/admin/speaking/cards/[id]/layout.tsx.
 */

import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { useParams } from 'next/navigation';
import { Loader2 } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AdminWizard } from '@/components/domain/wizard/AdminWizard';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { buildVideoStepHref, VIDEO_WIZARD_STEPS } from '@/components/domain/video-library/video-wizard-config';
import { EncodeStatusBadge } from '@/components/domain/video-library/EncodeStatusBadge';
import { adminGetVideo, type AdminVideoDetail } from '@/lib/api/video-library';

export default function VideoWizardLayout({ children }: { children: ReactNode }) {
  const params = useParams<{ id: string }>();
  const videoId = params?.id ?? '';
  const { user } = useCurrentUser();
  const perms = user?.adminPermissions;

  const [video, setVideo] = useState<AdminVideoDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setLoading(true);
    adminGetVideo(videoId)
      .then((v) => {
        if (active) setVideo(v);
      })
      .catch((e) => {
        if (active) setError(e instanceof Error ? e.message : 'Video not found.');
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [videoId]);

  const refresh = useCallback(() => adminGetVideo(videoId), [videoId]);

  if (loading) {
    return (
      <AdminRouteWorkspace>
        <AdminRoutePanel>
          <p className="inline-flex items-center gap-2 text-sm text-admin-text-muted">
            <Loader2 className="h-4 w-4 animate-spin" /> Loading video…
          </p>
        </AdminRoutePanel>
      </AdminRouteWorkspace>
    );
  }

  if (error || !video) {
    return (
      <AdminRouteWorkspace>
        <AdminRoutePanel>
          <p className="text-sm text-admin-text">{error ?? 'Video not found.'}</p>
        </AdminRoutePanel>
      </AdminRouteWorkspace>
    );
  }

  const canWrite = hasPermission(perms, AdminPermission.ContentWrite);
  const canPublish = hasPermission(perms, AdminPermission.ContentPublish);

  return (
    <AdminRouteWorkspace>
      <AdminRoutePanel>
        <AdminWizard<AdminVideoDetail>
          entity={video}
          steps={VIDEO_WIZARD_STEPS}
          buildStepHref={(stepId) => buildVideoStepHref(videoId, stepId)}
          refresh={refresh}
          canWrite={canWrite}
          canPublish={canPublish}
          header={
            <div className="flex items-center gap-2">
              <span className="font-bold text-navy">{video.title}</span>
              <Badge variant={video.status === 'Published' ? 'success' : 'muted'}>{video.status}</Badge>
              <EncodeStatusBadge status={video.encodeStatus} />
            </div>
          }
        >
          {children}
        </AdminWizard>
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
