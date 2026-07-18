'use client';

import { useMemo, useState, useCallback } from 'react';
import {
  Folder, Search, X, ChevronRight, Home, SearchX, FolderOpen, Download, Loader2,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import { analytics } from '@/lib/analytics';
import { fetchAuthorizedBlob } from '@/lib/api';
import {
  flattenFiles, searchFiles, folderStats, resolveTrail, formatBytes, collectFolderFiles,
} from '@/lib/materials-tree';
import type { LearnerMaterialFolderDto } from '@/lib/materials-api';
import { MaterialFileRow } from './material-file-row';

/**
 * Zip every file beneath a folder (preserving sub-folder structure) and hand the
 * learner a single archive — the "download the whole folder at once" option.
 * Files are fetched sequentially so a large section doesn't open dozens of
 * parallel authorised requests.
 */
async function downloadFolderAsZip(folder: LearnerMaterialFolderDto): Promise<void> {
  const entries = collectFolderFiles(folder);
  if (entries.length === 0) return;
  const { default: JSZip } = await import('jszip');
  const zip = new JSZip();
  for (const { file, relativePath } of entries) {
    const blob = await fetchAuthorizedBlob(file.downloadUrl);
    zip.file(relativePath, blob);
  }
  const archive = await zip.generateAsync({ type: 'blob' });
  const url = URL.createObjectURL(archive);
  try {
    const safeName = folder.name.replace(/[/\\:*?"<>|]/g, '-').trim() || 'materials';
    const a = document.createElement('a');
    a.href = url;
    a.download = `${safeName}.zip`;
    document.body.appendChild(a);
    a.click();
    a.remove();
  } finally {
    URL.revokeObjectURL(url);
  }
}

const SUBTESTS = ['listening', 'reading', 'writing', 'speaking'] as const;

function FolderCard({
  folder,
  onOpen,
}: {
  folder: LearnerMaterialFolderDto;
  onOpen: (id: string) => void;
}) {
  const stats = useMemo(() => folderStats(folder), [folder]);
  const [zipping, setZipping] = useState(false);
  const [error, setError] = useState(false);

  const handleDownload = useCallback(async () => {
    if (zipping || stats.files === 0) return;
    setZipping(true);
    setError(false);
    try {
      await downloadFolderAsZip(folder);
      analytics.track('material_folder_downloaded', { folderId: folder.id, files: stats.files });
    } catch {
      setError(true);
    } finally {
      setZipping(false);
    }
  }, [folder, stats.files, zipping]);

  return (
    <div className="group flex items-center gap-3 rounded-xl border border-border/60 bg-surface/70 px-3 py-3 text-left transition-all hover:border-primary/40 hover:bg-primary/[0.03] hover:shadow-sm sm:px-4">
      <button
        type="button"
        onClick={() => onOpen(folder.id)}
        className="flex min-w-0 flex-1 items-center gap-3 text-left"
      >
        <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary transition-colors group-hover:bg-primary/20">
          <Folder className="h-5 w-5" />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm font-semibold text-navy" title={folder.name}>
            {folder.name}
          </span>
          <span className="mt-0.5 block text-[11px] text-muted">
            {stats.folders > 0 && `${stats.folders} folder${stats.folders === 1 ? '' : 's'} · `}
            {stats.files} file{stats.files === 1 ? '' : 's'}
            {stats.bytes > 0 && ` · ${formatBytes(stats.bytes)}`}
            {error && <span className="ml-1 font-semibold text-red-500">· download failed</span>}
          </span>
        </span>
      </button>
      {stats.files > 0 && (
        <button
          type="button"
          onClick={handleDownload}
          disabled={zipping}
          title={zipping ? 'Preparing zip…' : `Download all ${stats.files} files as a zip`}
          aria-label={`Download folder ${folder.name} as a zip`}
          className="pressable flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary-dark transition-colors hover:bg-primary/20 disabled:opacity-50"
        >
          {zipping ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}
        </button>
      )}
      <ChevronRight className="h-4 w-4 shrink-0 text-muted transition-transform group-hover:translate-x-0.5 group-hover:text-primary" />
    </div>
  );
}

export function MaterialsBrowser({ folders }: { folders: LearnerMaterialFolderDto[] }) {
  const [trail, setTrail] = useState<string[]>([]);
  const [query, setQuery] = useState('');
  const [subtest, setSubtest] = useState<string | null>(null);

  const index = useMemo(() => flattenFiles(folders), [folders]);

  const open = useCallback((id: string) => {
    setTrail((t) => [...t, id]);
    setQuery('');
  }, []);

  const goTo = useCallback((depth: number) => {
    setTrail((t) => t.slice(0, depth));
  }, []);

  const crumbs = useMemo(() => resolveTrail(folders, trail), [folders, trail]);
  const current = crumbs.length > 0 ? crumbs[crumbs.length - 1] : null;

  const visibleFolders = current ? current.folders ?? [] : folders;
  const visibleFiles = useMemo(() => {
    const files = current ? current.files ?? [] : [];
    return subtest ? files.filter((f) => f.subtestCode?.toLowerCase() === subtest) : files;
  }, [current, subtest]);

  const searching = query.trim().length > 0;
  const results = useMemo(() => {
    if (!searching) return [];
    const hits = searchFiles(index, query);
    return subtest ? hits.filter((h) => h.file.subtestCode?.toLowerCase() === subtest) : hits;
  }, [searching, index, query, subtest]);

  const handleSearch = useCallback((value: string) => {
    setQuery(value);
    if (value.trim().length > 2) analytics.track('materials_searched', { length: value.trim().length });
  }, []);

  return (
    <div className="space-y-4">
      {/* Search + filters */}
      <div className="space-y-3 rounded-2xl border border-border/60 bg-surface/70 p-3 sm:p-4">
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
          <input
            type="search"
            value={query}
            onChange={(e) => handleSearch(e.target.value)}
            placeholder={`Search ${index.length} files…`}
            aria-label="Search materials"
            className="w-full rounded-xl border border-border bg-background-light py-2.5 pl-9 pr-9 text-sm text-navy placeholder:text-muted focus:border-primary/50 focus:outline-none focus:ring-2 focus:ring-primary/20"
          />
          {query && (
            <button
              type="button"
              onClick={() => setQuery('')}
              aria-label="Clear search"
              className="absolute right-2.5 top-1/2 -translate-y-1/2 rounded-md p-1 text-muted transition-colors hover:bg-muted/10 hover:text-navy"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-1.5">
          <button
            type="button"
            onClick={() => setSubtest(null)}
            className={cn(
              'rounded-full px-3 py-1 text-[11px] font-semibold capitalize transition-colors',
              subtest === null
                ? 'bg-primary text-white'
                : 'bg-muted/10 text-muted hover:bg-muted/20 hover:text-navy',
            )}
          >
            All
          </button>
          {SUBTESTS.map((s) => (
            <button
              key={s}
              type="button"
              onClick={() => setSubtest((cur) => (cur === s ? null : s))}
              className={cn(
                'rounded-full px-3 py-1 text-[11px] font-semibold capitalize transition-colors',
                subtest === s
                  ? 'bg-primary text-white'
                  : 'bg-muted/10 text-muted hover:bg-muted/20 hover:text-navy',
              )}
            >
              {s}
            </button>
          ))}
        </div>
      </div>

      {/* Search results replace navigation entirely */}
      {searching ? (
        <div className="space-y-2">
          <p className="px-1 text-xs font-semibold text-muted">
            {results.length} result{results.length === 1 ? '' : 's'} for “{query.trim()}”
          </p>
          {results.length === 0 ? (
            <div className="rounded-2xl border border-border/60 bg-surface/70 p-8 text-center">
              <SearchX className="mx-auto mb-3 h-9 w-9 text-muted/50" />
              <p className="text-sm font-semibold text-navy">No files match “{query.trim()}”</p>
              <p className="mt-1 text-xs text-muted">Try a shorter search, or clear the subtest filter.</p>
            </div>
          ) : (
            results.map((hit) => (
              <MaterialFileRow key={hit.file.id} file={hit.file} path={hit.path} />
            ))
          )}
        </div>
      ) : (
        <>
          {/* Breadcrumbs */}
          <nav aria-label="Breadcrumb" className="flex flex-wrap items-center gap-0.5 px-1 text-xs">
            <button
              type="button"
              onClick={() => goTo(0)}
              className={cn(
                'flex items-center gap-1 rounded-md px-1.5 py-1 font-semibold transition-colors',
                crumbs.length === 0 ? 'text-navy' : 'text-muted hover:bg-primary/5 hover:text-primary',
              )}
            >
              <Home className="h-3 w-3" />
              Materials
            </button>
            {crumbs.map((crumb, i) => (
              <span key={crumb.id} className="flex items-center gap-0.5">
                <ChevronRight className="h-3 w-3 shrink-0 text-muted/60" />
                <button
                  type="button"
                  onClick={() => goTo(i + 1)}
                  className={cn(
                    'rounded-md px-1.5 py-1 font-semibold transition-colors',
                    i === crumbs.length - 1
                      ? 'text-navy'
                      : 'text-muted hover:bg-primary/5 hover:text-primary',
                  )}
                >
                  {crumb.name}
                </button>
              </span>
            ))}
          </nav>

          {visibleFolders.length > 0 && (
            <div className="grid gap-2 sm:grid-cols-2">
              {visibleFolders.map((f) => (
                <FolderCard key={f.id} folder={f} onOpen={open} />
              ))}
            </div>
          )}

          {visibleFiles.length > 0 && (
            <div className="space-y-2">
              {visibleFiles.map((f) => (
                <MaterialFileRow key={f.id} file={f} />
              ))}
            </div>
          )}

          {visibleFolders.length === 0 && visibleFiles.length === 0 && (
            <div className="rounded-2xl border border-border/60 bg-surface/70 p-8 text-center">
              <FolderOpen className="mx-auto mb-3 h-9 w-9 text-muted/50" />
              <p className="text-sm font-semibold text-navy">
                {subtest ? `No ${subtest} files in this folder` : 'This folder is empty'}
              </p>
              {subtest && (
                <button
                  type="button"
                  onClick={() => setSubtest(null)}
                  className="mt-2 text-xs font-semibold text-primary hover:underline"
                >
                  Clear the {subtest} filter
                </button>
              )}
            </div>
          )}
        </>
      )}
    </div>
  );
}
