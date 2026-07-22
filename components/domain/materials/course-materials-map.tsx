'use client';

import { useEffect, useState } from 'react';
import { BookOpen, Database, FileText, Loader2, Stethoscope } from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { adminGetMaterialCourseMap, type MaterialCourseMap } from '@/lib/materials-api';

export function CourseMaterialsMap({ onAdvanced }: { onAdvanced: () => void }) {
  const [data, setData] = useState<MaterialCourseMap | null>(null);
  const [selectedId, setSelectedId] = useState('medicine');
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { void adminGetMaterialCourseMap().then(setData).catch((e: Error) => setError(e.message)); }, []);
  const selected = data?.professions.find((p) => p.id === selectedId);

  return <AdminCatalogLayout title="Course Materials" eyebrow="CMS"
    description="Choose a profession first. Listening and Reading project the same canonical records; Writing and Speaking stay profession-specific."
    breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Content', href: '/admin/content' }, { label: 'Course Materials' }]}
    hideViewModeToggle actions={<Button variant="outline" onClick={onAdvanced}><Database className="mr-1.5 h-4 w-4" />Advanced / Folder Tree</Button>}>
    <div className="col-span-full space-y-5">
      {!data && !error ? <div className="flex items-center gap-2 text-sm text-admin-fg-muted"><Loader2 className="h-4 w-4 animate-spin" />Loading course map…</div> : null}
      {error ? <EmptyState illustration={<FileText />} title="Course map unavailable" description={error} /> : null}
      {data ? <>
        <div role="list" aria-label="Course material areas" className="grid gap-3 sm:grid-cols-2 xl:grid-cols-7">
          {data.professions.map((profession) => <button key={profession.id} type="button" role="listitem" onClick={() => setSelectedId(profession.id)} className={`rounded-admin border p-4 text-left ${selectedId === profession.id ? 'border-admin-primary bg-admin-primary-tint ring-1 ring-admin-primary' : 'border-admin-border bg-admin-bg-surface hover:border-admin-primary'}`}>
            <Stethoscope className="mb-3 h-5 w-5 text-admin-primary" /><span className="block text-sm font-bold text-admin-fg-strong">{profession.label}</span><span className="mt-1 block text-xs text-admin-fg-muted">{profession.sections.reduce((n, s) => n + s.fileCount, 0)} files</span>
          </button>)}
          <button type="button" role="listitem" onClick={() => setSelectedId('general_english')} className={`rounded-admin border p-4 text-left ${selectedId === 'general_english' ? 'border-admin-primary bg-admin-primary-tint ring-1 ring-admin-primary' : 'border-admin-border bg-admin-bg-surface hover:border-admin-primary'}`}>
            <BookOpen className="mb-3 h-5 w-5 text-admin-primary" /><span className="block text-sm font-bold text-admin-fg-strong">General English</span><span className="mt-1 block text-xs text-admin-fg-muted">{data.generalEnglish.fileCount} files · independent</span>
          </button>
        </div>
        {selectedId === 'general_english' ? <Card><CardContent className="p-4"><h2 className="font-bold text-admin-fg-strong">General English</h2><p className="mt-1 text-xs text-admin-fg-muted">Separate course area · {data.generalEnglish.folderCount} folders · {data.generalEnglish.fileCount} files</p></CardContent></Card> : null}
        {selected ? <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">{selected.sections.map((section) => <Card key={section.subtestCode}><CardContent className="space-y-3 p-4">
          <div className="flex items-center justify-between"><h2 className="font-bold capitalize text-admin-fg-strong">{section.subtestCode}</h2><Badge variant={section.sharing === 'shared' ? 'success' : 'secondary'}>{section.sharing === 'shared' ? 'Shared canonical' : 'Profession-specific'}</Badge></div>
          <p className="text-xs text-admin-fg-muted">{section.folderCount} folders · {section.fileCount} files</p>
          <div className="space-y-1.5">{section.files.slice(0, 8).map((file) => <div key={file.canonicalFileId} className="rounded-admin bg-admin-bg-subtle px-2.5 py-2 text-xs"><span className="font-medium text-admin-fg-strong">{file.title}</span><span className="ml-1 text-admin-fg-muted">· {file.status}</span></div>)}{section.files.length === 0 ? <p className="text-xs text-admin-fg-muted">No files assigned.</p> : null}</div>
        </CardContent></Card>)}</div> : null}
      </> : null}
    </div>
  </AdminCatalogLayout>;
}
