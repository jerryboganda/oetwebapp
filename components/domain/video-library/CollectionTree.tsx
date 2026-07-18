'use client';

/**
 * Renders the flat Bunny Stream collection list as a collapsible folder tree.
 *
 * Bunny collections have no native nesting, so the folder hierarchy is encoded
 * in the collection *name* using " / " as a path separator — e.g. a collection
 * named "Medicine / Arabic / Listening" is shown as three nested folders. A path
 * segment that isn't itself a real collection (a pure grouping node, like the
 * "Medicine" parent when only "Medicine / Arabic / …" leaves exist) renders as a
 * non-selectable folder; video counts/sizes aggregate up from all descendants.
 */

import { ChevronDown, ChevronRight, Folder, FolderOpen, Layers, Pencil, Trash2 } from 'lucide-react';
import type { AdminCollection } from '@/lib/api/video-library';

export const PATH_SEP = ' / ';

export interface CollectionTreeNode {
  /** Full path from the root, e.g. "Medicine / Arabic / Listening". */
  path: string;
  /** Last path segment — the folder's display label. */
  label: string;
  /** The real Bunny collection at this exact path, or null for a grouping-only node. */
  collection: AdminCollection | null;
  children: CollectionTreeNode[];
  /** videoCount / size aggregated across this node's own collection plus all descendants. */
  videoCount: number;
  totalSizeBytes: number;
}

/** Split a collection name into trimmed, non-empty path segments. */
export function splitCollectionPath(name: string): string[] {
  return name
    .split('/')
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
}

/** Build a nested folder tree from the flat collection list. */
export function buildCollectionTree(collections: AdminCollection[]): CollectionTreeNode[] {
  const roots: CollectionTreeNode[] = [];
  const index = new Map<string, CollectionTreeNode>();

  for (const collection of collections) {
    const segments = splitCollectionPath(collection.name);
    // A name with no usable segments (empty / only slashes) still deserves a row —
    // fall back to the raw name so it never silently disappears.
    const effective = segments.length > 0 ? segments : [collection.name.trim() || 'Untitled'];

    let siblings = roots;
    let currentPath = '';
    let node: CollectionTreeNode | undefined;
    for (const segment of effective) {
      currentPath = currentPath ? `${currentPath}${PATH_SEP}${segment}` : segment;
      node = index.get(currentPath);
      if (!node) {
        node = {
          path: currentPath,
          label: segment,
          collection: null,
          children: [],
          videoCount: 0,
          totalSizeBytes: 0,
        };
        index.set(currentPath, node);
        siblings.push(node);
      }
      siblings = node.children;
    }
    if (node) node.collection = collection;
  }

  const aggregate = (node: CollectionTreeNode): void => {
    let vc = node.collection?.videoCount ?? 0;
    let sz = node.collection?.totalSizeBytes ?? 0;
    for (const child of node.children) {
      aggregate(child);
      vc += child.videoCount;
      sz += child.totalSizeBytes;
    }
    node.videoCount = vc;
    node.totalSizeBytes = sz;
  };

  const sort = (nodes: CollectionTreeNode[]): void => {
    nodes.sort((a, b) => a.label.localeCompare(b.label));
    nodes.forEach((n) => sort(n.children));
  };

  roots.forEach(aggregate);
  sort(roots);
  return roots;
}

interface CollectionTreeProps {
  nodes: CollectionTreeNode[];
  selectedId: string | null;
  expanded: Set<string>;
  onToggle: (path: string) => void;
  onSelect: (collection: AdminCollection) => void;
  onRename: (collection: AdminCollection) => void;
  onDelete: (collection: AdminCollection) => void;
  canWrite: boolean;
  canSystemAdmin: boolean;
  formatBytes: (bytes: number) => string;
  /** Collection id currently under a drag, highlighted as the drop target. */
  dropTargetId?: string | null;
  /** Fires when a video is dropped onto a folder (only real collections accept drops). */
  onDropCollection?: (collection: AdminCollection) => void;
  /** Fires while dragging over a folder (null clears the highlight). */
  onDragOverCollection?: (collectionId: string | null) => void;
  depth?: number;
}

export function CollectionTree({
  nodes,
  selectedId,
  expanded,
  onToggle,
  onSelect,
  onRename,
  onDelete,
  canWrite,
  canSystemAdmin,
  formatBytes,
  dropTargetId = null,
  onDropCollection,
  onDragOverCollection,
  depth = 0,
}: CollectionTreeProps) {
  return (
    <ul className="space-y-1">
      {nodes.map((node) => {
        const hasChildren = node.children.length > 0;
        const isOpen = expanded.has(node.path);
        const active = node.collection?.collectionId === selectedId;
        const selectable = Boolean(node.collection);
        const isDropTarget = Boolean(node.collection) && node.collection!.collectionId === dropTargetId;
        const acceptsDrop = Boolean(node.collection) && Boolean(onDropCollection);
        return (
          <li key={node.path}>
            <div
              className={`group flex items-center gap-1.5 rounded-admin border px-2 py-2 ${
                isDropTarget
                  ? 'border-admin-primary border-dashed bg-admin-primary-tint ring-1 ring-admin-primary'
                  : active
                    ? 'border-admin-primary bg-admin-primary-tint'
                    : 'border-transparent hover:bg-admin-bg-subtle'
              }`}
              style={{ paddingLeft: `${depth * 16 + 8}px` }}
              onDragOver={
                acceptsDrop
                  ? (e) => {
                      e.preventDefault();
                      e.dataTransfer.dropEffect = 'move';
                      onDragOverCollection?.(node.collection!.collectionId);
                    }
                  : undefined
              }
              onDragLeave={acceptsDrop ? () => onDragOverCollection?.(null) : undefined}
              onDrop={
                acceptsDrop
                  ? (e) => {
                      e.preventDefault();
                      onDropCollection?.(node.collection!);
                    }
                  : undefined
              }
            >
              <button
                type="button"
                onClick={() => hasChildren && onToggle(node.path)}
                className={`shrink-0 rounded p-0.5 text-admin-fg-muted ${
                  hasChildren ? 'hover:text-admin-fg-strong' : 'invisible'
                }`}
                aria-label={isOpen ? 'Collapse' : 'Expand'}
              >
                {isOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
              </button>

              <button
                type="button"
                onClick={() => (selectable ? onSelect(node.collection!) : hasChildren && onToggle(node.path))}
                className="flex min-w-0 flex-1 items-center gap-2 text-left"
              >
                {hasChildren ? (
                  isOpen ? (
                    <FolderOpen className="h-4 w-4 shrink-0 text-admin-fg-muted" />
                  ) : (
                    <Folder className="h-4 w-4 shrink-0 text-admin-fg-muted" />
                  )
                ) : (
                  <Layers className="h-4 w-4 shrink-0 text-admin-fg-muted" />
                )}
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-semibold text-admin-fg-strong">
                    {node.label}
                    {!selectable ? (
                      <span className="ml-1 text-xs font-normal text-admin-fg-muted">(folder)</span>
                    ) : null}
                  </span>
                  <span className="mt-0.5 block text-xs text-admin-fg-muted">
                    {node.videoCount} video{node.videoCount === 1 ? '' : 's'} · {formatBytes(node.totalSizeBytes)}
                  </span>
                </span>
              </button>

              {node.collection && canWrite ? (
                <button
                  type="button"
                  onClick={() => onRename(node.collection!)}
                  className="rounded p-1 text-admin-fg-muted opacity-0 hover:text-admin-fg-strong group-hover:opacity-100"
                  aria-label={`Rename ${node.collection.name}`}
                >
                  <Pencil className="h-3.5 w-3.5" />
                </button>
              ) : null}
              {node.collection && canSystemAdmin ? (
                <button
                  type="button"
                  onClick={() => onDelete(node.collection!)}
                  className="rounded p-1 text-admin-fg-muted opacity-0 hover:text-[var(--admin-danger)] group-hover:opacity-100"
                  aria-label={`Delete ${node.collection.name}`}
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </button>
              ) : null}
            </div>

            {hasChildren && isOpen ? (
              <div className="mt-1">
                <CollectionTree
                  nodes={node.children}
                  selectedId={selectedId}
                  expanded={expanded}
                  onToggle={onToggle}
                  onSelect={onSelect}
                  onRename={onRename}
                  onDelete={onDelete}
                  canWrite={canWrite}
                  canSystemAdmin={canSystemAdmin}
                  formatBytes={formatBytes}
                  dropTargetId={dropTargetId}
                  onDropCollection={onDropCollection}
                  onDragOverCollection={onDragOverCollection}
                  depth={depth + 1}
                />
              </div>
            ) : null}
          </li>
        );
      })}
    </ul>
  );
}
