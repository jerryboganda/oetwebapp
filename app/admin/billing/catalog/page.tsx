'use client';

import { useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, Database, RotateCcw } from 'lucide-react';
import { AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { NoBillingPermission } from '@/components/admin/billing/no-billing-permission';
import { reseedOet2026Catalog, type Oet2026ReseedResponse } from '@/lib/api';
import { useAuth } from '@/contexts/auth-context';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';

export default function AdminCatalogToolsPage() {
  const { user } = useAuth();
  const canWriteCatalog = hasPermission(
    user?.adminPermissions,
    AdminPermission.BillingWrite,
    AdminPermission.BillingCatalogWrite,
  );
  const [seeding, setSeeding] = useState(false);
  const [result, setResult] = useState<Oet2026ReseedResponse | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  if (!user) return null;
  if (!canWriteCatalog) return <NoBillingPermission />;

  const handleReseed = async () => {
    setSeeding(true);
    setErrorMessage(null);
    setResult(null);
    try {
      const response = await reseedOet2026Catalog();
      setResult(response);
    } catch (err) {
      console.error(err);
      setErrorMessage(err instanceof Error ? err.message : 'Reseed failed.');
    } finally {
      setSeeding(false);
    }
  };

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        eyebrow="Billing"
        title="OET 2026 — Catalog Tools"
        description="Re-run the canonical seeder against the live database. Idempotent UPSERT on Code — existing rows refresh, missing rows insert, nothing is deleted."
        icon={<Database aria-hidden className="h-5 w-5" />}
        actions={
          <Button asChild variant="ghost" size="sm">
            <Link href="/admin/billing">
              <ArrowLeft className="mr-1.5 h-4 w-4" /> Back to Billing
            </Link>
          </Button>
        }
      />

      <div className="grid gap-6 lg:grid-cols-3">
        <section className="lg:col-span-2 rounded-2xl border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-800 dark:bg-slate-950">
          <h3 className="text-base font-bold">Re-seed OET 2026 catalog</h3>
          <p className="mt-2 text-sm text-slate-600 dark:text-slate-300">
            Loads <code>backend/src/OetLearner.Api/Data/Seeds/oet-2026-catalog.json</code> and writes 20 BillingPlans + 7 BillingAddOns + matching ContentPackages. Use this after editing the manifest or if an admin accidentally archives a SKU.
          </p>
          <p className="mt-2 text-xs text-slate-500 dark:text-slate-400">
            Safe to run repeatedly. Existing rows are updated in place. The seeder never deletes a row.
          </p>

          <div className="mt-5 flex flex-wrap gap-3">
            <Button onClick={handleReseed} loading={seeding} disabled={seeding}>
              <RotateCcw className="mr-1.5 h-4 w-4" /> {seeding ? 'Re-seeding…' : 'Re-seed catalog now'}
            </Button>
          </div>

          {errorMessage && (
            <div className="mt-4">
              <InlineAlert variant="error" title="Reseed failed">{errorMessage}</InlineAlert>
            </div>
          )}

          {result && (
            <div className="mt-4">
              <InlineAlert variant="success" title="Catalog seed result">
                <ul className="mt-2 grid gap-1 text-sm sm:grid-cols-2">
                  <li>Plans created: <strong>{result.plansCreated}</strong></li>
                  <li>Plans updated: <strong>{result.plansUpdated}</strong></li>
                  <li>Add-ons created: <strong>{result.addOnsCreated}</strong></li>
                  <li>Add-ons updated: <strong>{result.addOnsUpdated}</strong></li>
                  <li>Packages created: <strong>{result.packagesCreated}</strong></li>
                  <li>Packages updated: <strong>{result.packagesUpdated}</strong></li>
                </ul>
              </InlineAlert>
            </div>
          )}
        </section>

        <aside className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-800 dark:bg-slate-950">
          <h4 className="text-sm font-bold text-slate-900 dark:text-slate-50">What the seeder does</h4>
          <ul className="mt-3 list-disc space-y-2 pl-5 text-sm text-slate-700 dark:text-slate-300">
            <li>Reads the canonical OET 2026 JSON manifest.</li>
            <li>UPSERTs each plan by <code>Code</code>; copies all 16 OET 2026 fields.</li>
            <li>UPSERTs each add-on by <code>Code</code>; copies eligibility flag + grant fields.</li>
            <li>Creates / updates matching <code>ContentPackage</code> marketing rows.</li>
            <li>Refreshes the active <code>BillingPlanVersion</code> / <code>BillingAddOnVersion</code> snapshots.</li>
            <li>Pharmacy is created with <code>IsDraft=true</code> and <code>IsVisible=false</code>.</li>
          </ul>
        </aside>
      </div>
    </AdminRouteWorkspace>
  );
}
