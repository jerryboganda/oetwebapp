'use client';

import { useEffect, useState, useCallback } from 'react';
import { Users, Plus, MessageSquare, UserPlus, ArrowRight } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';
import Link from 'next/link';

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

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Study Groups">
        <LearnerPageHero eyebrow="Community" title="Study Groups" description="Connect with peers preparing for the same exam." icon={Users} />
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {[...Array(4)].map((_, i) => <Skeleton key={i} className="h-32 rounded-lg" />)}
        </div>
      </LearnerDashboardShell>
    );
  }

  if (error) {
    return (
      <LearnerDashboardShell pageTitle="Study Groups">
        <LearnerPageHero eyebrow="Community" title="Study Groups" description="Connect with peers preparing for the same exam." icon={Users} />
        <EmptyState title="Could not load groups" description={error} />
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle="Study Groups">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Community"
          title="Study Groups"
          description="Connect with peers preparing for the same exam. Share resources, ask questions, and stay motivated together."
          icon={Users}
          highlights={[
            { icon: Users, label: 'Groups', value: `${groups.length} available` },
            { icon: MessageSquare, label: 'Format', value: 'Thread-based discussion' },
            { icon: UserPlus, label: 'Access', value: 'Free for all learners' },
          ]}
        />

        <div className="flex justify-end">
          <Button size="sm">
            <Plus className="w-4 h-4 mr-1" />Create Group
          </Button>
        </div>

        {groups.length === 0 ? (
          <EmptyState
            title="No study groups yet"
            description="Be the first to create a study group for your profession."
          />
        ) : (
          <MotionSection className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {groups.map((group) => (
              <MotionItem key={group.id}>
                <Link href={`/community/groups/${group.id}`}>
                  <Card className="p-5 shadow-sm hover:shadow-clinical transition-shadow duration-200">
                    <div className="flex items-start justify-between mb-2">
                      <h3 className="font-semibold text-sm">{group.name}</h3>
                      {group.isJoined ? (
                        <Badge variant="success">Joined</Badge>
                      ) : (
                        <Badge variant="outline">Open</Badge>
                      )}
                    </div>
                    <p className="text-xs text-muted-foreground mb-3 line-clamp-2">{group.description}</p>
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-2 text-xs text-muted-foreground">
                        <Users className="w-3.5 h-3.5" />
                        <span>{group.memberCount} members</span>
                        <Badge variant="muted" className="text-[10px] capitalize">{group.profession}</Badge>
                      </div>
                      <ArrowRight className="w-4 h-4 text-muted-foreground" />
                    </div>
                  </Card>
                </Link>
              </MotionItem>
            ))}
          </MotionSection>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
