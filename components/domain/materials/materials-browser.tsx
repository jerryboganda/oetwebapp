'use client';

import { useMemo, useState, useCallback } from 'react';
import {
  Folder, FolderTree, FileText, HardDrive, Search, X, ChevronRight, Home, SearchX,
  FolderOpen, Download, Loader2, Headphones, BookOpen, PenLine, Mic, type LucideIcon,
} from 'lucide-react';
import { Card, cardClassName } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
import { cn } from '@/lib/utils';
import { analytics } from '@/lib/analytics';
import { fetchAuthorizedBlob } from '@/lib/api';
import { isNativeDownloadPlatform, saveBlobNative } from '@/lib/mobile/file-download';
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
  const safeName = folder.name.replace(/[/\\:*?"<>|]/g, '-').trim() || 'materials';
  if (isNativeDownloadPlatform()) {
    // Same blob:-URL dead end as single-file downloads — write the archive
    // to disk and hand it to the native share sheet instead.
    await saveBlobNative(archive, `${safeName}.zip`);
    return;
  }
  const url = URL.createObjectURL(archive);
  try {
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
export type Subtest = (typeof SUBTESTS)[number];

/**
 * Each OET subtest owns a signature colour + icon so the library reads at a
 * glance — a learner spots "Listening" by its headphones before reading a
 * single label. The colours are the skill-* identity tokens (DESIGN.md §2),
 * never status colours. Folders whose name doesn't map to a subtest fall back
 * to the app's violet so nested folders stay cohesive.
 *
 * Exported so the admin Course Materials drill-down
 * (materials-course-browser.tsx) can reuse the same visual language.
 */
export interface SectionSkin {
  Icon: LucideIcon;
  tile: string;   // gradient + foreground for the icon tile
  bar: string;    // start-edge accent bar
  ring: string;   // hover border colour
  glow: string;   // hover background wash
}

export const SECTION_SKINS: Record<Subtest, SectionSkin> = {
  listening: {
    Icon: Headphones,
    tile: 'from-skill-listening/20 to-skill-listening/5 text-skill-listening',
    bar: 'bg-skill-listening', ring: 'hover:border-skill-listening/60', glow: 'hover:bg-skill-listening/5',
  },
  reading: {
    Icon: BookOpen,
    tile: 'from-skill-reading/20 to-skill-reading/5 text-skill-reading',
    bar: 'bg-skill-reading', ring: 'hover:border-skill-reading/60', glow: 'hover:bg-skill-reading/5',
  },
  writing: {
    Icon: PenLine,
    tile: 'from-skill-writing/20 to-skill-writing/5 text-skill-writing',
    bar: 'bg-skill-writing', ring: 'hover:border-skill-writing/60', glow: 'hover:bg-skill-writing/5',
  },
  speaking: {
    Icon: Mic,
    tile: 'from-skill-speaking/20 to-skill-speaking/5 text-skill-speaking',
    bar: 'bg-skill-speaking', ring: 'hover:border-skill-speaking/60', glow: 'hover:bg-skill-speaking/5',
  },
};

export const DEFAULT_SKIN: SectionSkin = {
  Icon: Folder,
  tile: 'from-primary/20 to-primary/5 text-primary',
  bar: 'bg-primary', ring: 'hover:border-primary/40', glow: 'hover:bg-primary/5',
};

// A pressed filter pill is a tint plus a border in its skill colour; never
// white text on a solid skill fill.
const PILL_ACTIVE: Record<Subtest, string> = {
  listening: 'border-skill-listening/30 bg-skill-listening/10 text-skill-listening',
  reading: 'border-skill-reading/30 bg-skill-reading/10 text-skill-reading',
  writing: 'border-skill-writing/30 bg-skill-writing/10 text-skill-writing',
  speaking: 'border-skill-speaking/30 bg-skill-speaking/10 text-skill-speaking',
};

const PILL_BASE =
  'inline-flex min-h-11 items-center gap-1.5 rounded-full border px-3.5 py-1.5 text-xs font-semibold capitalize transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary lg:min-h-8';
const PILL_IDLE = 'border-transparent bg-muted/10 text-muted hover:bg-muted/20 hover:text-navy';

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
    <span className="inline-flex items-center gap-1 text-2xs font-medium tabular-nums text-muted">
      <Icon className="h-3 w-3 opacity-70" aria-hidden="true" />
      {children}
    </span>
  );
}

function FolderCard({
  folder,
  onOpen,
}: {
  folder: LearnerMaterialFolderDto;
  onOpen: (id: string) => void;
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
      className={cn(
        cardClassName({ hoverable: true, padding: 'none' }),
        'group relative flex h-full items-center gap-3 overflow-hidden py-3.5 ps-4 pe-3',
        skin.ring, skin.glow,
      )}
    >
      {/* Signature accent bar — the section's colour spine */}
      <span
        aria-hidden
        className={cn(
          'absolute inset-y-0 start-0 w-1 rounded-e-full opacity-70 transition-opacity duration-200 group-hover:opacity-100',
          skin.bar,
        )}
      />

      <button
        type="button"
        onClick={() => onOpen(folder.id)}
        className="flex min-w-0 flex-1 items-center gap-3.5 rounded-xl text-start focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
      >
        <span
          className={cn(
            'flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br shadow-inner',
            skin.tile,
          )}
        >
          <Icon className="size-5.5" aria-hidden="true" />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm font-bold text-navy" title={folder.name}>
            {folder.name}
          </span>
          <span className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1">
            {stats.folders > 0 && (
              <MetaChip icon={FolderTree}>{stats.folders} folder{stats.folders === 1 ? '' : 's'}</MetaChip>
            )}
            <MetaChip icon={FileText}>{stats.files} file{stats.files === 1 ? '' : 's'}</MetaChip>
            {stats.bytes > 0 && <MetaChip icon={HardDrive}>{formatBytes(stats.bytes)}</MetaChip>}
            {error && <span className="text-2xs font-semibold text-danger-strong">· download failed</span>}
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
          className="pressable flex size-11 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary-dark transition-colors hover:bg-primary/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-50 lg:size-9"
        >
          {zipping ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Download className="h-4 w-4" aria-hidden="true" />}
        </button>
      )}
      <ChevronRight
        className="h-4 w-4 shrink-0 text-muted transition-transform duration-200 group-hover:text-navy group-hoverable:translate-x-0.5 rtl:rotate-180 rtl:group-hoverable:-translate-x-0.5"
        aria-hidden="true"
      />
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

  const crumbClass = (current: boolean) =>
    cn(
      'flex min-h-9 items-center gap-1 rounded-md px-1.5 py-1 font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
      current ? 'text-navy' : 'text-muted hover:bg-primary/5 hover:text-primary',
    );

  return (
    <div className="space-y-4">
      {/* Search + filters */}
      <Card padding="sm" className="space-y-3">
        <div className="group relative">
          <Search className="pointer-events-none absolute start-3.5 top-1/2 size-4.5 -translate-y-1/2 text-muted transition-colors group-focus-within:text-primary" aria-hidden="true" />
          <input
            type="search"
            value={query}
            onChange={(e) => handleSearch(e.target.value)}
            placeholder={`Search ${index.length} files…`}
            aria-label="Search materials"
            className="w-full rounded-xl border border-border bg-background-light py-3 ps-11 pe-12 text-sm text-navy shadow-inner placeholder:text-muted focus:border-primary/50 focus:outline-none focus:ring-2 focus:ring-primary/20"
          />
          {query && (
            <button
              type="button"
              onClick={() => setQuery('')}
              aria-label="Clear search"
              className="absolute end-1.5 top-1/2 inline-flex size-9 -translate-y-1/2 items-center justify-center rounded-md text-muted transition-colors hover:bg-muted/10 hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
              <X className="h-4 w-4" aria-hidden="true" />
            </button>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-1.5">
          <button
            type="button"
            onClick={() => setSubtest(null)}
            aria-pressed={subtest === null}
            className={cn(PILL_BASE, subtest === null ? 'border-primary/30 bg-primary/10 text-primary' : PILL_IDLE)}
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
                aria-pressed={active}
                className={cn(PILL_BASE, active ? PILL_ACTIVE[s] : PILL_IDLE)}
              >
                {s}
                {count ? (
                  <span
                    className={cn(
                      'rounded-full px-1.5 py-px text-3xs font-bold tabular-nums',
                      active ? 'bg-surface/80' : 'bg-muted/15 text-muted',
                    )}
                  >
                    {count}
                  </span>
                ) : null}
              </button>
            );
          })}
        </div>
      </Card>

      {/* Search results replace navigation entirely */}
      {searching ? (
        <div className="space-y-2">
          <p className="px-1 text-xs font-semibold tabular-nums text-muted">
            {results.length} result{results.length === 1 ? '' : 's'} for “{query.trim()}”
          </p>
          {results.length === 0 ? (
            <EmptyState
              icon={<SearchX className="h-7 w-7" aria-hidden="true" />}
              title={`No files match “${query.trim()}”`}
              description="Try a shorter search, or clear the subtest filter."
            />
          ) : (
            results.map((hit, i) => (
              <MotionItem key={hit.file.id} delayIndex={Math.min(i, 5)}>
                <MaterialFileRow file={hit.file} path={hit.path} />
              </MotionItem>
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
              aria-current={atRoot ? 'page' : undefined}
              className={crumbClass(atRoot)}
            >
              <Home className="h-3 w-3" aria-hidden="true" />
              Materials
            </button>
            {crumbs.map((crumb, i) => (
              <span key={crumb.id} className="flex items-center gap-0.5">
                <ChevronRight className="h-3 w-3 shrink-0 text-muted/60 rtl:rotate-180" aria-hidden="true" />
                <button
                  type="button"
                  onClick={() => goTo(i + 1)}
                  aria-current={i === crumbs.length - 1 ? 'page' : undefined}
                  className={crumbClass(i === crumbs.length - 1)}
                >
                  {crumb.name}
                </button>
              </span>
            ))}
          </nav>

          {visibleFolders.length > 0 && (
            <div className="space-y-2">
              <p className="px-1 eyebrow text-muted">
                {atRoot ? 'Sections' : 'Folders'}
                <span className="ms-1.5 font-semibold normal-case tracking-normal tabular-nums text-muted/70">
                  {visibleFolders.length}
                </span>
              </p>
              <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2">
                {visibleFolders.map((f, i) => (
                  <MotionItem key={f.id} delayIndex={Math.min(i, 5)} className="h-full">
                    <FolderCard folder={f} onOpen={open} />
                  </MotionItem>
                ))}
              </div>
            </div>
          )}

          {visibleFiles.length > 0 && (
            <div className="space-y-2">
              {visibleFolders.length > 0 && (
                <p className="px-1 pt-2 eyebrow text-muted">
                  Files
                  <span className="ms-1.5 font-semibold normal-case tracking-normal tabular-nums text-muted/70">
                    {visibleFiles.length}
                  </span>
                </p>
              )}
              {visibleFiles.map((f, i) => (
                <MotionItem key={f.id} delayIndex={Math.min(i, 5)}>
                  <MaterialFileRow file={f} />
                </MotionItem>
              ))}
            </div>
          )}

          {visibleFolders.length === 0 && visibleFiles.length === 0 && (
            <EmptyState
              icon={<FolderOpen className="h-7 w-7" aria-hidden="true" />}
              title={subtest ? `No ${subtest} files in this folder` : 'This folder is empty'}
              action={subtest ? { label: `Clear the ${subtest} filter`, onClick: () => setSubtest(null) } : undefined}
            />
          )}
        </>
      )}
    </div>
  );
}
