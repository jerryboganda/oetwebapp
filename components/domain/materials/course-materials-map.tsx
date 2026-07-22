'use client';

import { useEffect, useState } from 'react';
import { BookOpen, Database, FileText, FolderPlus, Loader2, Pencil, Plus, Stethoscope } from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { adminGetMaterialCourseMap, type MaterialCourseMap } from '@/lib/materials-api';

interface CourseMaterialsMapProps {
  onAdvanced: () => void;
  onCreateFolder: (professionId: string | null, professionLabel: string, subtestCode: string | null) => void;
  onAddFile: (folderId: string, subtestCode: string) => void;
  onEditFolder: (folderId: string) => void;
  onEditFile: (fileId: string) => void;
}

export function CourseMaterialsMap({
  onAdvanced,
  onCreateFolder,
  onAddFile,
  onEditFolder,
  onEditFile,
}: CourseMaterialsMapProps) {
  const [data, setData] = useState<MaterialCourseMap | null>(null);
  const [selectedId, setSelectedId] = useState('medicine');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void adminGetMaterialCourseMap().then(setData).catch((reason: Error) => setError(reason.message));
  }, []);

  const selected = data?.professions.find((profession) => profession.id === selectedId);

  return (
    <AdminCatalogLayout
      title="Course Materials"
      eyebrow="CMS"
      description="Choose a profession first. Listening and Reading project the same canonical records; Writing and Speaking stay profession-specific."
      breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Content', href: '/admin/content' }, { label: 'Course Materials' }]}
      hideViewModeToggle
      actions={<Button variant="outline" onClick={onAdvanced}><Database className="mr-1.5 h-4 w-4" />Advanced / Folder Tree</Button>}
    >
      <div className="col-span-full space-y-5">
        {!data && !error ? <div className="flex items-center gap-2 text-sm text-admin-fg-muted"><Loader2 className="h-4 w-4 animate-spin" />Loading course map…</div> : null}
        {error ? <EmptyState illustration={<FileText />} title="Course map unavailable" description={error} /> : null}
        {data ? (
          <>
            {data.unmapped.folderIds.length + data.unmapped.fileIds.length > 0 ? <div role="alert" className="rounded-admin border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900">{data.unmapped.folderIds.length} folder(s) and {data.unmapped.fileIds.length} file(s) still need a structured course scope. They remain preserved and available in Advanced.</div> : null}
            <div role="list" aria-label="Course material areas" className="grid gap-3 sm:grid-cols-2 xl:grid-cols-7">
              {data.professions.map((profession) => (
                <div key={profession.id} role="listitem">
                  <button
                    type="button"
                    aria-label={`Open ${profession.label}`}
                    onClick={() => setSelectedId(profession.id)}
                    className={`h-full w-full rounded-admin border p-4 text-left ${selectedId === profession.id ? 'border-admin-primary bg-admin-primary-tint ring-1 ring-admin-primary' : 'border-admin-border bg-admin-bg-surface hover:border-admin-primary'}`}
                  >
                    <Stethoscope className="mb-3 h-5 w-5 text-admin-primary" />
                    <span className="block text-sm font-bold text-admin-fg-strong">{profession.label}</span>
                    <span className="mt-1 block text-xs text-admin-fg-muted">{profession.sections.reduce((count, section) => count + section.fileCount, 0)} files</span>
                  </button>
                </div>
              ))}
              <div role="listitem">
                <button
                  type="button"
                  aria-label="Open General English"
                  onClick={() => setSelectedId('general_english')}
                  className={`h-full w-full rounded-admin border p-4 text-left ${selectedId === 'general_english' ? 'border-admin-primary bg-admin-primary-tint ring-1 ring-admin-primary' : 'border-admin-border bg-admin-bg-surface hover:border-admin-primary'}`}
                >
                  <BookOpen className="mb-3 h-5 w-5 text-admin-primary" />
                  <span className="block text-sm font-bold text-admin-fg-strong">General English</span>
                  <span className="mt-1 block text-xs text-admin-fg-muted">{data.generalEnglish.fileCount} files · independent</span>
                </button>
              </div>
            </div>

            {selectedId === 'general_english' ? (
              <Card>
                <CardContent className="space-y-3 p-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <h2 className="font-bold text-admin-fg-strong">General English</h2>
                      <p className="mt-1 text-xs text-admin-fg-muted">Separate course area · {data.generalEnglish.folderCount} folders · {data.generalEnglish.fileCount} files</p>
                    </div>
                    <div className="flex gap-2">
                      <Button size="sm" variant="outline" onClick={() => onCreateFolder(null, 'General English', null)}><FolderPlus className="mr-1 h-3.5 w-3.5" />New folder</Button>
                      {data.generalEnglish.folders[0] ? <Button size="sm" onClick={() => onAddFile(data.generalEnglish.folders[0].canonicalFolderId, 'listening')}><Plus className="mr-1 h-3.5 w-3.5" />Add file</Button> : null}
                    </div>
                  </div>
                  <div className="grid gap-2 md:grid-cols-2">
                    {data.generalEnglish.folders.map((folder) => (
                      <div key={folder.canonicalFolderId} className="flex items-center gap-2 rounded-admin bg-admin-bg-subtle px-3 py-2 text-sm">
                        <span className="min-w-0 flex-1 truncate font-medium text-admin-fg-strong">{folder.name}</span>
                        <Button size="sm" variant="ghost" aria-label={`Edit ${folder.name}`} onClick={() => onEditFolder(folder.canonicalFolderId)}><Pencil className="h-3.5 w-3.5" /></Button>
                      </div>
                    ))}
                  </div>
                </CardContent>
              </Card>
            ) : null}

            {selected ? (
              <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                {selected.sections.map((section) => (
                  <Card key={section.subtestCode}>
                    <CardContent className="space-y-3 p-4">
                      <div className="flex items-center justify-between gap-2">
                        <h2 className="font-bold capitalize text-admin-fg-strong">{section.subtestCode}</h2>
                        <Badge variant={section.sharing === 'shared' ? 'success' : 'secondary'}>{section.sharing === 'shared' ? 'Shared canonical' : 'Profession-specific'}</Badge>
                      </div>
                      <p className="text-xs text-admin-fg-muted">{section.folderCount} folders · {section.fileCount} files</p>
                      <div className="flex flex-wrap gap-2">
                        <Button size="sm" variant="outline" onClick={() => onCreateFolder(selected.id, selected.label, section.subtestCode)}><FolderPlus className="mr-1 h-3.5 w-3.5" />New folder</Button>
                        {section.folders[0] ? <Button size="sm" onClick={() => onAddFile(section.folders[0].canonicalFolderId, section.subtestCode)}><Plus className="mr-1 h-3.5 w-3.5" />Add file</Button> : null}
                      </div>
                      <div className="space-y-1.5">
                        {section.folders.map((folder) => (
                          <div key={folder.canonicalFolderId} className="flex items-center gap-2 rounded-admin border border-admin-border px-2.5 py-2 text-xs">
                            <span className="min-w-0 flex-1 truncate font-medium text-admin-fg-strong">{folder.name}</span>
                            <Button size="sm" variant="ghost" aria-label={`Edit ${folder.name}`} onClick={() => onEditFolder(folder.canonicalFolderId)}><Pencil className="h-3.5 w-3.5" /></Button>
                          </div>
                        ))}
                        {section.files.slice(0, 8).map((file) => (
                          <div key={file.canonicalFileId} className="flex items-center gap-2 rounded-admin bg-admin-bg-subtle px-2.5 py-2 text-xs">
                            <span className="min-w-0 flex-1 truncate font-medium text-admin-fg-strong">{file.title}<span className="ml-1 font-normal text-admin-fg-muted">· {file.status}</span></span>
                            <Button size="sm" variant="ghost" aria-label={`Edit ${file.title}`} onClick={() => onEditFile(file.canonicalFileId)}><Pencil className="h-3.5 w-3.5" /></Button>
                          </div>
                        ))}
                        {section.files.length === 0 ? <p className="text-xs text-admin-fg-muted">No files assigned.</p> : null}
                      </div>
                    </CardContent>
                  </Card>
                ))}
              </div>
            ) : null}
          </>
        ) : null}
      </div>
    </AdminCatalogLayout>
  );
}
