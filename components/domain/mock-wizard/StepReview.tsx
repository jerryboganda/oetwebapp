'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Loader2, Rocket } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { publishAdminMockBundle } from '@/lib/api';
import { fetchAdminContentPaper, getRequiredRoles, publishPaper } from '@/lib/mock-wizard/api';
import type { ContentPaperDto, PaperAssetRole } from '@/lib/content-upload-api';
import { useWizard } from './WizardShell';
import { PublishGateChecklist, type PublishGateRow } from './PublishGateChecklist';

type SubtestKey = 'listening' | 'reading' | 'writing' | 'speaking';
const SUBTEST_KEYS: SubtestKey[] = ['listening', 'reading', 'writing', 'speaking'];

interface PaperSnapshot {
  paper: ContentPaperDto;
  required: PaperAssetRole[];
}

export function StepReview() {
  const { bundle, refreshBundle, registerCanAdvance, registerStepSubmit } = useWizard();
  const router = useRouter();
  const [snapshots, setSnapshots] = useState<Partial<Record<SubtestKey, PaperSnapshot>>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [blockedAt, setBlockedAt] = useState<SubtestKey | null>(null);
  const [publishing, setPublishing] = useState(false);

  // The review step does not "advance" — disable the Next button.
  useEffect(() => {
    registerCanAdvance('review', false);
    registerStepSubmit('review', null);
  }, [registerCanAdvance, registerStepSubmit]);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setLoading(true);
      try {
        const next: Partial<Record<SubtestKey, PaperSnapshot>> = {};
        for (const k of SUBTEST_KEYS) {
          const section = bundle.sections.find((s) => s.subtestCode === k);
          if (!section) continue;
          const [paper, requiredResp] = await Promise.all([
            fetchAdminContentPaper(section.contentPaperId),
            getRequiredRoles(k),
          ]);
          next[k] = { paper, required: requiredResp.required };
        }
        if (!cancelled) setSnapshots(next);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load review snapshot.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }
    void load();
    return () => {
      cancelled = true;
    };
  }, [bundle.sections]);

  const checklists = useMemo(() => {
    return SUBTEST_KEYS.map((key) => {
      const snap = snapshots[key];
      const rows: PublishGateRow[] = [];
      rows.push({ label: `${key} paper exists`, ok: Boolean(snap) });
      if (snap) {
        const attached = new Set(
          (snap.paper.assets ?? []).filter((a) => a.isPrimary).map((a) => a.role),
        );
        for (const role of snap.required) {
          rows.push({ label: `${role} attached`, ok: attached.has(role) });
        }
        rows.push({
          label: 'Source provenance set',
          ok: Boolean(snap.paper.sourceProvenance && snap.paper.sourceProvenance.trim().length > 0),
        });
        rows.push({
          label: 'Paper not archived',
          ok: snap.paper.status !== 'Archived',
          detail: snap.paper.status,
        });
      }
      return { key, rows, allGreen: rows.every((r) => r.ok) };
    });
  }, [snapshots]);

  const allReady =
    !loading &&
    checklists.every((c) => c.allGreen) &&
    Boolean(bundle.sourceProvenance && bundle.sourceProvenance.trim().length > 0);

  const handlePublish = useCallback(async () => {
    if (publishing) return;
    setPublishing(true);
    setBlockedAt(null);
    setError(null);
    try {
      for (const c of checklists) {
        const snap = snapshots[c.key];
        if (!snap) continue;
        if (snap.paper.status === 'Published') continue;
        try {
          await publishPaper(snap.paper.id);
        } catch (err) {
          setBlockedAt(c.key);
          setError(
            err instanceof Error
              ? `Publishing ${c.key} paper failed: ${err.message}. Earlier subtests may already be published — the page will refresh so you can retry from the right spot.`
              : `Publishing ${c.key} paper failed.`,
          );
          // Refresh bundle + paper snapshots so the UI reflects what published
          // before the failure. Without this, subsequent retry attempts skip
          // already-published papers but the checklist still shows them as Draft.
          try { await refreshBundle(); } catch { /* non-fatal */ }
          return;
        }
      }
      await publishAdminMockBundle(bundle.id);
      await refreshBundle();
      router.push(`/admin/content/mocks/${bundle.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Bundle publish failed.');
      try { await refreshBundle(); } catch { /* non-fatal */ }
    } finally {
      setPublishing(false);
    }
  }, [bundle.id, checklists, publishing, refreshBundle, router, snapshots]);

  return (
    <div className="space-y-6">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Step 6 — Review &amp; publish</h2>
        <p className="text-sm text-muted">
          Each subtest must satisfy the publish gate before the bundle can release. Publish runs
          per-paper first, then the bundle.
        </p>
      </header>

      {error ? (
        <InlineAlert variant="error" title={blockedAt ? `Blocked at ${blockedAt}` : undefined}>
          {error}
        </InlineAlert>
      ) : null}

      {loading ? (
        <p className="inline-flex items-center gap-2 text-sm text-muted">
          <Loader2 className="h-4 w-4 animate-spin" /> Loading paper snapshots…
        </p>
      ) : (
        <div className="grid gap-3 md:grid-cols-2">
          {checklists.map((c) => (
            <PublishGateChecklist
              key={c.key}
              title={`${c.key.charAt(0).toUpperCase()}${c.key.slice(1)} publish gate`}
              rows={c.rows}
            />
          ))}
        </div>
      )}

      <PublishGateChecklist
        title="Bundle"
        rows={[
          {
            label: 'Bundle source provenance',
            ok: Boolean(bundle.sourceProvenance && bundle.sourceProvenance.trim().length > 0),
          },
          { label: 'All four subtests authored', ok: checklists.every((c) => Boolean(snapshots[c.key])) },
        ]}
      />

      <div className="flex items-center justify-end">
        <Button
          variant="primary"
          onClick={() => void handlePublish()}
          disabled={!allReady || publishing}
        >
          {publishing ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : <Rocket className="mr-1 h-4 w-4" />}
          Publish bundle
        </Button>
      </div>
    </div>
  );
}
