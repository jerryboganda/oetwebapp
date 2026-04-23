'use client';

import { useEffect, useMemo, useState } from 'react';
import { ArrowLeft, BookOpenCheck, ChevronRight, Clock, LockKeyhole, PlayCircle, Video } from 'lucide-react';
import Link from 'next/link';
import Image from 'next/image';
import { useParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchVideoLessonProgram } from '@/lib/api';
import type { VideoLessonListItem, VideoLessonProgram } from '@/lib/types/video-lessons';

const SUBTEST_LABELS: Record<string, string> = {
  writing: 'Writing',
  speaking: 'Speaking',
  reading: 'Reading',
  listening: 'Listening',
};

function formatMinutes(seconds: number) {
  if (!Number.isFinite(seconds) || seconds <= 0) return 'Self-paced';
  return `${Math.max(1, Math.round(seconds / 60))} min`;
}

function completedCount(lessons: VideoLessonListItem[]) {
  return lessons.filter((lesson) => lesson.progress?.completed).length;
}

export default function VideoLessonProgramPage() {
  const params = useParams();
  const rawProgramId = params?.programId;
  const programId = Array.isArray(rawProgramId) ? rawProgramId[0] : rawProgramId;
  const [program, setProgram] = useState<VideoLessonProgram | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!programId) return;
    let cancelled = false;

    void (async () => {
      await Promise.resolve();
      if (cancelled) return;

      setLoading(true);
      setError(null);
      setProgram(null);

      try {
        const data = await fetchVideoLessonProgram(programId);
        if (!cancelled) setProgram(data);
      } catch {
        if (!cancelled) {
          setProgram(null);
          setError('Could not load this video program.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [programId]);

  const allLessons = useMemo(() => {
    return program?.tracks.flatMap((track) => track.modules.flatMap((module) => module.lessons)) ?? [];
  }, [program]);

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-4">
          <Skeleton className="h-48 rounded-2xl" />
          <Skeleton className="h-36 rounded-2xl" />
          <Skeleton className="h-36 rounded-2xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!program) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="warning">{error ?? 'Video program not found.'}</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  const completed = completedCount(allLessons);
  const total = allLessons.length;
  const progress = total > 0 ? Math.round((completed / total) * 100) : 0;

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <Link href="/lessons/programs" className="inline-flex items-center gap-2 text-sm font-semibold text-muted hover:text-primary">
          <ArrowLeft className="h-4 w-4" />
          Programs
        </Link>

        <LearnerPageHero
          eyebrow="Video program"
          title={program.title}
          description={program.description ?? 'Structured expert-led OET video lessons.'}
          icon={Video}
          highlights={[
            { icon: BookOpenCheck, label: 'Lessons', value: `${completed}/${total} complete` },
            { icon: Clock, label: 'Progress', value: `${progress}%` },
            { icon: LockKeyhole, label: 'Access', value: program.isAccessible ? 'Included' : 'Preview or upgrade' },
          ]}
        />

        <Card className="overflow-hidden shadow-sm">
          {program.thumbnailUrl && (
            <div className="relative h-48 bg-[#243b53]">
              <Image src={program.thumbnailUrl} alt={program.title} fill unoptimized sizes="100vw" className="object-cover" />
            </div>
          )}
          <div className="p-5">
            <div className="mb-3 flex items-center justify-between gap-4">
              <h2 className="font-semibold text-navy">Program progress</h2>
              <Badge variant={program.isAccessible ? 'success' : 'warning'}>
                {program.isAccessible ? 'Accessible' : 'Package gated'}
              </Badge>
            </div>
            <div className="h-2 overflow-hidden rounded-full bg-background-light">
              <div className="h-full rounded-full bg-primary" style={{ width: `${progress}%` }} />
            </div>
          </div>
        </Card>

        {program.tracks.length === 0 ? (
          <Card className="border-dashed p-8 text-center shadow-sm">
            <p className="text-sm font-semibold text-navy">No published video lessons yet.</p>
            <p className="mt-2 text-sm text-muted">Published program modules will appear here once lessons are attached.</p>
          </Card>
        ) : (
          <div className="space-y-6">
            {program.tracks.map((track) => (
              <section key={track.id} className="space-y-3">
                <div>
                  <Badge variant="outline">{SUBTEST_LABELS[track.subtestCode ?? ''] ?? 'General'}</Badge>
                  <h2 className="mt-2 text-lg font-bold text-navy">{track.title}</h2>
                  {track.description && <p className="mt-1 text-sm text-muted">{track.description}</p>}
                </div>

                {track.modules.map((module) => (
                  <Card key={module.id} className="p-5 shadow-sm">
                    <div className="mb-4 flex flex-col gap-2 md:flex-row md:items-center md:justify-between">
                      <div>
                        <h3 className="font-semibold text-navy">{module.title}</h3>
                        {module.description && <p className="mt-1 text-sm text-muted">{module.description}</p>}
                      </div>
                      <span className="text-xs font-semibold text-muted">{module.estimatedDurationMinutes} min module</span>
                    </div>

                    <div className="divide-y divide-border">
                      {module.lessons.map((lesson) => {
                        const locked = lesson.requiresUpgrade && !lesson.isAccessible;
                        return (
                          <Link
                            key={lesson.id}
                            href={`/lessons/${lesson.id}`}
                            className="flex items-center justify-between gap-4 py-3 hover:text-primary"
                          >
                            <div className="min-w-0">
                              <div className="flex flex-wrap items-center gap-2">
                                <p className="truncate text-sm font-semibold text-navy">{lesson.title}</p>
                                {lesson.progress?.completed && <Badge variant="success">Done</Badge>}
                                {locked && <Badge variant="warning">Locked</Badge>}
                                {!locked && lesson.isPreviewEligible && <Badge variant="info">Preview</Badge>}
                              </div>
                              <p className="mt-1 text-xs text-muted">
                                {formatMinutes(lesson.durationSeconds)} / {lesson.progress?.percentComplete ?? 0}% complete
                              </p>
                            </div>
                            <div className="flex shrink-0 items-center gap-2 text-primary">
                              <PlayCircle className="h-4 w-4" />
                              <ChevronRight className="h-4 w-4" />
                            </div>
                          </Link>
                        );
                      })}
                    </div>
                  </Card>
                ))}
              </section>
            ))}
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
