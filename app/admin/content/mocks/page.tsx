'use client';

import { useCallback, useEffect, useState } from 'react';
import { Archive, CheckCircle, Layers, Plus } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import {
  addAdminMockBundleSection,
  archiveAdminMockBundle,
  createAdminMockBundle,
  fetchAdminMockBundles,
  publishAdminMockBundle,
} from '@/lib/api';

type MockBundleRow = {
  id: string;
  title: string;
  mockType: 'full' | 'sub';
  subtestCode: string | null;
  professionId: string | null;
  appliesToAllProfessions: boolean;
  status: 'draft' | 'published' | 'archived';
  estimatedDurationMinutes: number;
  sourceProvenance: string | null;
  sections: {
    id: string;
    sectionOrder: number;
    subtestCode: string;
    contentPaperId: string;
    contentPaperTitle?: string | null;
    contentPaperStatus?: string | null;
    timeLimitMinutes: number;
    reviewEligible: boolean;
  }[];
};

export default function AdminMockBundlesPage() {
  const [loading, setLoading] = useState(true);
  const [rows, setRows] = useState<MockBundleRow[]>([]);
  const [status, setStatus] = useState('');
  const [mockType, setMockType] = useState('');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [newTitle, setNewTitle] = useState('');
  const [newType, setNewType] = useState<'full' | 'sub'>('full');
  const [newSubtest, setNewSubtest] = useState('reading');
  const [newProvenance, setNewProvenance] = useState('Source: Admin-authored mock bundle assembled from published platform content.');
  const [sectionBundleId, setSectionBundleId] = useState('');
  const [sectionPaperId, setSectionPaperId] = useState('');
  const [sectionTimeLimit, setSectionTimeLimit] = useState('60');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const response = await fetchAdminMockBundles({
        status: status || undefined,
        mockType: mockType || undefined,
      }) as { items?: MockBundleRow[] };
      setRows(response.items ?? []);
    } catch {
      setToast({ variant: 'error', message: 'Failed to load mock bundles.' });
    } finally {
      setLoading(false);
    }
  }, [mockType, status]);

  useEffect(() => {
    void load();
  }, [load]);

  async function handleCreate() {
    try {
      await createAdminMockBundle({
        title: newTitle,
        mockType: newType,
        subtestCode: newType === 'sub' ? newSubtest : null,
        professionId: null,
        appliesToAllProfessions: true,
        sourceProvenance: newProvenance,
        priority: 0,
        tagsCsv: '',
      });
      setToast({ variant: 'success', message: 'Mock bundle created.' });
      setNewTitle('');
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Create failed.' });
    }
  }

  async function handleAddSection() {
    try {
      await addAdminMockBundleSection(sectionBundleId, {
        contentPaperId: sectionPaperId,
        timeLimitMinutes: Number(sectionTimeLimit),
      });
      setToast({ variant: 'success', message: 'Section added.' });
      setSectionPaperId('');
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Section add failed.' });
    }
  }

  async function handlePublish(id: string) {
    try {
      await publishAdminMockBundle(id);
      setToast({ variant: 'success', message: 'Mock bundle published.' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Publish gate failed.' });
    }
  }

  async function handleArchive(id: string) {
    try {
      await archiveAdminMockBundle(id);
      setToast({ variant: 'success', message: 'Mock bundle archived.' });
      await load();
    } catch {
      setToast({ variant: 'error', message: 'Archive failed.' });
    }
  }

  return (
    <>
      <AdminRouteWorkspace>
        <AdminRoutePanel>
          <AdminRouteSectionHeader
            eyebrow="Content"
            title="Mock Bundles"
            description="Assemble learner mock routes from published ContentPaper sections and publish them through the mock-specific gate."
            icon={Layers}
          />

          <div className="mb-6 grid gap-4 rounded-2xl border border-border bg-background-light p-4 lg:grid-cols-[1.2fr_0.8fr]">
            <div className="space-y-3">
              <p className="text-xs font-black uppercase tracking-widest text-muted">Create bundle</p>
              <div className="grid gap-3 md:grid-cols-2">
                <Input label="Title" value={newTitle} onChange={(e) => setNewTitle(e.target.value)} placeholder="Full Mock Route 1" />
                <Input label="Provenance" value={newProvenance} onChange={(e) => setNewProvenance(e.target.value)} />
              </div>
              <div className="flex flex-wrap gap-2">
                {(['full', 'sub'] as const).map((type) => (
                  <Button key={type} variant={newType === type ? 'primary' : 'secondary'} onClick={() => setNewType(type)}>
                    {type === 'full' ? 'Full mock' : 'Sub-test mock'}
                  </Button>
                ))}
                {newType === 'sub' ? (
                  <div className="flex flex-wrap gap-2">
                    {['listening', 'reading', 'writing', 'speaking'].map((subtest) => (
                      <Button key={subtest} variant={newSubtest === subtest ? 'primary' : 'secondary'} onClick={() => setNewSubtest(subtest)}>
                        {subtest}
                      </Button>
                    ))}
                  </div>
                ) : null}
                <Button variant="primary" onClick={handleCreate} disabled={!newTitle.trim()}>
                  <Plus className="mr-1 h-4 w-4" /> Create
                </Button>
              </div>
            </div>

            <div className="space-y-3">
              <p className="text-xs font-black uppercase tracking-widest text-muted">Add section</p>
              <Input label="Bundle ID" value={sectionBundleId} onChange={(e) => setSectionBundleId(e.target.value)} placeholder="mock-bundle-..." />
              <Input label="Content paper ID" value={sectionPaperId} onChange={(e) => setSectionPaperId(e.target.value)} placeholder="paper-..." />
              <Input label="Time limit minutes" value={sectionTimeLimit} onChange={(e) => setSectionTimeLimit(e.target.value)} />
              <Button variant="primary" onClick={handleAddSection} disabled={!sectionBundleId || !sectionPaperId}>
                Add section
              </Button>
            </div>
          </div>

          <div className="mb-4 flex flex-wrap items-end gap-3">
            <div>
              <label className="mb-1 block text-xs font-medium uppercase tracking-wide text-muted">Status</label>
              <select value={status} onChange={(e) => setStatus(e.target.value)} className="rounded-lg border border-border bg-surface px-3 py-2 text-sm">
                <option value="">All</option>
                <option value="draft">Draft</option>
                <option value="published">Published</option>
                <option value="archived">Archived</option>
              </select>
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium uppercase tracking-wide text-muted">Type</label>
              <select value={mockType} onChange={(e) => setMockType(e.target.value)} className="rounded-lg border border-border bg-surface px-3 py-2 text-sm">
                <option value="">All</option>
                <option value="full">Full</option>
                <option value="sub">Sub-test</option>
              </select>
            </div>
          </div>

          {loading ? (
            <div className="space-y-3">
              {Array.from({ length: 4 }).map((_, index) => <Skeleton key={index} className="h-24 rounded-2xl" />)}
            </div>
          ) : rows.length === 0 ? (
            <EmptyState
              icon={<Layers className="h-6 w-6" />}
              title="No mock bundles yet"
              description="Create a bundle, attach published ContentPaper sections, then publish it for learners."
            />
          ) : (
            <div className="space-y-3">
              {rows.map((row) => (
                <div key={row.id} className="rounded-2xl border border-border bg-surface p-4">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div>
                      <div className="flex flex-wrap items-center gap-2">
                        <p className="font-semibold text-navy">{row.title}</p>
                        <Badge variant={row.status === 'published' ? 'success' : row.status === 'archived' ? 'muted' : 'info'}>{row.status}</Badge>
                        <Badge variant="default">{row.mockType}</Badge>
                        {row.subtestCode ? <Badge variant="default">{row.subtestCode}</Badge> : null}
                      </div>
                      <p className="mt-1 font-mono text-xs text-muted">{row.id}</p>
                      <p className="mt-2 text-xs text-muted">
                        {row.sections.length} section{row.sections.length === 1 ? '' : 's'} · {row.estimatedDurationMinutes} min · provenance {row.sourceProvenance ? 'present' : 'missing'}
                      </p>
                    </div>
                    <div className="flex flex-wrap gap-2">
                      {row.status !== 'published' ? (
                        <Button variant="primary" onClick={() => handlePublish(row.id)}>
                          <CheckCircle className="mr-1 h-4 w-4" /> Publish
                        </Button>
                      ) : null}
                      {row.status !== 'archived' ? (
                        <Button variant="secondary" onClick={() => handleArchive(row.id)}>
                          <Archive className="mr-1 h-4 w-4" /> Archive
                        </Button>
                      ) : null}
                    </div>
                  </div>

                  {row.sections.length === 0 ? (
                    <div className="mt-4">
                      <InlineAlert variant="warning">Attach published ContentPaper sections before publishing.</InlineAlert>
                    </div>
                  ) : (
                    <div className="mt-4 grid gap-2">
                      {row.sections.map((section) => (
                        <div key={section.id} className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-background-light px-3 py-2 text-sm">
                          <div>
                            <span className="font-semibold text-navy">{section.sectionOrder}. {section.subtestCode}</span>
                            <span className="ml-2 text-muted">{section.contentPaperTitle ?? section.contentPaperId}</span>
                          </div>
                          <div className="flex items-center gap-2 text-xs text-muted">
                            <span>{section.timeLimitMinutes}m</span>
                            <span>{section.reviewEligible ? 'review eligible' : 'no review'}</span>
                            <span>{section.contentPaperStatus ?? 'unknown'}</span>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </AdminRoutePanel>
      </AdminRouteWorkspace>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </>
  );
}
