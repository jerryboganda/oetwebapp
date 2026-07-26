'use client';

import { useEffect, useMemo, useState } from 'react';
import { Package, Check, ChevronRight } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { fetchContentPackages, fetchFreePreviewAssets } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type {
  ContentPackage,
  FreePreviewAsset,
  PaginatedResponse,
} from '@/lib/types/content-hierarchy';
import {
  resolveWebsitePackageByCode,
  websitePackagePurchaseHref,
} from '@/lib/catalog-website-packages';

const PACKAGE_TYPE_COLORS: Record<string, string> = {
  full_course: 'bg-primary/10 text-primary',
  crash_course: 'bg-warning/10 text-warning',
  combo: 'bg-primary/10 text-primary',
  foundation: 'bg-success/10 text-success',
  standalone: 'bg-background-light text-muted',
};

const PACKAGE_TYPE_LABELS: Record<string, string> = {
  full_course: 'Full Course',
  crash_course: 'Crash Course',
  combo: 'Combo Bundle',
  foundation: 'Foundation',
  standalone: 'Standalone',
};

export default function PackagesPage() {
  const [packages, setPackages] = useState<ContentPackage[]>([]);
  const [previews, setPreviews] = useState<FreePreviewAsset[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [typeFilter, setTypeFilter] = useState('');

  const changeFilter = (value: string) => {
    setLoading(true);
    setTypeFilter(value);
  };

  useEffect(() => {
    analytics.track('packages_page_viewed');
  }, []);

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      fetchContentPackages({ type: typeFilter || undefined }),
      fetchFreePreviewAssets(),
    ])
      .then(([pkgData, previewData]) => {
        if (cancelled) return;
        const pkgResponse = pkgData as PaginatedResponse<ContentPackage>;
        setPackages(pkgResponse.items ?? []);
        setPreviews((previewData as FreePreviewAsset[]) ?? []);
        setLoading(false);
      })
      .catch(() => {
        if (cancelled) return;
        setError('Unable to load packages.');
        setLoading(false);
      });
    return () => { cancelled = true; };
  }, [typeFilter]);

  const displayPackages = useMemo(
    () =>
      [...packages].sort((left, right) => {
        const leftNumber = resolveWebsitePackageByCode(left.code)?.packageNo ?? Number.MAX_SAFE_INTEGER;
        const rightNumber = resolveWebsitePackageByCode(right.code)?.packageNo ?? Number.MAX_SAFE_INTEGER;
        return leftNumber - rightNumber || left.displayOrder - right.displayOrder;
      }),
    [packages],
  );

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Content Packages"
        description="Compare preparation packages and find the one that fits your study timeline and goals."
      />

      {/* Type filter */}
      <div className="flex gap-2 mb-6 flex-wrap">
        <button
          onClick={() => changeFilter('')}
          className={`px-3 py-1.5 rounded-full text-sm font-medium transition-colors ${!typeFilter ? 'bg-primary text-white dark:bg-violet-700' : 'bg-muted text-muted hover-primary'}`}
        >
          All Packages
        </button>
        {Object.entries(PACKAGE_TYPE_LABELS).map(([key, label]) => (
          <button
            key={key}
            onClick={() => changeFilter(key)}
            className={`px-3 py-1.5 rounded-full text-sm font-medium transition-colors ${typeFilter === key ? 'bg-primary text-white dark:bg-violet-700' : 'bg-muted text-muted hover-primary'}`}
          >
            {label}
          </button>
        ))}
      </div>

      {error && <InlineAlert variant="error">{error}</InlineAlert>}

      {loading ? (
        <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-64 rounded-xl" />
          ))}
        </div>
      ) : (
        <>
          {/* Package comparison grid */}
          {packages.length === 0 ? (
            <div className="text-center py-12 text-muted">
              <Package className="w-12 h-12 mx-auto mb-3 opacity-40" />
              <p className="text-lg font-medium">No packages available</p>
              <p className="text-sm mt-1">Check back soon for new content packages.</p>
            </div>
          ) : (
            <MotionSection>
              <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
                {displayPackages.map((pkg) => {
                  const websitePackage = resolveWebsitePackageByCode(pkg.code);
                  const features = websitePackage?.features ?? pkg.comparisonFeatures;
                  return (
                    <MotionItem key={pkg.id}>
                      <div
                        className={`relative flex h-full flex-col rounded-xl border border-border bg-surface p-6 ${
                          websitePackage?.featured ? 'ring-2 ring-primary' : ''
                        }`}
                      >
                        {websitePackage ? (
                          <>
                            <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-muted">
                              Package {websitePackage.packageNo}
                            </p>
                            <div className="mt-2 flex flex-wrap gap-1.5">
                              {websitePackage.badges.map((badge) => (
                                <Badge key={badge} className="bg-primary/10 text-primary">
                                  {badge}
                                </Badge>
                              ))}
                            </div>
                            <div className="mt-3 flex flex-wrap gap-1.5">
                              {websitePackage.metaChips.map((chip) => (
                                <span
                                  key={chip}
                                  className="rounded-full bg-background-light px-2.5 py-0.5 text-[11px] font-semibold text-muted"
                                >
                                  {chip}
                                </span>
                              ))}
                            </div>
                            <p className="mt-2 text-xs text-muted">
                              <span className="font-semibold text-navy">Category:</span>{' '}
                              {websitePackage.category}
                            </p>
                          </>
                        ) : (
                          <Badge
                            className={`mb-3 self-start text-[10px] ${
                              PACKAGE_TYPE_COLORS[pkg.packageType] ?? 'bg-muted'
                            }`}
                          >
                            {PACKAGE_TYPE_LABELS[pkg.packageType] ?? pkg.packageType}
                          </Badge>
                        )}

                        <h3 className="mt-3 text-lg font-semibold">
                          {websitePackage?.name ?? pkg.title}
                        </h3>
                        {websitePackage?.description ?? pkg.description ? (
                          <p className="mt-2 text-sm leading-6 text-muted">
                            {websitePackage?.description ?? pkg.description}
                          </p>
                        ) : null}
                        {websitePackage ? (
                          <p className="mt-3 text-sm text-muted">
                            <span className="font-semibold text-navy">Format:</span>{' '}
                            {websitePackage.formatLine}
                          </p>
                        ) : null}

                        {features.length > 0 ? (
                          <ul className="mb-4 mt-4 flex-1 space-y-2">
                            {features.map((feature) => (
                              <li key={feature} className="flex items-start gap-2 text-sm">
                                <Check className="mt-0.5 h-4 w-4 shrink-0 text-success" />
                                <span>{feature}</span>
                              </li>
                            ))}
                          </ul>
                        ) : (
                          <div className="flex-1" />
                        )}

                        {websitePackage ? (
                          <p className="mb-4 rounded-lg border border-border bg-background-light px-3 py-2 text-sm">
                            <span className="font-semibold">Best for:</span>{' '}
                            {websitePackage.bestFor}
                          </p>
                        ) : null}

                        <Link
                          href={
                            websitePackage
                              ? websitePackagePurchaseHref(websitePackage)
                              : `/marketplace/packages/${encodeURIComponent(pkg.code)}`
                          }
                          className="mt-auto flex items-center justify-center gap-1 rounded-lg bg-primary px-4 py-2.5 text-sm font-medium text-white transition-[color,background-color,transform] duration-200 hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
                        >
                          View Details <ChevronRight className="h-4 w-4" />
                        </Link>
                      </div>
                    </MotionItem>
                  );
                })}
              </div>
            </MotionSection>
          )}

          {/* Free previews section */}
          {previews.length > 0 && (
            <div className="mt-10">
              <h2 className="text-base font-semibold mb-4">Free Previews</h2>
              <MotionSection>
                <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
                  {previews.map((preview) => (
                    <MotionItem key={preview.id}>
                      <Link
                        href="/videos"
                        className="group block rounded-lg border border-border bg-surface p-4 hover:shadow-sm transition-shadow"
                      >
                        <Badge variant="muted" className="text-[10px] mb-2">{preview.previewType.replaceAll('_', ' ')}</Badge>
                        <h3 className="text-sm font-medium leading-tight group-hover:text-primary transition-colors">{preview.title}</h3>
                        {preview.conversionCtaText && (
                          <p className="text-xs text-primary mt-2">{preview.conversionCtaText}</p>
                        )}
                      </Link>
                    </MotionItem>
                  ))}
                </div>
              </MotionSection>
            </div>
          )}
        </>
      )}
    </LearnerDashboardShell>
  );
}
