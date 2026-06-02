'use client';

import { useEffect, useState, useCallback } from 'react';
import { FolderOpen, FileText, Music, Download, ChevronDown, ChevronRight, BookOpen, Headphones, FilePenLine, Mic } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchMaterialsTree, type LearnerMaterialFolderDto, type LearnerMaterialFileDto } from '@/lib/materials-api';
import { fetchAuthorizedObjectUrl } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { cn } from '@/lib/utils';

const SUBTEST_META: Record<string, { label: string; icon: React.ReactNode; color: string }> = {
  listening: { label: 'Listening', icon: <Headphones className="w-3.5 h-3.5" />, color: 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300' },
  reading: { label: 'Reading', icon: <BookOpen className="w-3.5 h-3.5" />, color: 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300' },
  writing: { label: 'Writing', icon: <FilePenLine className="w-3.5 h-3.5" />, color: 'bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300' },
  speaking: { label: 'Speaking', icon: <Mic className="w-3.5 h-3.5" />, color: 'bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300' },
};

function SubtestBadge({ subtest }: { subtest: string }) {
  const meta = SUBTEST_META[subtest.toLowerCase()] ?? {
    label: subtest,
    icon: <FileText className="w-3.5 h-3.5" />,
    color: 'bg-muted/30 text-muted',
  };
  return (
    <span className={cn('inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-semibold', meta.color)}>
      {meta.icon}
      {meta.label}
    </span>
  );
}

function formatBytes(bytes?: number | null): string {
  if (bytes == null || bytes === 0) return '';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function MaterialFileRow({ file }: { file: LearnerMaterialFileDto }) {
  const [downloading, setDownloading] = useState(false);

  const handleDownload = useCallback(async () => {
    if (downloading) return;
    setDownloading(true);
    try {
      const url = await fetchAuthorizedObjectUrl(file.downloadUrl);
      const a = document.createElement('a');
      a.href = url;
      a.download = file.originalFilename ?? file.title;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      analytics.track('material_file_downloaded', { fileId: file.id, kind: file.kind, subtest: file.subtestCode });
    } catch {
      // user sees nothing; silent failure
    } finally {
      setDownloading(false);
    }
  }, [downloading, file]);

  return (
    <div className="flex items-center gap-3 rounded-xl border border-border/50 bg-surface/70 px-4 py-3 hover:border-primary/30 hover:bg-primary/3 transition-colors">
      <span className="shrink-0 text-muted">
        {file.kind === 'audio'
          ? <Music className="w-5 h-5 text-blue-500" />
          : <FileText className="w-5 h-5 text-red-400" />}
      </span>

      <div className="flex-1 min-w-0">
        <p className="text-sm font-semibold text-navy truncate">{file.title}</p>
        {file.description && (
          <p className="text-xs text-muted mt-0.5 truncate">{file.description}</p>
        )}
        <div className="mt-1.5 flex items-center gap-2 flex-wrap">
          <SubtestBadge subtest={file.subtestCode} />
          {file.sizeBytes && (
            <span className="text-[10px] text-muted">{formatBytes(file.sizeBytes)}</span>
          )}
        </div>
      </div>

      <button
        type="button"
        onClick={handleDownload}
        disabled={downloading}
        className="pressable shrink-0 flex items-center gap-1.5 rounded-lg bg-primary/10 px-3 py-1.5 text-xs font-semibold text-primary-dark hover:bg-primary/20 transition-colors disabled:opacity-50"
        aria-label={`Download ${file.title}`}
      >
        <Download className="w-3.5 h-3.5" />
        {downloading ? 'Downloading…' : 'Download'}
      </button>
    </div>
  );
}

function FolderNode({ folder, depth = 0 }: { folder: LearnerMaterialFolderDto; depth?: number }) {
  const [open, setOpen] = useState(depth === 0);

  const hasContent = folder.files.length > 0 || folder.folders.length > 0;
  if (!hasContent) return null;

  return (
    <div className={cn('rounded-2xl border border-border/60 overflow-hidden', depth === 0 ? 'bg-surface/70' : 'bg-surface/40')}>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="w-full flex items-center gap-3 px-4 py-3.5 text-left hover:bg-primary/5 transition-colors"
        aria-expanded={open}
      >
        <FolderOpen className={cn('w-5 h-5 shrink-0 transition-colors', open ? 'text-primary' : 'text-muted')} />
        <span className="flex-1 text-sm font-semibold text-navy">{folder.name}</span>
        {folder.description && (
          <span className="hidden sm:block text-xs text-muted truncate max-w-xs mr-2">{folder.description}</span>
        )}
        {open
          ? <ChevronDown className="w-4 h-4 text-muted shrink-0" />
          : <ChevronRight className="w-4 h-4 text-muted shrink-0" />}
      </button>

      {open && (
        <div className="px-4 pb-4 space-y-3 border-t border-border/40 pt-3">
          {folder.folders.length > 0 && (
            <div className="space-y-2">
              {folder.folders.map((sub) => (
                <FolderNode key={sub.id} folder={sub} depth={depth + 1} />
              ))}
            </div>
          )}
          {folder.files.length > 0 && (
            <div className="space-y-2">
              {folder.files.map((file) => (
                <MaterialFileRow key={file.id} file={file} />
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

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

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Materials"
        description="Downloadable study resources and practice files shared by your tutors."
        highlights={[
          { icon: FolderOpen, label: 'Folders', value: String(folders.length) },
        ]}
      />

      <div className="px-4 sm:px-6 lg:px-8 pb-24 space-y-6 max-w-3xl mx-auto mt-6">
        <LearnerSurfaceSectionHeader title="Your Materials" />

        {loading && (
          <div className="space-y-3">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-14 rounded-2xl" />)}
          </div>
        )}

        {!loading && error && (
          <InlineAlert variant="error">{error}</InlineAlert>
        )}

        {!loading && !error && folders.length === 0 && (
          <div className="rounded-2xl border border-border/60 bg-surface/70 p-8 text-center">
            <FolderOpen className="mx-auto mb-3 h-10 w-10 text-muted/50" />
            <p className="text-sm font-semibold text-navy">No materials yet</p>
            <p className="text-xs text-muted mt-1">Your tutor will share study files here when they&apos;re available.</p>
          </div>
        )}

        {!loading && !error && folders.length > 0 && (
          <div className="space-y-3">
            {folders.map((folder) => (
              <FolderNode key={folder.id} folder={folder} depth={0} />
            ))}
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
