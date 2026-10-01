'use client';

import { useEffect, useState, useCallback } from 'react';
import { Users, ArrowRight } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { MotionItem } from '@/components/ui/motion-primitives';
import { CardLink } from '@/components/ui/card-link';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

interface StudyGroup {
  id: string;
  name: string;
  profession: string;
  memberCount: number;
  description: string;
  isJoined: boolean;
}

export default function GroupsPage() {
  const [groups, setGroups] = useState<StudyGroup[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await apiClient.request<StudyGroup[]>('/v1/community/study-groups');
      setGroups(Array.isArray(data) ? data : []);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load study groups');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { analytics.track('page_viewed', { page: 'community-groups' }); load(); }, [load]);

  return (
    <>
      <LearnerPageHero
        eyebrow="Community"
        title="Study Groups"
        description="Connect with peers preparing for the same exam. Share resources, ask questions, and stay motivated together."
        icon={Users}
        highlights={!loading && !error ? [{ icon: Users, label: 'Groups', value: `${groups.length} available` }] : undefined}
      />

      {loading ? (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2" aria-hidden="true">
          {[...Array(4)].map((_, i) => <Skeleton key={i} className="h-32 rounded-2xl" />)}
        </div>
      ) : error ? (
        <ErrorState title="Could not load groups" message={error} onRetry={() => void load()} />
      ) : groups.length === 0 ? (
        <EmptyState
          icon={<Users className="h-8 w-8" />}
          title="No study groups yet"
          description="Be the first to create a study group for your profession."
        />
      ) : (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {groups.map((group, index) => (
            <MotionItem key={group.id} delayIndex={Math.min(index, 5)}>
              <CardLink href={`/community/groups/${group.id}`} prefetch={false} className="h-full">
                <div className="mb-2 flex items-start justify-between gap-3">
                  <h2 className="min-w-0 break-words text-sm font-bold text-navy">{group.name}</h2>
                  {group.isJoined ? (
                    <Badge variant="success">Joined</Badge>
                  ) : (
                    <Badge variant="outline">Open</Badge>
                  )}
                </div>
                <p className="mb-3 line-clamp-2 text-xs text-muted">{group.description}</p>
                <div className="flex items-center justify-between gap-3">
                  <div className="flex flex-wrap items-center gap-2 text-xs text-muted">
                    <Users className="h-3.5 w-3.5" aria-hidden="true" />
                    <span className="tabular-nums">{group.memberCount} members</span>
                    <Badge variant="muted" className="text-3xs capitalize">{group.profession}</Badge>
                  </div>
                  <ArrowRight className="h-4 w-4 shrink-0 text-muted rtl:rotate-180" aria-hidden="true" />
                </div>
              </CardLink>
            </MotionItem>
          ))}
        </div>
      )}
    </>
  );
}
