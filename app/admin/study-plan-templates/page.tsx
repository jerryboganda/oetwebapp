'use client';

import { useEffect, useState, useCallback } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  listStudyPlanTemplates,
  bulkStudyPlanTemplateAction,
  type StudyPlanTemplateListItem,
} from '@/lib/study-plan-admin-api';

type Filter = { tier: string; active: 'all' | 'active' | 'inactive' };

export default function StudyPlanTemplatesAdminPage() {
  const router = useRouter();
  const [rows, setRows] = useState<StudyPlanTemplateListItem[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>({ tier: '', active: 'all' });
  const [toast, setToast] = useState<string | null>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await listStudyPlanTemplates({
        tier: filter.tier || undefined,
        active: filter.active === 'all' ? undefined : filter.active === 'active',
      });
      setRows(data);
    } catch (e: any) {
      setError(e?.userMessage ?? e?.message ?? 'Failed to load templates.');
    } finally {
      setLoading(false);
    }
  }, [filter]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const toggleSelect = (id: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const selectAll = () => {
    if (selected.size === rows.length) {
      setSelected(new Set());
    } else {
      setSelected(new Set(rows.map((r) => r.id)));
    }
  };

  const runBulk = async (action: 'activate' | 'deactivate' | 'duplicate' | 'soft-delete') => {
    if (selected.size === 0) return;
    if (action === 'soft-delete' && !confirm(`Soft-delete ${selected.size} template(s)?`)) return;
    try {
      const result = await bulkStudyPlanTemplateAction(action, Array.from(selected));
      setToast(`${action}: ${result.processed} template(s) processed.`);
      setSelected(new Set());
      await reload();
    } catch (e: any) {
      setError(e?.userMessage ?? e?.message ?? 'Bulk action failed.');
    }
  };

  return (
    <div className="p-6 max-w-7xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold">Study Plan Templates</h1>
          <p className="text-sm text-gray-600 mt-1">
            Admin-authored skeletons the planner picks from for each learner. Tier-gated and
            profession-aware.
          </p>
        </div>
        <Link
          href="/admin/study-plan-templates/new"
          className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700"
        >
          + New Template
        </Link>
      </div>

      {/* Filters */}
      <div className="flex gap-3 mb-4 items-center">
        <select
          value={filter.tier}
          onChange={(e) => setFilter((f) => ({ ...f, tier: e.target.value }))}
          className="border rounded px-3 py-2"
        >
          <option value="">All tiers</option>
          <option value="free">Free</option>
          <option value="premium">Premium</option>
          <option value="elite">Elite</option>
        </select>
        <select
          value={filter.active}
          onChange={(e) => setFilter((f) => ({ ...f, active: e.target.value as Filter['active'] }))}
          className="border rounded px-3 py-2"
        >
          <option value="all">All statuses</option>
          <option value="active">Active only</option>
          <option value="inactive">Inactive only</option>
        </select>
        <button onClick={() => void reload()} className="px-3 py-2 border rounded">
          Refresh
        </button>
      </div>

      {/* Bulk toolbar */}
      {selected.size > 0 && (
        <div className="bg-blue-50 border border-blue-200 rounded p-3 mb-4 flex items-center gap-3">
          <span className="text-sm">{selected.size} selected</span>
          <button
            onClick={() => runBulk('activate')}
            className="px-3 py-1 bg-green-600 text-white rounded text-sm"
          >
            Activate
          </button>
          <button
            onClick={() => runBulk('deactivate')}
            className="px-3 py-1 bg-yellow-600 text-white rounded text-sm"
          >
            Deactivate
          </button>
          <button
            onClick={() => runBulk('duplicate')}
            className="px-3 py-1 bg-gray-600 text-white rounded text-sm"
          >
            Duplicate
          </button>
          <button
            onClick={() => runBulk('soft-delete')}
            className="px-3 py-1 bg-red-600 text-white rounded text-sm"
          >
            Soft-delete
          </button>
          <button
            onClick={() => setSelected(new Set())}
            className="px-3 py-1 text-sm underline"
          >
            Clear
          </button>
        </div>
      )}

      {toast && (
        <div className="bg-green-50 border border-green-200 text-green-800 rounded p-3 mb-4">
          {toast}
        </div>
      )}
      {error && (
        <div className="bg-red-50 border border-red-200 text-red-800 rounded p-3 mb-4">
          {error}
        </div>
      )}

      {loading ? (
        <div className="p-6 text-center text-gray-500">Loading...</div>
      ) : rows.length === 0 ? (
        <div className="p-12 text-center bg-gray-50 border rounded">
          <p className="text-gray-700">No templates match these filters.</p>
          <Link
            href="/admin/study-plan-templates/new"
            className="inline-block mt-3 px-4 py-2 bg-blue-600 text-white rounded"
          >
            Create your first template
          </Link>
        </div>
      ) : (
        <div className="border rounded overflow-x-auto">
          <table className="w-full">
            <thead className="bg-gray-50 text-left">
              <tr>
                <th className="p-3 w-10">
                  <input
                    type="checkbox"
                    checked={selected.size === rows.length}
                    onChange={selectAll}
                  />
                </th>
                <th className="p-3">Name</th>
                <th className="p-3">Slug</th>
                <th className="p-3">Weeks</th>
                <th className="p-3">Tiers</th>
                <th className="p-3">Profession</th>
                <th className="p-3">Band</th>
                <th className="p-3">Status</th>
                <th className="p-3">v</th>
                <th className="p-3"></th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id} className="border-t hover:bg-gray-50">
                  <td className="p-3">
                    <input
                      type="checkbox"
                      checked={selected.has(r.id)}
                      onChange={() => toggleSelect(r.id)}
                    />
                  </td>
                  <td className="p-3">
                    <button
                      onClick={() => router.push(`/admin/study-plan-templates/${r.id}`)}
                      className="text-blue-600 hover:underline text-left"
                    >
                      {r.name}
                    </button>
                    {r.description && (
                      <div className="text-xs text-gray-500 mt-1">{r.description}</div>
                    )}
                  </td>
                  <td className="p-3 text-sm font-mono">{r.slug}</td>
                  <td className="p-3 text-sm">
                    {r.minWeeks}–{r.maxWeeks}
                  </td>
                  <td className="p-3 text-sm">
                    {r.tierCodes.length === 0 ? (
                      <span className="text-gray-400">none</span>
                    ) : (
                      r.tierCodes.map((t) => (
                        <span
                          key={t}
                          className="inline-block px-2 py-0.5 bg-gray-100 rounded text-xs mr-1"
                        >
                          {t}
                        </span>
                      ))
                    )}
                  </td>
                  <td className="p-3 text-sm">{r.professionId ?? <span className="text-gray-400">any</span>}</td>
                  <td className="p-3 text-sm">{r.targetBand ?? <span className="text-gray-400">any</span>}</td>
                  <td className="p-3">
                    {r.isActive ? (
                      <span className="px-2 py-0.5 bg-green-100 text-green-800 rounded text-xs">Active</span>
                    ) : (
                      <span className="px-2 py-0.5 bg-gray-100 text-gray-600 rounded text-xs">Inactive</span>
                    )}
                  </td>
                  <td className="p-3 text-sm text-gray-500">{r.version}</td>
                  <td className="p-3">
                    <Link
                      href={`/admin/study-plan-templates/${r.id}`}
                      className="text-sm text-blue-600 hover:underline"
                    >
                      Edit
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
