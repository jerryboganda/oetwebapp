'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { BookOpen, Database, Languages, Loader2, Stethoscope } from 'lucide-react';
import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button, buttonVariants } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { adminGetVideoCourseMap, type VideoCourseMap } from '@/lib/api/video-library';

export function CourseVideosMap({ onAdvanced }: { onAdvanced: () => void }) {
  const [data, setData] = useState<VideoCourseMap | null>(null);
  const [selectedId, setSelectedId] = useState('medicine');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { void adminGetVideoCourseMap().then(setData).catch((e: Error) => setError(e.message)); }, []);
  const selected = data?.professions.find((p) => p.id === selectedId);

  return (
    <AdminOperationsLayout
      eyebrow="Content"
      title="Course Videos"
      description="Choose a profession first. Languages and subtests stay inside its working pane; shared rows keep one canonical video ID."
      breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Content', href: '/admin/content' }, { label: 'Course Videos' }]}
      actions={<Button variant="outline" onClick={onAdvanced}><Database className="mr-1.5 h-4 w-4" />Advanced / Storage</Button>}
    >
      {!data && !error ? <div className="flex items-center gap-2 text-sm text-admin-fg-muted"><Loader2 className="h-4 w-4 animate-spin" />Loading course map…</div> : null}
      {error ? <EmptyState icon={<BookOpen className="h-6 w-6" />} title="Course map unavailable" description={error} /> : null}
      {data ? <div className="space-y-5">
        <div role="list" aria-label="Course video professions" className="grid gap-3 sm:grid-cols-2 xl:grid-cols-6">
          {data.professions.map((profession) => (
            <button key={profession.id} type="button" role="listitem" onClick={() => setSelectedId(profession.id)}
              className={`rounded-admin border p-4 text-left transition ${selectedId === profession.id ? 'border-admin-primary bg-admin-primary-tint ring-1 ring-admin-primary' : 'border-admin-border bg-admin-bg-surface hover:border-admin-primary'}`}>
              <Stethoscope className="mb-3 h-5 w-5 text-admin-primary" />
              <span className="block text-sm font-bold text-admin-fg-strong">{profession.label}</span>
              <span className="mt-1 block text-xs text-admin-fg-muted">{profession.languages.reduce((n, l) => n + l.sections.reduce((x, s) => x + s.count, 0), 0)} projected videos</span>
            </button>
          ))}
        </div>

        {selected ? <div className="grid gap-4 xl:grid-cols-2">
          {selected.languages.map((language) => <Card key={language.code}>
            <CardContent className="space-y-4 p-4">
              <div className="flex items-center gap-2"><Languages className="h-4 w-4 text-admin-primary" /><h2 className="font-bold text-admin-fg-strong">{language.label}</h2></div>
              {language.sections.filter((s) => s.available).map((section) => <section key={section.subtestCode} className="rounded-admin border border-admin-border p-3">
                <div className="mb-2 flex items-center justify-between gap-2"><h3 className="text-sm font-semibold capitalize text-admin-fg-strong">{section.subtestCode}</h3><div className="flex items-center gap-2"><Badge variant="secondary">{section.count}</Badge><Link href={`/admin/content/videos/new?profession=${selected.id}&language=${language.code}&subtest=${section.subtestCode}`} className={buttonVariants({ variant: 'outline', size: 'sm' })}>New</Link></div></div>
                {section.items.length === 0 ? <p className="text-xs text-admin-fg-muted">No videos assigned.</p> : <div className="space-y-2">{section.items.map((item) => <div key={item.canonicalVideoId} className="flex items-center gap-2 rounded-admin bg-admin-bg-subtle px-3 py-2">
                  <div className="min-w-0 flex-1"><p className="truncate text-sm font-medium text-admin-fg-strong">{item.title}</p><p className="text-xs text-admin-fg-muted">{item.sourceLabel} · {item.status}</p></div>
                  <Link href={`/admin/content/videos/${item.canonicalVideoId}/details`} className={buttonVariants({ variant: 'outline', size: 'sm' })}>Edit</Link>
                </div>)}</div>}
              </section>)}
            </CardContent>
          </Card>)}
        </div> : null}
      </div> : null}
    </AdminOperationsLayout>
  );
}
