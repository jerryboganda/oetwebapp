'use client';

import type { MaterialFolderDto } from '@/lib/materials-api';

interface FolderScopePickerProps {
  folderTree: MaterialFolderDto[];
  selectedIds: string[];
  onChange: (ids: string[]) => void;
  disabled?: boolean;
}

/** Recursive checkbox tree over the Materials Library folder tree, storing selected folder ids. */
export function FolderScopePicker({ folderTree, selectedIds, onChange, disabled }: FolderScopePickerProps) {
  const selectedSet = new Set(selectedIds);

  function toggle(id: string) {
    const next = new Set(selectedSet);
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    onChange(Array.from(next));
  }

  function renderNode(folder: MaterialFolderDto, depth: number) {
    return (
      <div key={folder.id}>
        <label
          className="flex cursor-pointer items-center gap-2 rounded-lg py-1 text-sm hover:bg-admin-bg-subtle"
          style={{ paddingLeft: depth * 16 }}
        >
          <input
            type="checkbox"
            checked={selectedSet.has(folder.id)}
            onChange={() => toggle(folder.id)}
            disabled={disabled}
            className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
          />
          <span className="text-navy">{folder.name}</span>
        </label>
        {folder.folders && folder.folders.length > 0 ? (
          <div>{folder.folders.map((child) => renderNode(child, depth + 1))}</div>
        ) : null}
      </div>
    );
  }

  if (folderTree.length === 0) {
    return <p className="text-sm text-muted">No material folders found.</p>;
  }

  return (
    <div className="max-h-64 space-y-0.5 overflow-y-auto rounded-2xl border border-border bg-background-light p-3">
      {folderTree.map((folder) => renderNode(folder, 0))}
    </div>
  );
}
