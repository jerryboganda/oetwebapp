'use client';

import { useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, Database, RotateCcw } from 'lucide-react';

import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { Button } from '@/components/admin/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/admin/ui/card';
import { PageHeader } from '@/components/admin/ui/page-header';
import { InlineAlert } from '@/components/ui/alert';
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
    <AdminPageShell>
      <PageHeader
        eyebrow="Billing"
        title="OET 2026 — Catalog Tools"
        description="Re-run the canonical seeder against the live database. Idempotent UPSERT on Code — existing rows refresh, missing rows insert, nothing is deleted."
        icon={<Database aria-hidden className="h-5 w-5" />}
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Billing', href: '/admin/billing' },
          { label: 'Catalog tools' },
        ]}
        actions={
          <Button asChild variant="ghost" size="sm">
            <Link href="/admin/billing">
              <ArrowLeft className="mr-1.5 h-4 w-4" /> Back to Billing
            </Link>
          </Button>
        }
      />

      <div className="grid gap-6 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle>Re-seed OET 2026 catalog</CardTitle>
            <CardDescription>
              Loads <code>backend/src/OetLearner.Api/Data/Seeds/oet-2026-catalog.json</code> and writes 20
              BillingPlans + 7 BillingAddOns + matching ContentPackages. Use this after editing the manifest or
              if an admin accidentally archives a SKU.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <p className="text-xs text-admin-fg-muted">
              Safe to run repeatedly. Existing rows are updated in place. The seeder never deletes a row.
            </p>

            <div className="flex flex-wrap gap-3">
              <Button onClick={handleReseed} loading={seeding} disabled={seeding} startIcon={<RotateCcw className="h-4 w-4" />}>
                {seeding ? 'Re-seeding…' : 'Re-seed catalog now'}
              </Button>
            </div>

            {errorMessage && (
              <InlineAlert variant="error" title="Reseed failed">
                {errorMessage}
              </InlineAlert>
            )}

            {result && (
              <InlineAlert variant="success" title="Catalog seed result">
                <ul className="mt-2 grid gap-1 text-sm sm:grid-cols-2">
                  <li>
                    Plans created: <strong>{result.plansCreated}</strong>
                  </li>
                  <li>
                    Plans updated: <strong>{result.plansUpdated}</strong>
                  </li>
                  <li>
                    Add-ons created: <strong>{result.addOnsCreated}</strong>
                  </li>
                  <li>
                    Add-ons updated: <strong>{result.addOnsUpdated}</strong>
                  </li>
                  <li>
                    Packages created: <strong>{result.packagesCreated}</strong>
                  </li>
                  <li>
                    Packages updated: <strong>{result.packagesUpdated}</strong>
                  </li>
                </ul>
              </InlineAlert>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-sm">What the seeder does</CardTitle>
          </CardHeader>
          <CardContent>
            <ul className="list-disc space-y-2 pl-5 text-sm text-admin-fg-default">
              <li>Reads the canonical OET 2026 JSON manifest.</li>
              <li>UPSERTs each plan by <code>Code</code>; copies all 16 OET 2026 fields.</li>
              <li>UPSERTs each add-on by <code>Code</code>; copies eligibility flag + grant fields.</li>
              <li>Creates / updates matching <code>ContentPackage</code> marketing rows.</li>
              <li>Refreshes the active <code>BillingPlanVersion</code> / <code>BillingAddOnVersion</code> snapshots.</li>
              <li>Pharmacy is created with <code>IsDraft=true</code> and <code>IsVisible=false</code>.</li>
            </ul>
          </CardContent>
        </Card>
      </div>
    </AdminPageShell>
  );
}
