'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { Archive, BarChart3, CalendarClock, CheckCircle, Layers, Plus } from 'lucide-react';
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
  mockType: 'full' | 'lrw' | 'sub' | 'part' | 'diagnostic' | 'final_readiness' | 'remedial';
  subtestCode: string | null;
  professionId: string | null;
  appliesToAllProfessions: boolean;
  status: 'draft' | 'published' | 'archived';
  estimatedDurationMinutes: number;
  sourceProvenance: string | null;
  difficulty?: string;
  sourceStatus?: string;
  qualityStatus?: string;
  releasePolicy?: string;
  topicTagsCsv?: string;
  skillTagsCsv?: string;
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
  const [newType, setNewType] = useState<MockBundleRow['mockType']>('full');
  const [newSubtest, setNewSubtest] = useState('reading');
  const [newProvenance, setNewProvenance] = useState('Source: Admin-authored mock bundle assembled from published platform content.');
  const [newDifficulty, setNewDifficulty] = useState('exam_ready');
  const [newSourceStatus, setNewSourceStatus] = useState('needs_review');
  const [newQualityStatus, setNewQualityStatus] = useState('draft');
  const [newReleasePolicy, setNewReleasePolicy] = useState('instant');
  const [newTopicTags, setNewTopicTags] = useState('');
  const [newSkillTags, setNewSkillTags] = useState('');
  const [newWatermarkEnabled, setNewWatermarkEnabled] = useState(true);
  const [newRandomiseQuestions, setNewRandomiseQuestions] = useState(false);
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
        subtestCode: newType === 'sub' || newType === 'part' || newType === 'remedial' ? newSubtest : null,
        professionId: null,
        appliesToAllProfessions: true,
        sourceProvenance: newProvenance,
        priority: 0,
        tagsCsv: '',
        difficulty: newDifficulty,
        sourceStatus: newSourceStatus,
        qualityStatus: newQualityStatus,
        releasePolicy: newReleasePolicy,
        topicTagsCsv: newTopicTags,
        skillTagsCsv: newSkillTags,
        watermarkEnabled: newWatermarkEnabled,
        randomiseQuestions: newRandomiseQuestions,
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

          <div className="mb-6 flex flex-wrap gap-3">
            <Link
              href="/admin/content/mocks/item-analysis"
              className="inline-flex items-center rounded-xl border border-border bg-surface px-4 py-2 text-sm font-bold text-navy hover:bg-background-light"
            >
              <BarChart3 className="mr-2 h-4 w-4" /> Item analysis
            </Link>
            <Link
              href="/admin/content/mocks/operations"
              className="inline-flex items-center rounded-xl border border-border bg-surface px-4 py-2 text-sm font-bold text-navy hover:bg-background-light"
            >
              <CalendarClock className="mr-2 h-4 w-4" /> Mock operations
            </Link>
          </div>

          <div className="mb-6 grid gap-4 rounded-2xl border border-border bg-background-light p-4 lg:grid-cols-[1.2fr_0.8fr]">
            <div className="space-y-3">
              <p className="text-xs font-black uppercase tracking-widest text-muted">Create bundle</p>
              <div className="grid gap-3 md:grid-cols-2">
                <Input label="Title" value={newTitle} onChange={(e) => setNewTitle(e.target.value)} placeholder="Full Mock Route 1" />
                <Input label="Provenance" value={newProvenance} onChange={(e) => setNewProvenance(e.target.value)} />
                <Input label="Topic tags" value={newTopicTags} onChange={(e) => setNewTopicTags(e.target.value)} placeholder="cardiology, discharge" />
                <Input label="Skill tags" value={newSkillTags} onChange={(e) => setNewSkillTags(e.target.value)} placeholder="inference, purpose, fluency" />
              </div>
              <div className="flex flex-wrap gap-2">
                {(['full', 'lrw', 'sub', 'part', 'diagnostic', 'final_readiness', 'remedial'] as const).map((type) => (
                  <Button key={type} variant={newType === type ? 'primary' : 'secondary'} onClick={() => setNewType(type)}>
                    {type.replace(/_/g, ' ')}
                  </Button>
                ))}
                {newType === 'sub' || newType === 'part' || newType === 'remedial' ? (
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
              <div className="grid gap-3 md:grid-cols-4">
                <label className="text-xs font-black uppercase tracking-widest text-muted">
                  Difficulty
                  <select value={newDifficulty} onChange={(e) => setNewDifficulty(e.target.value)} className="mt-1 w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm normal-case tracking-normal">
                    <option value="foundation">Foundation</option>
                    <option value="developing">Developing</option>
                    <option value="exam_ready">Exam ready</option>
                    <option value="stretch">Stretch</option>
                  </select>
                </label>
                <label className="text-xs font-black uppercase tracking-widest text-muted">
                  Source status
                  <select value={newSourceStatus} onChange={(e) => setNewSourceStatus(e.target.value)} className="mt-1 w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm normal-case tracking-normal">
                    <option value="needs_review">Needs review</option>
                    <option value="original">Original</option>
                    <option value="licensed">Licensed</option>
                    <option value="official_sample">Official sample</option>
                  </select>
                </label>
                <label className="text-xs font-black uppercase tracking-widest text-muted">
                  QA status
                  <select value={newQualityStatus} onChange={(e) => setNewQualityStatus(e.target.value)} className="mt-1 w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm normal-case tracking-normal">
                    <option value="draft">Draft</option>
                    <option value="in_review">In review</option>
                    <option value="approved">Approved</option>
                    <option value="pilot">Pilot</option>
                    <option value="retired">Retired</option>
                  </select>
                </label>
                <label className="text-xs font-black uppercase tracking-widest text-muted">
                  Release policy
                  <select value={newReleasePolicy} onChange={(e) => setNewReleasePolicy(e.target.value)} className="mt-1 w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm normal-case tracking-normal">
                    <option value="instant">Instant</option>
                    <option value="after_teacher_marking">After teacher marking</option>
                    <option value="scheduled">Scheduled</option>
                  </select>
                </label>
              </div>
              <div className="grid gap-3 md:grid-cols-2">
                <label className="flex items-center gap-3 rounded-xl border border-border bg-surface px-3 py-2 text-sm font-semibold text-navy">
                  <input
                    type="checkbox"
                    checked={newWatermarkEnabled}
                    onChange={(e) => setNewWatermarkEnabled(e.target.checked)}
                    className="h-4 w-4 rounded border-border"
                  />
                  Watermark learner player/report
                </label>
                <label className="flex items-center gap-3 rounded-xl border border-border bg-surface px-3 py-2 text-sm font-semibold text-navy">
                  <input
                    type="checkbox"
                    checked={newRandomiseQuestions}
                    onChange={(e) => setNewRandomiseQuestions(e.target.checked)}
                    className="h-4 w-4 rounded border-border"
                  />
                  Randomise question order with seed
                </label>
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
                <option value="lrw">LRW</option>
                <option value="sub">Sub-test</option>
                <option value="part">Part</option>
                <option value="diagnostic">Diagnostic</option>
                <option value="final_readiness">Final readiness</option>
                <option value="remedial">Remedial</option>
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
                        {row.qualityStatus ? <Badge variant={row.qualityStatus === 'approved' ? 'success' : row.qualityStatus === 'draft' ? 'muted' : 'warning'}>{row.qualityStatus}</Badge> : null}
                      </div>
                      <p className="mt-1 font-mono text-xs text-muted">{row.id}</p>
                      <p className="mt-2 text-xs text-muted">
                        {row.sections.length} section{row.sections.length === 1 ? '' : 's'} / {row.estimatedDurationMinutes} min / provenance {row.sourceProvenance ? 'present' : 'missing'}
                      </p>
                      <p className="mt-1 text-xs text-muted">
                        Source {row.sourceStatus ?? 'needs_review'} / Release {row.releasePolicy ?? 'instant'} / Difficulty {row.difficulty ?? 'exam_ready'}
                      </p>
                      {(row.topicTagsCsv || row.skillTagsCsv) ? (
                        <p className="mt-1 text-xs text-muted">Tags: {row.topicTagsCsv} {row.skillTagsCsv}</p>
                      ) : null}
                    </div>
                    <div className="flex flex-wrap gap-2">
                      <Link
                        href={`/admin/content/mocks/${encodeURIComponent(row.id)}/item-analysis`}
                        className="inline-flex items-center rounded-lg border border-border px-3 py-2 text-sm font-medium text-navy hover:bg-background-light"
                      >
                        <BarChart3 className="mr-1 h-4 w-4" /> Analysis
                      </Link>
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
