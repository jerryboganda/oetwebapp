'use client';

import { useQuery } from '@tanstack/react-query';
import { ArrowRight, CheckCircle2, GraduationCap } from 'lucide-react';
import { apiClient } from '@/lib/api';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { Badge } from '@/components/ui/badge';
import { CardLink } from '@/components/ui/card-link';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
import { CardSkeleton } from '@/components/ui/skeleton';
import { queryKeys } from '@/lib/query/hooks';

interface LessonItem {
  id: string;
  slug: string;
  title: string;
  skillCode: string;
  orderIndex: number;
  estimatedMinutes: number;
  isPublished: boolean;
  completedByUser: boolean;
}

const SKILL_LABEL: Record<string, string> = {
  L1: 'Detail capture',
  L2: 'Note-taking speed',
  L3: 'Spelling accuracy',
  L4: 'Gist comprehension',
  L5: 'Distractor recognition',
  L6: 'Inference',
  L7: 'Speaker stance',
  L8: 'Accent adaptation',
};

export default function ListeningLessonsPage() {
  // FE-006: TanStack Query gives loading/error/retry + caching for free, replacing
  // the hand-rolled useState+useEffect+reloadKey (FE-021's manual error handling).
  const { data: lessons = [], isPending, isError, refetch } = useQuery({
    queryKey: queryKeys.listening.lessons,
    queryFn: () => apiClient.get<LessonItem[]>('/v1/listening-pathway/lessons'),
  });

  return (
    <>
      <LearnerPageHero
        icon={GraduationCap}
        accent="purple"
        title="Foundation Lessons"
        description="8 bite-sized lessons covering each Listening sub-skill (L1–L8). Each lesson runs ~30 min: watch, read, drill × 3, then a mini-quiz."
      />

      {isPending ? (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4" aria-busy="true">
          {Array.from({ length: 8 }, (_, i) => <CardSkeleton key={i} />)}
        </div>
      ) : isError ? (
        <ErrorState
          message="We couldn't load the foundation lessons."
          onRetry={() => void refetch()}
        />
      ) : lessons.length === 0 ? (
        // The developer seed-flag hint that used to follow is not for learners.
        <EmptyState
          icon={<GraduationCap className="h-8 w-8" aria-hidden />}
          title="Foundation lessons are being seeded by the content team."
        />
      ) : (
        <ol className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {lessons.map((l, index) => (
            <li key={l.id}>
              <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                {/* The whole card opens the lesson. */}
                <CardLink href={`/listening/lessons/${l.slug}`} className="group flex h-full flex-col">
                  <span className="eyebrow text-skill-listening">
                    {l.skillCode} · {SKILL_LABEL[l.skillCode] ?? l.skillCode}
                  </span>
                  <h2 className="mt-1 text-base font-semibold text-navy">{l.title}</h2>
                  <p className="mt-1 text-xs tabular-nums text-muted">~{l.estimatedMinutes} min</p>
                  <div className="mt-auto flex items-center gap-2 pt-4">
                    {l.completedByUser ? (
                      <Badge variant="success" className="gap-1">
                        <CheckCircle2 className="h-3 w-3" aria-hidden />
                        Completed
                      </Badge>
                    ) : null}
                    <ArrowRight className="ms-auto h-4 w-4 text-primary transition-transform group-hoverable:translate-x-0.5 rtl:rotate-180 rtl:group-hoverable:-translate-x-0.5" aria-hidden />
                  </div>
                </CardLink>
              </MotionItem>
            </li>
          ))}
        </ol>
      )}
    </>
  );
}
