'use client';

import { useMemo, useState } from 'react';
import { ChevronDown, Search } from 'lucide-react';
import type { AllocatableVideo } from '@/lib/user-access';
import {
  SECTION_ORDER,
  languageLabel,
  skinForSubtest,
  type SectionKey,
  type SectionSkin,
} from '@/lib/user-access-sections';

interface VideoScopePickerProps {
  videos: AllocatableVideo[];
  selectedIds: string[];
  onChange: (ids: string[]) => void;
  disabled?: boolean;
  /** Overrides the default "nothing selected" footer copy (per-user allocation wording). */
  emptyHint?: string;
  /** Overrides the default "N allocated" footer copy. Receives the selection count. */
  selectedHint?: (count: number) => string;
}

interface LanguageGroup {
  key: string;
  label: string;
  videos: AllocatableVideo[];
}

interface SectionGroup {
  skin: SectionSkin;
  videos: AllocatableVideo[];
  languages: LanguageGroup[];
}

/** Stable language ordering: English → Arabic → Unspecified. */
const LANGUAGE_ORDER = ['en', 'ar', ''] as const;

function buildGroups(videos: AllocatableVideo[], query: string): SectionGroup[] {
  const needle = query.trim().toLowerCase();
  // Many videos share no distinguishing words in their own title (e.g. "Writing Session 3") — the
  // curated shelf/category name (e.g. "... New Medicine Crash Course ...") is often the only thing
  // that tells a batch of videos apart, so search matches either.
  const filtered = needle
    ? videos.filter(
        (v) =>
          v.title.toLowerCase().includes(needle) ||
          v.categoryNames.some((name) => name.toLowerCase().includes(needle)),
      )
    : videos;

  const bySection = new Map<SectionKey, AllocatableVideo[]>();
  for (const v of filtered) {
    const key = skinForSubtest(v.subtestCode).key;
    (bySection.get(key) ?? bySection.set(key, []).get(key)!).push(v);
  }

  const groups: SectionGroup[] = [];
  for (const key of SECTION_ORDER) {
    const items = bySection.get(key);
    if (!items || items.length === 0) continue;
    const skin = skinForSubtest(key === 'other' ? null : key);

    const byLang = new Map<string, AllocatableVideo[]>();
    for (const v of items) {
      const lang = (v.language ?? '').trim().toLowerCase();
      (byLang.get(lang) ?? byLang.set(lang, []).get(lang)!).push(v);
    }
    const languages: LanguageGroup[] = [];
    const seen = new Set<string>();
    for (const lang of LANGUAGE_ORDER) {
      const bucket = byLang.get(lang);
      if (bucket && bucket.length > 0) {
        languages.push({ key: lang || 'unspecified', label: languageLabel(lang || null), videos: bucket });
        seen.add(lang);
      }
    }
    // Any other language codes not in the canonical order.
    for (const [lang, bucket] of byLang) {
      if (!seen.has(lang)) {
        languages.push({ key: lang || 'unspecified', label: languageLabel(lang || null), videos: bucket });
      }
    }

    groups.push({ skin, videos: items, languages });
  }
  return groups;
}

/** Checkbox that reflects all / none / partial selection of its group. */
function TriStateCheckbox({
  total,
  selected,
  onToggle,
  disabled,
}: {
  total: number;
  selected: number;
  onToggle: () => void;
  disabled?: boolean;
}) {
  const allSelected = total > 0 && selected === total;
  const partial = selected > 0 && selected < total;
  return (
    <input
      type="checkbox"
      checked={allSelected}
      ref={(el) => {
        if (el) el.indeterminate = partial;
      }}
      onChange={onToggle}
      disabled={disabled}
      onClick={(e) => e.stopPropagation()}
      className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
    />
  );
}

/**
 * Grouped, color-coded video allocator: Section (Listening/Reading/Writing/
 * Speaking/…) → Language (English/Arabic/Unspecified) → individual videos.
 * Tick a whole group to grant it, or drill down to single videos. Stores the
 * selected video ids (empty = no restriction — the learner gets everything the
 * Videos module grants).
 */
export function VideoScopePicker({
  videos,
  selectedIds,
  onChange,
  disabled,
  emptyHint,
  selectedHint,
}: VideoScopePickerProps) {
  const [query, setQuery] = useState('');
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const selectedSet = useMemo(() => new Set(selectedIds), [selectedIds]);
  const groups = useMemo(() => buildGroups(videos, query), [videos, query]);

  function setMany(ids: string[], next: boolean) {
    const set = new Set(selectedSet);
    for (const id of ids) {
      if (next) set.add(id);
      else set.delete(id);
    }
    onChange(Array.from(set));
  }

  function toggleOne(id: string) {
    setMany([id], !selectedSet.has(id));
  }

  function countSelected(items: AllocatableVideo[]): number {
    let n = 0;
    for (const v of items) if (selectedSet.has(v.id)) n += 1;
    return n;
  }

  function toggleCollapse(key: string) {
    setCollapsed((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  if (videos.length === 0) {
    return <p className="text-sm text-muted">No videos available to allocate.</p>;
  }

  return (
    <div className="space-y-3">
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden />
        <input
          type="search"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Search by video title or shelf/category (e.g. “Crash”, “December”)…"
          disabled={disabled}
          className="w-full rounded-xl border border-border bg-background-light py-2 pl-9 pr-3 text-sm text-navy placeholder:text-muted focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
        />
      </div>

      {groups.length === 0 ? (
        <p className="text-sm text-muted">No videos match “{query}”.</p>
      ) : null}

      <div className="max-h-80 space-y-2 overflow-y-auto rounded-2xl border border-border bg-background-light p-2">
        {groups.map((group) => {
          const { skin } = group;
          const sectionIds = group.videos.map((v) => v.id);
          const sectionSelected = countSelected(group.videos);
          const isCollapsed = collapsed.has(`s:${skin.key}`);
          const Icon = skin.Icon;
          return (
            <div key={skin.key} className="overflow-hidden rounded-xl border border-border bg-background">
              {/* Section header */}
              <div
                className={`flex items-center gap-2 px-2 py-2 ${skin.glow}`}
                role="button"
                tabIndex={0}
                onClick={() => toggleCollapse(`s:${skin.key}`)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    toggleCollapse(`s:${skin.key}`);
                  }
                }}
              >
                <span className={`h-6 w-1.5 shrink-0 rounded-full ${skin.bar}`} aria-hidden />
                <TriStateCheckbox
                  total={sectionIds.length}
                  selected={sectionSelected}
                  onToggle={() => setMany(sectionIds, sectionSelected !== sectionIds.length)}
                  disabled={disabled}
                />
                <span
                  className={`flex h-6 w-6 items-center justify-center rounded-md bg-gradient-to-br ${skin.tile}`}
                  aria-hidden
                >
                  <Icon className="h-3.5 w-3.5" />
                </span>
                <span className="text-sm font-semibold text-navy">{skin.label}</span>
                <span className="ml-auto text-xs text-muted">
                  {sectionSelected}/{sectionIds.length}
                </span>
                <ChevronDown
                  className={`h-4 w-4 text-muted transition-transform ${isCollapsed ? '-rotate-90' : ''}`}
                  aria-hidden
                />
              </div>

              {/* Language sub-groups */}
              {!isCollapsed ? (
                <div className="space-y-1 border-t border-border px-2 py-2">
                  {group.languages.map((lang) => {
                    const langIds = lang.videos.map((v) => v.id);
                    const langSelected = countSelected(lang.videos);
                    const langKey = `l:${skin.key}:${lang.key}`;
                    const langCollapsed = collapsed.has(langKey);
                    return (
                      <div key={lang.key}>
                        <div className="flex items-center gap-2 rounded-lg px-1 py-1 hover:bg-admin-bg-subtle">
                          <TriStateCheckbox
                            total={langIds.length}
                            selected={langSelected}
                            onToggle={() => setMany(langIds, langSelected !== langIds.length)}
                            disabled={disabled}
                          />
                          <button
                            type="button"
                            onClick={() => toggleCollapse(langKey)}
                            className="flex flex-1 items-center gap-1.5 text-left"
                          >
                            <ChevronDown
                              className={`h-3.5 w-3.5 text-muted transition-transform ${langCollapsed ? '-rotate-90' : ''}`}
                              aria-hidden
                            />
                            <span className="text-xs font-semibold uppercase tracking-wide text-muted">
                              {lang.label}
                            </span>
                            <span className="text-[11px] text-muted">
                              {langSelected}/{langIds.length}
                            </span>
                          </button>
                        </div>

                        {!langCollapsed ? (
                          <div className="ml-6 space-y-0.5 border-l border-border pl-2">
                            {lang.videos.map((video) => (
                              <label
                                key={video.id}
                                className="flex cursor-pointer items-start gap-2 rounded-md px-1.5 py-1 text-sm hover:bg-admin-bg-subtle"
                              >
                                <input
                                  type="checkbox"
                                  checked={selectedSet.has(video.id)}
                                  onChange={() => toggleOne(video.id)}
                                  disabled={disabled}
                                  className="mt-0.5 h-4 w-4 shrink-0 rounded border-border text-primary focus:ring-primary"
                                />
                                <span className="min-w-0">
                                  <span className="block truncate text-navy">{video.title}</span>
                                  {video.categoryNames.length > 0 ? (
                                    <span className="block truncate text-xs text-muted">
                                      {video.categoryNames.join(' · ')}
                                    </span>
                                  ) : null}
                                </span>
                              </label>
                            ))}
                          </div>
                        ) : null}
                      </div>
                    );
                  })}
                </div>
              ) : null}
            </div>
          );
        })}
      </div>

      <p className="text-xs text-muted">
        {selectedIds.length === 0
          ? (emptyHint ?? 'Nothing selected — the learner gets every video their plan grants (for their profession).')
          : (selectedHint ?? ((count) => `${count} video${count === 1 ? '' : 's'} allocated — the learner sees only these.`))(
              selectedIds.length,
            )}
      </p>
    </div>
  );
}
