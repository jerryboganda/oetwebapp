'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, Download, ShieldCheck } from 'lucide-react';
import { AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import { NoBillingPermission } from '@/components/admin/billing/no-billing-permission';
import { fetchEligibilityMatrix } from '@/lib/api';
import type { EligibilityMatrixResponse, EligibilityMatrixRow } from '@/lib/types/admin';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';

type LoadState = 'loading' | 'success' | 'error';

type ProfessionFilter = 'all' | 'medicine' | 'nursing' | 'pharmacy';

type CategoryFilter = 'all' | string;

export default function AdminEligibilityMatrixPage() {
  const { user } = useAuth();
  const canReadBilling = hasPermission(
    user?.adminPermissions,
    AdminPermission.BillingRead,
    AdminPermission.BillingWrite,
  );

  const [data, setData] = useState<EligibilityMatrixResponse | null>(null);
  const [status, setStatus] = useState<LoadState>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const [professionFilter, setProfessionFilter] = useState<ProfessionFilter>('all');
  const [categoryFilter, setCategoryFilter] = useState<CategoryFilter>('all');
  const [showDraft, setShowDraft] = useState(true);
  const [showHidden, setShowHidden] = useState(false);

  const load = useCallback(async () => {
    if (!canReadBilling) return;
    setStatus('loading');
    setErrorMessage(null);
    try {
      const response = await fetchEligibilityMatrix();
      setData(response);
      setStatus('success');
    } catch (err) {
      console.error('Failed to load eligibility matrix', err);
      setErrorMessage(err instanceof Error ? err.message : 'Failed to load eligibility matrix.');
      setStatus('error');
    }
  }, [canReadBilling]);

  useEffect(() => {
    void load();
  }, [load]);

  const filteredRows = useMemo<EligibilityMatrixRow[]>(() => {
    if (!data?.plans) return [];
    return data.plans.filter((row) => {
      if (!showDraft && row.isDraft) return false;
      if (!showHidden && !row.isVisible) return false;
      if (professionFilter !== 'all' && row.profession !== professionFilter && row.profession !== 'all') return false;
      if (categoryFilter !== 'all' && row.productCategory !== categoryFilter) return false;
      return true;
    });
  }, [data, professionFilter, categoryFilter, showDraft, showHidden]);

  const categories = useMemo<string[]>(() => {
    if (!data?.plans) return [];
    const set = new Set<string>();
    data.plans.forEach((row) => set.add(row.productCategory));
    return Array.from(set).sort();
  }, [data]);

  const handleExportCsv = () => {
    if (!data?.plans) return;
    const header = 'code,name,profession,category,is_draft,is_visible,writing_addons,speaking_addons,tutor_book_discount,eligible_addons';
    const rows = filteredRows.map((row) => [
      csvEscape(row.code),
      csvEscape(row.name),
      csvEscape(row.profession),
      csvEscape(row.productCategory),
      row.isDraft ? 'true' : 'false',
      row.isVisible ? 'true' : 'false',
      row.writingAddonsEnabled ? 'true' : 'false',
      row.speakingAddonsEnabled ? 'true' : 'false',
      row.tutorBookDiscountEnabled ? 'true' : 'false',
      csvEscape(row.eligibleAddOnCodes.join('|')),
    ].join(','));
    const blob = new Blob([[header, ...rows].join('\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `oet-2026-eligibility-matrix-${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  };

  if (!user) return null;
  if (!canReadBilling) return <NoBillingPermission />;

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        eyebrow="Billing"
        title="OET 2026 — Add-on Eligibility Matrix"
        description="Audit the three independent eligibility flags (writing_addons, speaking_addons, tutor_book_discount) across every plan, and inspect which add-on SKUs each plan unlocks."
        icon={<ShieldCheck aria-hidden className="h-5 w-5" />}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Button asChild variant="ghost" size="sm">
              <Link href="/admin/billing">
                <ArrowLeft className="mr-1.5 h-4 w-4" /> Back to Billing
              </Link>
            </Button>
            <Button onClick={handleExportCsv} size="sm" disabled={status !== 'success'}>
              <Download className="mr-1.5 h-4 w-4" /> Export CSV
            </Button>
          </div>
        }
      />

      <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm dark:border-slate-800 dark:bg-slate-950">
        <div className="grid grid-cols-1 gap-3 md:grid-cols-4">
          <FilterSelect
            label="Profession"
            value={professionFilter}
            options={[
              { value: 'all', label: 'All professions' },
              { value: 'medicine', label: 'Medicine' },
              { value: 'nursing', label: 'Nursing' },
              { value: 'pharmacy', label: 'Pharmacy' },
            ]}
            onChange={(v) => setProfessionFilter(v as ProfessionFilter)}
          />
          <FilterSelect
            label="Category"
            value={categoryFilter}
            options={[
              { value: 'all', label: 'All categories' },
              ...categories.map((c) => ({ value: c, label: c })),
            ]}
            onChange={(v) => setCategoryFilter(v)}
          />
          <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-700 dark:text-slate-300">
            <input type="checkbox" checked={showDraft} onChange={(e) => setShowDraft(e.target.checked)} />
            Show drafts
          </label>
          <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-700 dark:text-slate-300">
            <input type="checkbox" checked={showHidden} onChange={(e) => setShowHidden(e.target.checked)} />
            Show hidden
          </label>
        </div>
      </div>

      {status === 'loading' ? (
        <div className="space-y-2">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
        </div>
      ) : status === 'error' ? (
        <InlineAlert variant="error" title="Could not load matrix">{errorMessage}</InlineAlert>
      ) : (
        <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm dark:border-slate-800 dark:bg-slate-950">
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-slate-200 text-sm dark:divide-slate-800">
              <thead className="bg-slate-50 text-left text-xs font-medium uppercase tracking-wide text-slate-500 dark:bg-slate-900 dark:text-slate-400">
                <tr>
                  <th className="px-4 py-3">Plan</th>
                  <th className="px-4 py-3">Profession</th>
                  <th className="px-4 py-3">Category</th>
                  <th className="px-4 py-3 text-center">W</th>
                  <th className="px-4 py-3 text-center">S</th>
                  <th className="px-4 py-3 text-center">TB £32</th>
                  <th className="px-4 py-3">Eligible add-ons</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
                {filteredRows.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="px-4 py-8 text-center text-slate-500 dark:text-slate-400">
                      No plans match the current filters.
                    </td>
                  </tr>
                ) : (
                  filteredRows.map((row) => (
                    <tr key={row.code} className="hover:bg-slate-50/60 dark:hover:bg-slate-900/40">
                      <td className="px-4 py-3">
                        <div className="font-medium text-slate-900 dark:text-slate-100">{row.name}</div>
                        <div className="text-xs text-slate-500 dark:text-slate-400">
                          <code>{row.code}</code>
                          {row.isDraft && <Badge variant="outline" className="ml-2 text-amber-600 border-amber-400">draft</Badge>}
                          {!row.isVisible && <Badge variant="outline" className="ml-2 text-muted-foreground">hidden</Badge>}
                        </div>
                      </td>
                      <td className="px-4 py-3 capitalize text-slate-700 dark:text-slate-300">{row.profession}</td>
                      <td className="px-4 py-3 text-slate-700 dark:text-slate-300">{row.productCategory}</td>
                      <td className="px-4 py-3 text-center">
                        <FlagDot enabled={row.writingAddonsEnabled} />
                      </td>
                      <td className="px-4 py-3 text-center">
                        <FlagDot enabled={row.speakingAddonsEnabled} />
                      </td>
                      <td className="px-4 py-3 text-center">
                        <FlagDot enabled={row.tutorBookDiscountEnabled} />
                      </td>
                      <td className="px-4 py-3">
                        {row.eligibleAddOnCodes.length === 0 ? (
                          <span className="text-xs text-slate-400">—</span>
                        ) : (
                          <div className="flex flex-wrap gap-1">
                            {row.eligibleAddOnCodes.map((code) => (
                              <Badge key={code} variant="outline" className="text-[10px]">
                                {code}
                              </Badge>
                            ))}
                          </div>
                        )}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
          <div className="border-t border-slate-200 bg-slate-50 px-4 py-2 text-xs text-slate-500 dark:border-slate-800 dark:bg-slate-900 dark:text-slate-400">
            {filteredRows.length} of {data?.plans.length ?? 0} plans • Gold dot = enabled, grey = hidden (matches the PDF design language)
          </div>
        </div>
      )}
    </AdminRouteWorkspace>
  );
}

function FlagDot({ enabled }: { enabled: boolean }) {
  return (
    <span
      aria-label={enabled ? 'enabled' : 'disabled'}
      className={`inline-block h-3 w-3 rounded-full ${
        enabled ? 'bg-amber-400 ring-2 ring-amber-100' : 'bg-slate-200 dark:bg-slate-700'
      }`}
    />
  );
}

function FilterSelect({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: string;
  options: Array<{ value: string; label: string }>;
  onChange: (v: string) => void;
}) {
  return (
    <label className="flex flex-col gap-1 text-xs font-medium uppercase tracking-wide text-slate-500 dark:text-slate-400">
      {label}
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="rounded-md border border-slate-200 bg-white px-2 py-1.5 text-sm font-normal normal-case text-slate-900 shadow-sm dark:border-slate-700 dark:bg-slate-900 dark:text-slate-100"
      >
        {options.map((opt) => (
          <option key={opt.value} value={opt.value}>
            {opt.label}
          </option>
        ))}
      </select>
    </label>
  );
}

function csvEscape(value: string): string {
  if (value.includes(',') || value.includes('"') || value.includes('\n')) {
    return `"${value.replace(/"/g, '""')}"`;
  }
  return value;
}
