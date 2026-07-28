'use client';

import { useMemo, useState } from 'react';
import {
  ArrowLeft,
  ChevronRight,
  File as FileIcon,
  FileText,
  FolderOpen,
  FolderPlus,
  Image as ImageIcon,
  Music,
  Pencil,
  Plus,
  Video,
} from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import type { MaterialCourseMapSection } from '@/lib/materials-api';
import {
  buildBreadcrumbTrail,
  buildChildrenByParent,
  buildFilesByFolder,
  buildFoldersById,
  countDescendants,
  resolveVirtualRoot,
} from '@/lib/materials-course-tree';
import { DEFAULT_SKIN, SECTION_SKINS, type Subtest } from './materials-browser';

interface MaterialsCourseBrowserProps {
  section: MaterialCourseMapSection;
  title: string;
  onBack: () => void;
  onCreateFolder: (parentFolderId: string | null) => void;
  onAddFile: (folderId: string) => void;
  onEditFolder: (folderId: string) => void;
  onEditFile: (fileId: string) => void;
}

function FileKindIcon({ kind }: { kind: string }) {
  switch (kind) {
    case 'audio':
      return <Music className="h-4 w-4 text-blue-500" />;
    case 'video':
      return <Video className="h-4 w-4 text-fuchsia-500" />;
    case 'image':
      return <ImageIcon className="h-4 w-4 text-emerald-500" />;
    case 'document':
      return <FileIcon className="h-4 w-4 text-amber-500" />;
    default:
      return <FileText className="h-4 w-4 text-red-400" />;
  }
}

export function MaterialsCourseBrowser({
  section,
  title,
  onBack,
  onCreateFolder,
  onAddFile,
  onEditFolder,
  onEditFile,
}: MaterialsCourseBrowserProps) {
  const foldersById = useMemo(() => buildFoldersById(section.folders), [section.folders]);
  const childrenByParent = useMemo(() => buildChildrenByParent(section.folders), [section.folders]);
  const filesByFolder = useMemo(() => buildFilesByFolder(section.files), [section.files]);
  // Production content nests real folders one (or two) levels beneath a
  // "Listening"/"Reading"/"Writing"/"Speaking" wrapper folder that exists
  // purely to anchor the section's scope — never real content the admin
  // should have to click through. This unwraps down to wherever the real
  // folders/files actually start, so "Reading" opens straight onto Jahshan /
  // Benchmark / etc. instead of a single redundant "Reading" folder card.
  const virtualRootId = useMemo(
    () => resolveVirtualRoot(childrenByParent, filesByFolder),
    [childrenByParent, filesByFolder],
  );
  const [currentFolderId, setCurrentFolderId] = useState<string | null>(virtualRootId);

  const trail = useMemo(
    () => buildBreadcrumbTrail(currentFolderId, foldersById, virtualRootId),
    [currentFolderId, foldersById, virtualRootId],
  );

  const childFolders = childrenByParent.get(currentFolderId) ?? [];
  const currentFiles = filesByFolder.get(currentFolderId) ?? [];
  const skin = SECTION_SKINS[section.subtestCode as Subtest] ?? DEFAULT_SKIN;
  const Icon = skin.Icon;
  const isEmpty = childFolders.length === 0 && currentFiles.length === 0;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Button size="sm" variant="ghost" onClick={onBack}>
          <ArrowLeft className="mr-1.5 h-4 w-4" />
          Back to Course Materials
        </Button>
        <div className="flex gap-2">
          <Button size="sm" variant="outline" onClick={() => onCreateFolder(currentFolderId)}>
            <FolderPlus className="mr-1.5 h-3.5 w-3.5" />
            New folder
          </Button>
          {currentFolderId ? (
            <Button size="sm" onClick={() => onAddFile(currentFolderId)}>
              <Plus className="mr-1.5 h-3.5 w-3.5" />
              Add file
            </Button>
          ) : null}
        </div>
      </div>

      <div className="flex items-center gap-2">
        <span className={`inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-gradient-to-br ${skin.tile}`}>
          <Icon className="h-4 w-4" />
        </span>
        <nav aria-label="Breadcrumb" className="flex flex-wrap items-center gap-1 text-sm">
          <button
            type="button"
            className={currentFolderId === virtualRootId ? 'font-semibold text-admin-fg-strong' : 'font-semibold text-admin-fg-muted hover:text-admin-fg-strong'}
            onClick={() => setCurrentFolderId(virtualRootId)}
          >
            {title}
          </button>
          {trail.map((crumb) => (
            <span key={crumb.canonicalFolderId} className="flex items-center gap-1">
              <ChevronRight className="h-3.5 w-3.5 text-admin-fg-muted" />
              <button
                type="button"
                className={currentFolderId === crumb.canonicalFolderId ? 'font-semibold text-admin-fg-strong' : 'font-semibold text-admin-fg-muted hover:text-admin-fg-strong'}
                onClick={() => setCurrentFolderId(crumb.canonicalFolderId)}
              >
                {crumb.name}
              </button>
            </span>
          ))}
        </nav>
      </div>

      {isEmpty ? (
        <EmptyState illustration={<FileText />} title="No files in this folder yet" size="sm" />
      ) : (
        <div className="space-y-3">
          {childFolders.length > 0 ? (
            <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
              {childFolders.map((folder) => {
                const counts = countDescendants(folder.canonicalFolderId, childrenByParent, filesByFolder);
                return (
                  <Card key={folder.canonicalFolderId}>
                    <CardContent className="space-y-1 p-3">
                      <div className="flex items-start justify-between gap-2">
                        <button
                          type="button"
                          className="flex min-w-0 flex-1 items-center gap-2 text-left"
                          onClick={() => setCurrentFolderId(folder.canonicalFolderId)}
                        >
                          <FolderOpen className="h-4 w-4 shrink-0 text-admin-fg-muted" />
                          <span className="min-w-0 flex-1 truncate text-sm font-semibold text-admin-fg-strong">{folder.name}</span>
                        </button>
                        <Button size="sm" variant="ghost" aria-label={`Edit ${folder.name}`} onClick={() => onEditFolder(folder.canonicalFolderId)}>
                          <Pencil className="h-3.5 w-3.5" />
                        </Button>
                      </div>
                      <div className="flex items-center gap-2 pl-6">
                        <Badge variant={folder.status === 'Published' ? 'success' : 'secondary'}>{folder.status}</Badge>
                        <span className="text-xs text-admin-fg-muted">{counts.folderCount} folders · {counts.fileCount} files</span>
                      </div>
                    </CardContent>
                  </Card>
                );
              })}
            </div>
          ) : null}

          {currentFiles.length > 0 ? (
            <div className="space-y-1.5">
              {currentFiles.map((file) => (
                <div key={file.canonicalFileId} className="flex items-center gap-2 rounded-admin border border-admin-border bg-admin-bg-subtle px-2.5 py-2 text-xs">
                  <FileKindIcon kind={file.kind} />
                  <span className="min-w-0 flex-1 truncate font-medium text-admin-fg-strong">
                    {file.title}
                    <span className="ml-1 font-normal text-admin-fg-muted">· {file.status}</span>
                  </span>
                  <Button size="sm" variant="ghost" aria-label={`Edit ${file.title}`} onClick={() => onEditFile(file.canonicalFileId)}>
                    <Pencil className="h-3.5 w-3.5" />
                  </Button>
                </div>
              ))}
            </div>
          ) : null}
        </div>
      )}
    </div>
  );
}
