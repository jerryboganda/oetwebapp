'use client';

import { useEffect, useMemo, useState } from 'react';
import { FolderOpen, FileText, HardDrive } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
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
      { icon: HardDrive, label: 'Library', value: formatBytes(totals.bytes) || '—' },
    ];
  }, [folders]);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Materials"
        description="Every study resource shared by your tutors — search the whole library, or browse by section."
        icon={FolderOpen}
        highlights={loading ? [] : highlights}
      />

      <div className="mx-auto mt-6 max-w-4xl space-y-5 px-4 pb-24 sm:px-6 lg:px-8">
        <LearnerSurfaceSectionHeader
          title="Your Materials"
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
          <div className="rounded-2xl border border-border/60 bg-surface/70 p-8 text-center">
            <FolderOpen className="mx-auto mb-3 h-10 w-10 text-muted/50" />
            <p className="text-sm font-semibold text-navy">No materials yet</p>
            <p className="mt-1 text-xs text-muted">
              Your tutor will share study files here when they&apos;re available.
            </p>
          </div>
        )}

        {!loading && !error && folders.length > 0 && <MaterialsBrowser folders={folders} />}
      </div>
    </LearnerDashboardShell>
  );
}
