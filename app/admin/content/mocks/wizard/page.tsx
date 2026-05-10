'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { Loader2, Plus, Sparkles } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { createAdminMockBundle, fetchAdminMockBundles } from '@/lib/api';

type DraftRow = {
  id: string;
  title: string;
  status: string;
  mockType?: string;
  updatedAt?: string;
  sections?: Array<{ id: string }>;
};

export default function MockWizardLandingPage() {
  const router = useRouter();
  const [drafts, setDrafts] = useState<DraftRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const resp = (await fetchAdminMockBundles({ status: 'draft' })) as { items?: DraftRow[] };
      setDrafts(resp.items ?? []);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load drafts.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  async function handleStart() {
    setCreating(true);
    try {
      const created = (await createAdminMockBundle({
        title: 'New mock (draft)',
        mockType: 'full',
        appliesToAllProfessions: true,
        sourceProvenance: 'Draft mock builder wizard seed; review source provenance before publish.',
        priority: 0,
        difficulty: 'exam_ready',
        sourceStatus: 'needs_review',
        qualityStatus: 'draft',
        releasePolicy: 'instant',
        topicTagsCsv: '',
        skillTagsCsv: '',
        watermarkEnabled: true,
        randomiseQuestions: false,
      })) as { id?: string };
      if (created?.id) {
        router.push(`/admin/content/mocks/wizard/${created.id}/bundle`);
      } else {
        await load();
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not create draft.');
    } finally {
      setCreating(false);
    }
  }

  return (
    <AdminRouteWorkspace>
      <AdminRoutePanel>
        <AdminRouteSectionHeader
          eyebrow="Content"
          title="Mock Builder Wizard"
          description="Build one OET mock end-to-end — bundle metadata, then Listening, Reading, Writing, Speaking, then publish."
          icon={Sparkles}
          actions={
            <Button variant="primary" onClick={handleStart} disabled={creating}>
              {creating ? (
                <Loader2 className="mr-1 h-4 w-4 animate-spin" />
              ) : (
                <Plus className="mr-1 h-4 w-4" />
              )}
              Start new mock
            </Button>
          }
        />

        {error ? (
          <div className="mt-4">
            <InlineAlert variant="error">{error}</InlineAlert>
          </div>
        ) : null}

        <section className="mt-6">
          <h2 className="text-sm font-bold uppercase tracking-wider text-muted">Resume drafts</h2>
          {loading ? (
            <p className="mt-3 inline-flex items-center gap-2 text-sm text-muted">
              <Loader2 className="h-4 w-4 animate-spin" /> Loading…
            </p>
          ) : drafts.length === 0 ? (
            <p className="mt-3 rounded-2xl border border-dashed border-border bg-background-light p-6 text-center text-sm text-muted">
              No draft mocks yet. Click <strong>Start new mock</strong> above.
            </p>
          ) : (
            <ul className="mt-3 space-y-2">
              {drafts.map((d) => (
                <li
                  key={d.id}
                  className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-surface px-4 py-3"
                >
                  <div>
                    <p className="text-sm font-bold text-navy">{d.title}</p>
                    <p className="text-xs text-muted">
                      {d.mockType ?? 'full'} · {d.sections?.length ?? 0} sections
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    <Badge variant="muted">{d.status}</Badge>
                    <Link
                      href={`/admin/content/mocks/wizard/${d.id}/bundle`}
                      className="inline-flex items-center rounded-xl border border-border bg-surface px-3 py-1.5 text-xs font-bold text-navy hover:bg-background-light"
                    >
                      Resume
                    </Link>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
