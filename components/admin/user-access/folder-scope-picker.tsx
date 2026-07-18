'use client';

import { useMemo, useState } from 'react';
import { ChevronDown } from 'lucide-react';
import type { MaterialFolderDto } from '@/lib/materials-api';
import { skinForName } from '@/lib/user-access-sections';

interface FolderScopePickerProps {
  folderTree: MaterialFolderDto[];
  selectedIds: string[];
  onChange: (ids: string[]) => void;
  disabled?: boolean;
}

/** All folder ids in a subtree, including the folder itself. */
function collectIds(folder: MaterialFolderDto, acc: string[] = []): string[] {
  acc.push(folder.id);
  for (const child of folder.folders ?? []) collectIds(child, acc);
  return acc;
}

/** Checkbox that reflects all / none / partial selection of its subtree. */
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
      onClick={(e) => e.stopPropagation()}
      disabled={disabled}
      className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
    />
  );
}

/**
 * Color-coded Materials folder allocator. Top-level folders are grouped by
 * section (Listening/Reading/Writing/Speaking/General English) with a colored
 * spine + icon and a tri-state "select whole section" checkbox; child folders
 * are individually selectable. Stores selected folder ids (empty = no
 * restriction — the learner gets every folder their plan grants).
 */
export function FolderScopePicker({ folderTree, selectedIds, onChange, disabled }: FolderScopePickerProps) {
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const selectedSet = useMemo(() => new Set(selectedIds), [selectedIds]);

  function setMany(ids: string[], next: boolean) {
    const set = new Set(selectedSet);
    for (const id of ids) {
      if (next) set.add(id);
      else set.delete(id);
    }
    onChange(Array.from(set));
  }

  function toggleCollapse(id: string) {
    setCollapsed((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function renderChild(folder: MaterialFolderDto, depth: number) {
    return (
      <div key={folder.id}>
        <label
          className="flex cursor-pointer items-center gap-2 rounded-md py-1 pr-1 text-sm hover:bg-admin-bg-subtle"
          style={{ paddingLeft: 8 + depth * 16 }}
        >
          <input
            type="checkbox"
            checked={selectedSet.has(folder.id)}
            onChange={() => setMany([folder.id], !selectedSet.has(folder.id))}
            disabled={disabled}
            className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
          />
          <span className="truncate text-navy">{folder.name}</span>
        </label>
        {folder.folders && folder.folders.length > 0 ? (
          <div>{folder.folders.map((child) => renderChild(child, depth + 1))}</div>
        ) : null}
      </div>
    );
  }

  if (folderTree.length === 0) {
    return <p className="text-sm text-muted">No material folders found.</p>;
  }

  return (
    <div className="max-h-80 space-y-2 overflow-y-auto rounded-2xl border border-border bg-background-light p-2">
      {folderTree.map((section) => {
        const skin = skinForName(section.name);
        const ids = collectIds(section);
        const selectedCount = ids.reduce((n, id) => n + (selectedSet.has(id) ? 1 : 0), 0);
        const isCollapsed = collapsed.has(section.id);
        const Icon = skin.Icon;
        const children = section.folders ?? [];
        return (
          <div key={section.id} className="overflow-hidden rounded-xl border border-border bg-background">
            <div
              className={`flex items-center gap-2 px-2 py-2 ${skin.glow}`}
              role="button"
              tabIndex={0}
              onClick={() => toggleCollapse(section.id)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  toggleCollapse(section.id);
                }
              }}
            >
              <span className={`h-6 w-1.5 shrink-0 rounded-full ${skin.bar}`} aria-hidden />
              <TriStateCheckbox
                total={ids.length}
                selected={selectedCount}
                onToggle={() => setMany(ids, selectedCount !== ids.length)}
                disabled={disabled}
              />
              <span
                className={`flex h-6 w-6 items-center justify-center rounded-md bg-gradient-to-br ${skin.tile}`}
                aria-hidden
              >
                <Icon className="h-3.5 w-3.5" />
              </span>
              <span className="truncate text-sm font-semibold text-navy">{section.name}</span>
              <span className="ml-auto shrink-0 text-xs text-muted">
                {selectedCount}/{ids.length}
              </span>
              {children.length > 0 ? (
                <ChevronDown
                  className={`h-4 w-4 shrink-0 text-muted transition-transform ${isCollapsed ? '-rotate-90' : ''}`}
                  aria-hidden
                />
              ) : null}
            </div>

            {!isCollapsed && children.length > 0 ? (
              <div className="border-t border-border py-1">
                {children.map((child) => renderChild(child, 0))}
              </div>
            ) : null}
          </div>
        );
      })}
    </div>
  );
}
