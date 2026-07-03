'use client';

import { useEffect } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { buildVideoStepHref } from '@/components/domain/video-library/video-wizard-config';

/** A bare video URL lands on the first wizard step. */
export default function VideoIndexPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const videoId = params?.id ?? '';

  useEffect(() => {
    if (videoId) router.replace(buildVideoStepHref(videoId, 'details'));
  }, [videoId, router]);

  return null;
}
