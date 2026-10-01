'use client';

import { useEffect, useState, useCallback } from 'react';
import { Users } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { analytics } from '@/lib/analytics';
import { fetchStudyGroups, joinStudyGroup, type StudyGroupSummary } from '@/lib/api/community';

export default function GroupsPage() {
  const [groups, setGroups] = useState<StudyGroupSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [joiningId, setJoiningId] = useState<string | null>(null);
  const [joinError, setJoinError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await fetchStudyGroups();
      setGroups(Array.isArray(data?.groups) ? data.groups : []);
      setTotal(typeof data?.total === 'number' ? data.total : 0);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load study groups');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { analytics.track('page_viewed', { page: 'community-groups' }); load(); }, [load]);

  const join = async (groupId: string) => {
    setJoiningId(groupId);
    setJoinError(null);
    try {
      await joinStudyGroup(groupId);
      await load();
    } catch (err) {
      setJoinError(err instanceof Error ? err.message : 'Could not join this group.');
    } finally {
      setJoiningId(null);
    }
  };

  return (
    <>
      <LearnerPageHero
        eyebrow="Community"
        title="Study Groups"
        description="Connect with peers preparing for the same exam. Share resources, ask questions, and stay motivated together."
        icon={Users}
        highlights={!loading && !error ? [{ icon: Users, label: 'Groups', value: `${total} available` }] : undefined}
      />

      {joinError ? (
        <InlineAlert variant="error" dismissible onDismiss={() => setJoinError(null)}>
          {joinError}
        </InlineAlert>
      ) : null}

      {loading ? (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2" role="status" aria-busy="true" aria-label="Loading">
          {[...Array(4)].map((_, i) => <Skeleton aria-hidden key={i} className="h-32 rounded-2xl" />)}
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
          {groups.map((group, index) => {
            const full = group.memberCount >= group.maxMembers;
            return (
              <MotionItem key={group.id} delayIndex={Math.min(index, 5)} className="h-full">
                <Card className="flex h-full flex-col gap-3">
                  <div className="flex items-start justify-between gap-3">
                    <h2 className="min-w-0 break-words text-sm font-bold text-navy">{group.name}</h2>
                    {group.isJoined ? (
                      <Badge variant="success">Joined</Badge>
                    ) : full ? (
                      <Badge variant="muted">Full</Badge>
                    ) : null}
                  </div>
                  {group.description ? <p className="line-clamp-2 text-xs text-muted">{group.description}</p> : null}
                  <div className="mt-auto flex flex-wrap items-center justify-between gap-3">
                    <div className="flex flex-wrap items-center gap-2 text-xs text-muted">
                      <Users className="h-3.5 w-3.5" aria-hidden="true" />
                      <span className="tabular-nums">{group.memberCount}/{group.maxMembers} members</span>
                      {group.examTypeCode ? (
                        <Badge variant="muted" className="text-3xs uppercase">{group.examTypeCode}</Badge>
                      ) : null}
                    </div>
                    {!group.isJoined && !full ? (
                      <Button size="sm" variant="outline" loading={joiningId === group.id} onClick={() => void join(group.id)}>
                        Join group
                      </Button>
                    ) : null}
                  </div>
                </Card>
              </MotionItem>
            );
          })}
        </div>
      )}
    </>
  );
}
