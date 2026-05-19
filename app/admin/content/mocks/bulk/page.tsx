'use client';

/**
 * Bulk mock-bundle generator (SUBAGENT_E).
 *
 * UI replacement for `scripts/admin/generate-mocks.mjs`:
 *   - matrix: rows = professions (multi-select), columns = difficulty (easy/medium/hard),
 *     cell = number of Full bundles to create with that pair.
 *   - source mode: "auto-select published papers" (live) OR
 *     "generate new papers via CLI" (informational — shows the CLI command).
 *   - per-bundle, the UI mirrors the script flow:
 *       POST /v1/admin/mock-bundles
 *       POST /v1/admin/mock-bundles/{id}/sections  ×4
 *       POST /v1/admin/mock-bundles/{id}/publish
 *
 * Backend already exposes every endpoint we need; no new contracts required.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Copy } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { BulkRunner } from '@/components/admin/bulk-runner/BulkRunner';
import {
  adminBulkListPapers,
  adminBulkListMockBundles,
  addAdminMockBundleSection,
  createAdminMockBundle,
  publishAdminMockBundle,
  type AdminBulkPaperRow,
} from '@/lib/api';
import type {
  BulkDifficulty,
  BulkMockMatrixCell,
  BulkMockPlanItem,
  BulkSubtest,
} from '@/lib/types/admin/bulk-ops';

const PROFESSIONS = [
  'medicine', 'nursing', 'dentistry', 'pharmacy',
  'physiotherapy', 'occupational-therapy', 'optometry', 'podiatry',
  'dietetics', 'radiography', 'speech-pathology', 'veterinary',
];

const DIFFICULTIES: BulkDifficulty[] = ['easy', 'medium', 'hard'];
const SUBTESTS: BulkSubtest[] = ['listening', 'reading', 'writing', 'speaking'];
const TITLE_PREFIX = 'OET Full Mock #';

type SourceMode = 'auto' | 'cli';

/** Per-subtest pool of published papers, keyed by subtest. */
type PaperPool = Record<BulkSubtest, AdminBulkPaperRow[]>;

function emptyPool(): PaperPool {
  return { listening: [], reading: [], writing: [], speaking: [] };
}

/**
 * Resolve a candidate paper for a (subtest, profession) pair, preferring
 * a strict profession match, then appliesToAllProfessions, then any unused.
 * Mirrors `pickPaper` in generate-mocks.mjs.
 */
function pickPaper(
  pool: AdminBulkPaperRow[],
  profession: string,
  used: Set<string>,
): AdminBulkPaperRow | null {
  const strict = pool.find(p => !used.has(p.id) && p.professionId === profession);
  if (strict) return strict;
  const allProf = pool.find(p => !used.has(p.id) && p.appliesToAllProfessions);
  if (allProf) return allProf;
  const any = pool.find(p => !used.has(p.id));
  if (any) return any;
  // Last resort: reuse a profession-compatible paper.
  return pool.find(p => p.professionId === profession || p.appliesToAllProfessions) ?? pool[0] ?? null;
}

export default function MocksBulkPage() {
  const [sourceMode, setSourceMode] = useState<SourceMode>('auto');
  const [matrix, setMatrix] = useState<Record<string, Record<BulkDifficulty, number>>>(() => {
    const init: Record<string, Record<BulkDifficulty, number>> = {};
    for (const p of PROFESSIONS) init[p] = { easy: 0, medium: 0, hard: 0 };
    return init;
  });
  const [pool, setPool] = useState<PaperPool>(emptyPool());
  const [loading, setLoading] = useState(true);
  const [poolError, setPoolError] = useState<string | null>(null);
  const [alreadyHave, setAlreadyHave] = useState(0);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  // Load published-paper pool per subtest + count existing Full bundles.
  const refreshPool = useCallback(async () => {
    setLoading(true);
    setPoolError(null);
    try {
      const [l, r, w, s, bundles] = await Promise.all([
        adminBulkListPapers({ subtest: 'listening', status: 'Published', pageSize: 200 }),
        adminBulkListPapers({ subtest: 'reading', status: 'Published', pageSize: 200 }),
        adminBulkListPapers({ subtest: 'writing', status: 'Published', pageSize: 200 }),
        adminBulkListPapers({ subtest: 'speaking', status: 'Published', pageSize: 200 }),
        adminBulkListMockBundles({ mockType: 'full' }),
      ]);
      setPool({ listening: l, reading: r, writing: w, speaking: s });
      setAlreadyHave(
        (Array.isArray(bundles) ? bundles : []).filter(b => typeof b.title === 'string' && b.title.startsWith(TITLE_PREFIX)).length,
      );
    } catch (e) {
      setPoolError(e instanceof Error ? e.message : 'Failed to load published-paper pool.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void refreshPool(); }, [refreshPool]);

  const cells: BulkMockMatrixCell[] = useMemo(() => {
    const out: BulkMockMatrixCell[] = [];
    for (const profession of PROFESSIONS) {
      for (const difficulty of DIFFICULTIES) {
        const count = matrix[profession]?.[difficulty] ?? 0;
        if (count > 0) out.push({ profession, difficulty, count });
      }
    }
    return out;
  }, [matrix]);

  const plan: BulkMockPlanItem[] = useMemo(() => {
    const items: BulkMockPlanItem[] = [];
    let ord = alreadyHave + 1;
    for (const cell of cells) {
      for (let i = 0; i < cell.count; i++) {
        const title = `${TITLE_PREFIX}${ord} — ${cell.profession} (${cell.difficulty})`;
        items.push({
          id: `plan-${cell.profession}-${cell.difficulty}-${ord}`,
          ordinal: ord,
          title,
          profession: cell.profession,
          difficulty: cell.difficulty,
        });
        ord++;
      }
    }
    return items;
  }, [cells, alreadyHave]);

  const setCell = (profession: string, difficulty: BulkDifficulty, value: number) => {
    setMatrix(prev => ({
      ...prev,
      [profession]: { ...prev[profession], [difficulty]: Math.max(0, Math.floor(value || 0)) },
    }));
  };

  // Track papers consumed across the run so we spread the pool for variety.
  // Lives outside React state because the BulkRunner workers mutate it directly.
  // Resets whenever the plan identity changes (new matrix => new pool of work).
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const usedRef = useMemo(() => ({ current: new Set<string>() }), [plan]);

  const cliCommand = useMemo(() => {
    const total = plan.length;
    return `node scripts/admin/generate-mocks.mjs --count ${total}`;
  }, [plan.length]);

  const totalToCreate = plan.length;

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="Bulk-generate Full Mock bundles"
        description="Compose published L/R/W/S papers into Full mocks. Mirrors scripts/admin/generate-mocks.mjs but with full progress visibility."
      />

      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}

      <AdminRoutePanel>
        <AdminRouteSectionHeader title="Source mode" />
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            size="sm"
            variant={sourceMode === 'auto' ? 'primary' : 'outline'}
            onClick={() => setSourceMode('auto')}
          >
            Auto-select existing published papers
          </Button>
          <Button
            type="button"
            size="sm"
            variant={sourceMode === 'cli' ? 'primary' : 'outline'}
            onClick={() => setSourceMode('cli')}
          >
            Generate new papers via CLI (informational)
          </Button>
        </div>
        {sourceMode === 'cli' && (
          <div className="mt-3 space-y-2">
            <InlineAlert variant="info">
              Bundle creation always runs through the backend API. To generate fresh papers first,
              run the CLI generators on the VPS, then return here and use “Auto-select”.
            </InlineAlert>
            <div className="flex items-center gap-2">
              <code className="block flex-1 rounded bg-slate-100 px-3 py-2 text-xs dark:bg-slate-900">
                node scripts/admin/generate-listening.mjs &amp;&amp; node scripts/admin/generate-reading.mjs &amp;&amp; node scripts/admin/generate-writing.mjs &amp;&amp; node scripts/admin/generate-speaking.mjs
              </code>
              <Button
                type="button"
                size="sm"
                variant="ghost"
                onClick={() => {
                  void navigator.clipboard?.writeText(
                    'node scripts/admin/generate-listening.mjs && node scripts/admin/generate-reading.mjs && node scripts/admin/generate-writing.mjs && node scripts/admin/generate-speaking.mjs',
                  );
                  setToast({ variant: 'success', message: 'CLI command copied.' });
                }}
              >
                <Copy className="h-4 w-4" />
              </Button>
            </div>
            <p className="text-xs text-slate-500">
              The matrix below still runs against the live API to compose bundles from whatever published papers exist.
            </p>
          </div>
        )}
      </AdminRoutePanel>

      <AdminRoutePanel>
        <AdminRouteSectionHeader
          title="Matrix"
          description={
            loading
              ? 'Loading published-paper pool…'
              : `Pool: L=${pool.listening.length} · R=${pool.reading.length} · W=${pool.writing.length} · S=${pool.speaking.length}. Existing Full bundles: ${alreadyHave}.`
          }
        />
        {poolError && <InlineAlert variant="error">{poolError}</InlineAlert>}
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs uppercase text-slate-500">
                <th className="px-2 py-2">Profession</th>
                {DIFFICULTIES.map(d => (
                  <th key={d} className="px-2 py-2">{d}</th>
                ))}
                <th className="px-2 py-2">Row total</th>
              </tr>
            </thead>
            <tbody>
              {PROFESSIONS.map(p => {
                const row = matrix[p] ?? { easy: 0, medium: 0, hard: 0 };
                const rowTotal = DIFFICULTIES.reduce((acc, d) => acc + (row[d] || 0), 0);
                return (
                  <tr key={p} className="border-t border-slate-100 dark:border-slate-800">
                    <td className="px-2 py-2 font-medium">{p}</td>
                    {DIFFICULTIES.map(d => (
                      <td key={d} className="px-2 py-2">
                        <input
                          type="number"
                          min={0}
                          value={row[d]}
                          onChange={(e) => setCell(p, d, Number(e.target.value))}
                          className="w-20 rounded border border-slate-300 px-2 py-1 text-sm dark:border-slate-700 dark:bg-slate-900"
                          aria-label={`${p} ${d} count`}
                        />
                      </td>
                    ))}
                    <td className="px-2 py-2 text-xs text-slate-600">{rowTotal}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <div className="mt-3 text-sm text-slate-600 dark:text-slate-300">
          Planned bundles: <strong>{totalToCreate}</strong>
        </div>
      </AdminRoutePanel>

      <AdminRoutePanel>
        <AdminRouteSectionHeader
          title="Run"
          description="Each bundle: create → add 4 sections (L→R→W→S) → publish."
        />
        <BulkRunner
          items={plan}
          getKey={(p) => p.id}
          startLabel="Generate bundles"
          renderRow={(p) => (
            <div>
              <div className="font-medium">{p.title}</div>
              <div className="text-xs text-slate-500">{p.profession} · {p.difficulty}</div>
            </div>
          )}
          run={async (planItem) => {
            // Resolve all 4 papers BEFORE creating the bundle.
            const picks: Partial<Record<BulkSubtest, AdminBulkPaperRow>> = {};
            for (const subtest of SUBTESTS) {
              const paper = pickPaper(pool[subtest], planItem.profession, usedRef.current);
              if (!paper) {
                return {
                  ok: false,
                  error: `No published ${subtest} paper available for ${planItem.profession}.`,
                };
              }
              picks[subtest] = paper;
            }

            // Create bundle.
            let bundleId: string;
            try {
              const created = await createAdminMockBundle({
                title: planItem.title,
                mockType: 'full',
                subtestCode: null,
                professionId: planItem.profession,
                appliesToAllProfessions: false,
                sourceProvenance: `Admin bulk mock generator (UI) · ${new Date().toISOString()}`,
                priority: 0,
                tagsCsv: 'admin-bulk,full-mock',
                difficulty: planItem.difficulty,
              }) as { id?: string };
              if (!created?.id) {
                return { ok: false, error: 'createAdminMockBundle returned no id.' };
              }
              bundleId = created.id;
            } catch (e) {
              return { ok: false, error: e instanceof Error ? e.message : String(e) };
            }

            // Reserve picked papers once the bundle row exists.
            for (const subtest of SUBTESTS) {
              const picked = picks[subtest];
              if (picked) usedRef.current.add(picked.id);
            }

            // Add sections in canonical OET order.
            for (const subtest of SUBTESTS) {
              const paper = picks[subtest]!;
              try {
                await addAdminMockBundleSection(bundleId, { contentPaperId: paper.id });
              } catch (e) {
                return {
                  ok: false,
                  detail: `bundleId=${bundleId} (sections incomplete)`,
                  error: `add ${subtest} section failed: ${e instanceof Error ? e.message : String(e)}`,
                };
              }
            }

            // Publish.
            const warnings: string[] = [];
            try {
              const pub = await publishAdminMockBundle(bundleId) as { warnings?: string[] };
              if (Array.isArray(pub?.warnings)) warnings.push(...pub.warnings);
            } catch (e) {
              return {
                ok: false,
                detail: `bundleId=${bundleId} (created, not published)`,
                error: e instanceof Error ? e.message : String(e),
              };
            }

            return { ok: true, warnings, detail: `bundleId=${bundleId}` };
          }}
          onFinished={(summary) => {
            setToast({
              variant: summary.failed > 0 ? 'error' : 'success',
              message: `Done — ${summary.ok} ok, ${summary.failed} failed, ${summary.warnings} with warnings.`,
            });
            void refreshPool();
          }}
        />
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
