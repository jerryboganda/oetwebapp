'use client';

import { useMemo, useState, useCallback } from 'react';
import {
  Folder, FolderTree, FileText, HardDrive, Search, X, ChevronRight, Home, SearchX,
  FolderOpen, Download, Loader2, Headphones, BookOpen, PenLine, Mic, type LucideIcon,
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
type Subtest = (typeof SUBTESTS)[number];

/**
 * Each OET subtest owns a signature colour + icon so the library reads at a
 * glance — a learner spots "Listening" by its blue headphones before reading a
 * single label. Folders whose name doesn't map to a subtest fall back to the
 * app's violet so nested folders stay cohesive.
 */
interface SectionSkin {
  Icon: LucideIcon;
  tile: string;   // gradient + foreground for the icon tile
  bar: string;    // left accent bar
  ring: string;   // hover border colour
  glow: string;   // hover background wash
}

const SECTION_SKINS: Record<Subtest, SectionSkin> = {
  listening: {
    Icon: Headphones,
    tile: 'from-blue-500/20 to-blue-500/5 text-blue-600 dark:text-blue-300',
    bar: 'bg-blue-500', ring: 'hover:border-blue-400/60', glow: 'hover:bg-blue-500/[0.04]',
  },
  reading: {
    Icon: BookOpen,
    tile: 'from-emerald-500/20 to-emerald-500/5 text-emerald-600 dark:text-emerald-300',
    bar: 'bg-emerald-500', ring: 'hover:border-emerald-400/60', glow: 'hover:bg-emerald-500/[0.04]',
  },
  writing: {
    Icon: PenLine,
    tile: 'from-amber-500/20 to-amber-500/5 text-amber-600 dark:text-amber-300',
    bar: 'bg-amber-500', ring: 'hover:border-amber-400/60', glow: 'hover:bg-amber-500/[0.04]',
  },
  speaking: {
    Icon: Mic,
    tile: 'from-purple-500/20 to-purple-500/5 text-purple-600 dark:text-purple-300',
    bar: 'bg-purple-500', ring: 'hover:border-purple-400/60', glow: 'hover:bg-purple-500/[0.04]',
  },
};

const DEFAULT_SKIN: SectionSkin = {
  Icon: Folder,
  tile: 'from-primary/20 to-primary/5 text-primary',
  bar: 'bg-primary', ring: 'hover:border-primary/40', glow: 'hover:bg-primary/[0.03]',
};

const PILL_ACTIVE: Record<Subtest, string> = {
  listening: 'bg-blue-500 text-white shadow-sm shadow-blue-500/25',
  reading: 'bg-emerald-500 text-white shadow-sm shadow-emerald-500/25',
  writing: 'bg-amber-500 text-white shadow-sm shadow-amber-500/25',
  speaking: 'bg-purple-500 text-white shadow-sm shadow-purple-500/25',
};

function matchSubtest(name: string): Subtest | null {
  const n = name.toLowerCase();
  return SUBTESTS.find((s) => n.includes(s)) ?? null;
}

function skinFor(name: string): SectionSkin {
  const s = matchSubtest(name);
  return s ? SECTION_SKINS[s] : DEFAULT_SKIN;
}

/** Small icon + value chip for folder-card metadata (folders / files / size). */
function MetaChip({ icon: Icon, children }: { icon: LucideIcon; children: React.ReactNode }) {
  return (
    <span className="inline-flex items-center gap-1 text-[11px] font-medium text-muted">
      <Icon className="h-3 w-3 opacity-70" />
      {children}
    </span>
  );
}

function FolderCard({
  folder,
  onOpen,
  index,
}: {
  folder: LearnerMaterialFolderDto;
  onOpen: (id: string) => void;
  index: number;
}) {
  const stats = useMemo(() => folderStats(folder), [folder]);
  const skin = useMemo(() => skinFor(folder.name), [folder.name]);
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

  const { Icon } = skin;

  return (
    <div
      style={{ animationDelay: `${Math.min(index, 8) * 45}ms` }}
      className={cn(
        'group relative flex items-center gap-3 overflow-hidden rounded-2xl border border-border/60 bg-surface/70 pl-4 pr-3 py-3.5 text-left shadow-sm transition-all duration-200 hover:-translate-y-0.5 hover:shadow-md motion-reduce:hover:translate-y-0',
        'animate-in fade-in slide-in-from-bottom-2 fill-mode-both motion-reduce:animate-none',
        skin.ring, skin.glow,
      )}
    >
      {/* Signature accent bar — the section's colour spine */}
      <span
        aria-hidden
        className={cn(
          'absolute inset-y-0 left-0 w-1 origin-top scale-y-100 rounded-r-full opacity-70 transition-all duration-200 group-hover:opacity-100',
          skin.bar,
        )}
      />

      <button
        type="button"
        onClick={() => onOpen(folder.id)}
        className="flex min-w-0 flex-1 items-center gap-3.5 text-left"
      >
        <span
          className={cn(
            'flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br shadow-inner transition-transform duration-200 group-hover:scale-105 motion-reduce:group-hover:scale-100',
            skin.tile,
          )}
        >
          <Icon className="h-[22px] w-[22px]" />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block truncate text-[15px] font-bold text-navy" title={folder.name}>
            {folder.name}
          </span>
          <span className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1">
            {stats.folders > 0 && (
              <MetaChip icon={FolderTree}>{stats.folders} folder{stats.folders === 1 ? '' : 's'}</MetaChip>
            )}
            <MetaChip icon={FileText}>{stats.files} file{stats.files === 1 ? '' : 's'}</MetaChip>
            {stats.bytes > 0 && <MetaChip icon={HardDrive}>{formatBytes(stats.bytes)}</MetaChip>}
            {error && <span className="text-[11px] font-semibold text-red-500">· download failed</span>}
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
          className="pressable flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary-dark transition-colors hover:bg-primary/20 disabled:opacity-50"
        >
          {zipping ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}
        </button>
      )}
      <ChevronRight className="h-4 w-4 shrink-0 text-muted transition-transform duration-200 group-hover:translate-x-0.5 group-hover:text-navy" />
    </div>
  );
}

export function MaterialsBrowser({ folders }: { folders: LearnerMaterialFolderDto[] }) {
  const [trail, setTrail] = useState<string[]>([]);
  const [query, setQuery] = useState('');
  const [subtest, setSubtest] = useState<string | null>(null);

  const index = useMemo(() => flattenFiles(folders), [folders]);

  /** Live per-subtest file counts, surfaced as badges on the filter pills. */
  const subtestCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const entry of index) {
      const code = entry.file.subtestCode?.toLowerCase();
      if (code) counts[code] = (counts[code] ?? 0) + 1;
    }
    return counts;
  }, [index]);

  const open = useCallback((id: string) => {
    setTrail((t) => [...t, id]);
    setQuery('');
  }, []);

  const goTo = useCallback((depth: number) => {
    setTrail((t) => t.slice(0, depth));
  }, []);

  const crumbs = useMemo(() => resolveTrail(folders, trail), [folders, trail]);
  const current = crumbs.length > 0 ? crumbs[crumbs.length - 1] : null;
  const atRoot = crumbs.length === 0;

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
      <div className="space-y-3 rounded-2xl border border-border/60 bg-gradient-to-b from-surface/90 to-surface/60 p-3 shadow-sm sm:p-4">
        <div className="group relative">
          <Search className="pointer-events-none absolute left-3.5 top-1/2 h-[18px] w-[18px] -translate-y-1/2 text-muted transition-colors group-focus-within:text-primary" />
          <input
            type="search"
            value={query}
            onChange={(e) => handleSearch(e.target.value)}
            placeholder={`Search ${index.length} files…`}
            aria-label="Search materials"
            className="w-full rounded-xl border border-border bg-background-light py-3 pl-11 pr-10 text-[15px] text-navy shadow-inner placeholder:text-muted focus:border-primary/50 focus:outline-none focus:ring-2 focus:ring-primary/20"
          />
          {query && (
            <button
              type="button"
              onClick={() => setQuery('')}
              aria-label="Clear search"
              className="absolute right-2.5 top-1/2 -translate-y-1/2 rounded-md p-1 text-muted transition-colors hover:bg-muted/10 hover:text-navy"
            >
              <X className="h-4 w-4" />
            </button>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-1.5">
          <button
            type="button"
            onClick={() => setSubtest(null)}
            className={cn(
              'rounded-full px-3.5 py-1.5 text-xs font-semibold capitalize transition-all',
              subtest === null
                ? 'bg-navy text-white shadow-sm'
                : 'bg-muted/10 text-muted hover:bg-muted/20 hover:text-navy',
            )}
          >
            All
          </button>
          {SUBTESTS.map((s) => {
            const active = subtest === s;
            const count = subtestCounts[s];
            return (
              <button
                key={s}
                type="button"
                onClick={() => setSubtest((cur) => (cur === s ? null : s))}
                className={cn(
                  'flex items-center gap-1.5 rounded-full px-3.5 py-1.5 text-xs font-semibold capitalize transition-all',
                  active ? PILL_ACTIVE[s] : 'bg-muted/10 text-muted hover:bg-muted/20 hover:text-navy',
                )}
              >
                {s}
                {count ? (
                  <span
                    className={cn(
                      'rounded-full px-1.5 py-px text-[10px] font-bold tabular-nums',
                      active ? 'bg-white/25 text-white' : 'bg-muted/15 text-muted',
                    )}
                  >
                    {count}
                  </span>
                ) : null}
              </button>
            );
          })}
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
                atRoot ? 'text-navy' : 'text-muted hover:bg-primary/5 hover:text-primary',
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
            <div className="space-y-2">
              <p className="px-1 text-[11px] font-bold uppercase tracking-wider text-muted">
                {atRoot ? 'Sections' : 'Folders'}
                <span className="ml-1.5 font-semibold normal-case tracking-normal text-muted/70">
                  {visibleFolders.length}
                </span>
              </p>
              <div className="grid gap-2.5 sm:grid-cols-2">
                {visibleFolders.map((f, i) => (
                  <FolderCard key={f.id} folder={f} onOpen={open} index={i} />
                ))}
              </div>
            </div>
          )}

          {visibleFiles.length > 0 && (
            <div className="space-y-2">
              {visibleFolders.length > 0 && (
                <p className="px-1 pt-2 text-[11px] font-bold uppercase tracking-wider text-muted">
                  Files
                  <span className="ml-1.5 font-semibold normal-case tracking-normal text-muted/70">
                    {visibleFiles.length}
                  </span>
                </p>
              )}
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
