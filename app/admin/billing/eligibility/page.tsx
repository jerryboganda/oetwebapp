'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, Download, ShieldCheck } from 'lucide-react';

import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Label } from '@/components/admin/ui/label';
import { PageHeader } from '@/components/admin/ui/page-header';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/admin/ui/select';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Switch } from '@/components/admin/ui/switch';
import { InlineAlert } from '@/components/ui/alert';
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
    const header =
      'code,name,profession,category,is_draft,is_visible,writing_addons,speaking_addons,tutor_book_discount,eligible_addons';
    const rows = filteredRows.map((row) =>
      [
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
      ].join(','),
    );
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
    <AdminPageShell>
      <PageHeader
        eyebrow="Billing"
        title="OET 2026 — Add-on Eligibility Matrix"
        description="Audit the three independent eligibility flags (writing_addons, speaking_addons, tutor_book_discount) across every plan, and inspect which add-on SKUs each plan unlocks."
        icon={<ShieldCheck aria-hidden className="h-5 w-5" />}
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Billing', href: '/admin/billing' },
          { label: 'Eligibility' },
        ]}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Button asChild variant="ghost" size="sm">
              <Link href="/admin/billing">
                <ArrowLeft className="mr-1.5 h-4 w-4" /> Back to Billing
              </Link>
            </Button>
            <Button onClick={handleExportCsv} size="sm" disabled={status !== 'success'} startIcon={<Download className="h-4 w-4" />}>
              Export CSV
            </Button>
          </div>
        }
      />

      <Card>
        <CardContent className="pt-6">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-4">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="profession-filter" className="text-xs uppercase tracking-wide text-admin-fg-muted">
                Profession
              </Label>
              <Select
                value={professionFilter}
                onValueChange={(v) => setProfessionFilter(v as ProfessionFilter)}
              >
                <SelectTrigger id="profession-filter">
                  <SelectValue placeholder="All professions" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All professions</SelectItem>
                  <SelectItem value="medicine">Medicine</SelectItem>
                  <SelectItem value="nursing">Nursing</SelectItem>
                  <SelectItem value="pharmacy">Pharmacy</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="category-filter" className="text-xs uppercase tracking-wide text-admin-fg-muted">
                Category
              </Label>
              <Select value={categoryFilter} onValueChange={setCategoryFilter}>
                <SelectTrigger id="category-filter">
                  <SelectValue placeholder="All categories" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All categories</SelectItem>
                  {categories.map((c) => (
                    <SelectItem key={c} value={c}>
                      {c}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="flex items-center gap-2">
              <Switch id="show-draft" checked={showDraft} onCheckedChange={setShowDraft} />
              <Label htmlFor="show-draft">Show drafts</Label>
            </div>

            <div className="flex items-center gap-2">
              <Switch id="show-hidden" checked={showHidden} onCheckedChange={setShowHidden} />
              <Label htmlFor="show-hidden">Show hidden</Label>
            </div>
          </div>
        </CardContent>
      </Card>

      {status === 'loading' ? (
        <div className="space-y-2">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
        </div>
      ) : status === 'error' ? (
        <InlineAlert variant="error" title="Could not load matrix">{errorMessage}</InlineAlert>
      ) : (
        <Card className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-admin-border text-sm">
              <thead className="bg-admin-bg-subtle text-left text-xs font-medium uppercase tracking-wide text-admin-fg-muted">
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
              <tbody className="divide-y divide-admin-border">
                {filteredRows.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="px-4 py-8 text-center text-admin-fg-muted">
                      No plans match the current filters.
                    </td>
                  </tr>
                ) : (
                  filteredRows.map((row) => (
                    <tr key={row.code} className="hover:bg-admin-bg-subtle/60">
                      <td className="px-4 py-3">
                        <div className="font-medium text-admin-fg-strong">{row.name}</div>
                        <div className="text-xs text-admin-fg-muted">
                          <code>{row.code}</code>
                          {row.isDraft && (
                            <Badge variant="warning" className="ml-2">
                              draft
                            </Badge>
                          )}
                          {!row.isVisible && (
                            <Badge variant="default" className="ml-2">
                              hidden
                            </Badge>
                          )}
                        </div>
                      </td>
                      <td className="px-4 py-3 capitalize text-admin-fg-default">{row.profession}</td>
                      <td className="px-4 py-3 text-admin-fg-default">{row.productCategory}</td>
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
                          <span className="text-xs text-admin-fg-muted">—</span>
                        ) : (
                          <div className="flex flex-wrap gap-1">
                            {row.eligibleAddOnCodes.map((code) => (
                              <Badge key={code} variant="info" size="sm">
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
          <div className="border-t border-admin-border bg-admin-bg-subtle px-4 py-2 text-xs text-admin-fg-muted">
            {filteredRows.length} of {data?.plans.length ?? 0} plans • Gold dot = enabled, grey = hidden
          </div>
        </Card>
      )}
    </AdminPageShell>
  );
}

function FlagDot({ enabled }: { enabled: boolean }) {
  return (
    <span
      aria-label={enabled ? 'enabled' : 'disabled'}
      className={`inline-block h-3 w-3 rounded-full ${
        enabled
          ? 'bg-[var(--admin-warning)] ring-2 ring-[var(--admin-warning-tint)]'
          : 'bg-admin-bg-subtle'
      }`}
    />
  );
}

function csvEscape(value: string): string {
  if (value.includes(',') || value.includes('"') || value.includes('\n')) {
    return `"${value.replace(/"/g, '""')}"`;
  }
  return value;
}
