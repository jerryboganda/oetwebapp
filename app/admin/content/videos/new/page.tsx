'use client';

/**
 * Entry point for authoring a new library video. Creates a blank Draft (so
 * every wizard step is a partial PATCH against a real id — mirroring the
 * Speaking card wizard) and routes into the wizard. The create is an explicit
 * button click to avoid orphaned/duplicate drafts on refresh.
 */

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowLeft, ArrowRight, Clapperboard, Loader2 } from 'lucide-react';
import { AdminRouteHero, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { adminCreateVideo, adminPatchVideo } from '@/lib/api/video-library';
import { COURSE_PROFESSIONS, COURSE_SUBTESTS, expectedVideoTargets, type CourseProfessionId, type CourseSubtest } from '@/lib/course-content-matrix';
import { buildVideoStepHref, VIDEO_DRAFT_SEED_TITLE } from '@/components/domain/video-library/video-wizard-config';

export default function NewVideoPage() {
  const router = useRouter();
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleStart() {
    setCreating(true);
    setError(null);
    try {
      const created = await adminCreateVideo({ title: VIDEO_DRAFT_SEED_TITLE });
      const query = new URLSearchParams(window.location.search);
      const profession = query.get('profession') as CourseProfessionId | null;
      const language = query.get('language');
      const subtest = query.get('subtest') as CourseSubtest | null;
      if (profession && language && subtest
        && COURSE_PROFESSIONS.some((p) => p.id === profession)
        && (language === 'en' || language === 'ar')
        && COURSE_SUBTESTS.includes(subtest)) {
        const targets = expectedVideoTargets(language, subtest, profession);
        if (targets) {
          await adminPatchVideo(created.videoId, { language, subtestCode: subtest, targetProfessionIds: targets });
        }
      }
      router.replace(buildVideoStepHref(created.videoId, 'details'));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create a draft video.');
      setCreating(false);
    }
  }

  return (
    <AdminRouteWorkspace>
      <AdminRouteHero
        eyebrow="Content"
        title="New video"
        description="Author a library video in a guided, step-by-step flow: details, direct-to-Bunny upload, thumbnail/captions/chapters/attachments, access & scheduling, then review and publish."
        icon={Clapperboard}
        accent="primary"
      />
      <AdminRoutePanel>
        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        <div className="flex flex-wrap items-center gap-3 py-2">
          <Button variant="primary" onClick={() => void handleStart()} disabled={creating}>
            {creating ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : null}
            Create draft &amp; open wizard <ArrowRight className="ml-1 h-4 w-4" />
          </Button>
          <Button variant="ghost" onClick={() => router.push('/admin/content/videos')} disabled={creating}>
            <ArrowLeft className="mr-1 h-4 w-4" /> Back to Video Library
          </Button>
        </div>
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
