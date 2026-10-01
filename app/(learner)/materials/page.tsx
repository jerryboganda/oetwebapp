'use client';

import { useEffect, useMemo, useState } from 'react';
import { FolderOpen, FileText, HardDrive } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection } from '@/components/ui/motion-primitives';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { MaterialsBrowser } from '@/components/domain/materials/materials-browser';
import { fetchMaterialsTree, type LearnerMaterialFolderDto } from '@/lib/materials-api';
import { folderStats, formatBytes } from '@/lib/materials-tree';
import { analytics } from '@/lib/analytics';

export default function MaterialsPage() {
  const [folders, setFolders] = useState<LearnerMaterialFolderDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('materials_page_viewed');
    fetchMaterialsTree()
      .then((data) => setFolders(data.folders ?? []))
      .catch((e: Error) => setError(e.message ?? 'Failed to load materials.'))
      .finally(() => setLoading(false));
  }, []);

  const highlights = useMemo(() => {
    const totals = folders.reduce(
      (acc, f) => {
        const s = folderStats(f);
        return { files: acc.files + s.files, bytes: acc.bytes + s.bytes };
      },
      { files: 0, bytes: 0 },
    );
    return [
      { icon: FolderOpen, label: 'Sections', value: String(folders.length) },
      { icon: FileText, label: 'Files', value: String(totals.files) },
      // Library size only once there is something to measure (no "—" placeholder chip).
      ...(totals.bytes > 0 ? [{ icon: HardDrive, label: 'Library', value: formatBytes(totals.bytes) }] : []),
    ];
  }, [folders]);

  return (
    <>
      <LearnerPageHero
        title="Course Materials"
        description="Every study resource shared by your tutors — search the whole library, or browse by section."
        icon={FolderOpen}
        highlights={loading ? [] : highlights}
      />

      <MotionSection className="space-y-5">
        <LearnerSurfaceSectionHeader
          title="Your Course Materials"
          description="Listening and Reading are shared across professions. Writing and Speaking are specific to yours."
        />

        {loading && (
          <div className="space-y-3">
            <Skeleton className="h-24 rounded-2xl" />
            {[1, 2, 3, 4].map((i) => (
              <Skeleton key={i} className="h-16 rounded-xl" />
            ))}
          </div>
        )}

        {!loading && error && <InlineAlert variant="error">{error}</InlineAlert>}

        {!loading && !error && folders.length === 0 && (
          <LearnerEmptyState
            icon={FolderOpen}
            title="No materials yet"
            description="Your tutor will share study files here when they're available."
            primaryAction={{ label: 'Browse Video Library', href: '/videos' }}
          />
        )}

        {!loading && !error && folders.length > 0 && <MaterialsBrowser folders={folders} />}
      </MotionSection>
    </>
  );
}
