'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Package, Check, ChevronRight } from 'lucide-react';
import Link from 'next/link';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, cardClassName } from '@/components/ui/card';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { fetchContentPackages, fetchFreePreviewAssets, fetchPublicCatalog } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { cn } from '@/lib/utils';
import { useRevalidateOnResume } from '@/hooks/use-revalidate-on-resume';
import type {
  ContentPackage,
  FreePreviewAsset,
  PaginatedResponse,
} from '@/lib/types/content-hierarchy';
import type { CatalogPresentation } from '@/lib/catalog-presentation';
import {
  resolveWebsitePackageByCode,
  resolveWebsitePackageWithOverlay,
  websitePackageNumber,
  websitePackagePurchaseHref,
} from '@/lib/catalog-website-packages';

const PACKAGE_TYPE_BADGE: Record<string, BadgeProps['variant']> = {
  full_course: 'default',
  crash_course: 'warning',
  combo: 'default',
  foundation: 'success',
  standalone: 'muted',
};

const PACKAGE_TYPE_LABELS: Record<string, string> = {
  full_course: 'Full Course',
  crash_course: 'Crash Course',
  combo: 'Combo Bundle',
  foundation: 'Foundation',
  standalone: 'Standalone',
};

function filterChipClass(active: boolean) {
  return cn(
    'pressable min-h-11 rounded-control border px-4 text-sm font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
    active ? 'border-primary/30 bg-primary/10 text-primary' : 'hover-primary border-border bg-background-light text-muted',
  );
}

export default function PackagesPage() {
  const [packages, setPackages] = useState<ContentPackage[]>([]);
  const [previews, setPreviews] = useState<FreePreviewAsset[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [typeFilter, setTypeFilter] = useState('');
  const [presentation, setPresentation] = useState<CatalogPresentation | null>(null);

  const changeFilter = (value: string) => {
    setLoading(true);
    setTypeFilter(value);
  };

  useEffect(() => {
    analytics.track('packages_page_viewed');
  }, []);

  // Admin package copy rides on the public catalogue. It never blocks the page: until it
  // arrives (or if it cannot be loaded) the static package copy is shown, and a failed refresh
  // keeps the copy already loaded. A newer request supersedes an older one.
  const presentationRequestRef = useRef(0);
  const loadPresentation = useCallback(async () => {
    presentationRequestRef.current += 1;
    const requestId = presentationRequestRef.current;
    try {
      const catalog = await fetchPublicCatalog();
      if (requestId !== presentationRequestRef.current) return;
      setPresentation(catalog.presentation ?? null);
    } catch {
      // Keep the static defaults or the copy already loaded.
    }
  }, []);

  useEffect(() => {
    void loadPresentation();
    return () => {
      // Drop the reply of any request still in flight once this page is gone.
      presentationRequestRef.current += 1;
    };
  }, [loadPresentation]);

  useRevalidateOnResume(() => {
    void loadPresentation();
  });

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
    <>
      <LearnerPageHero
        icon={Package}
        title="Content Packages"
        description="Compare preparation packages and find the one that fits your study timeline and goals."
      />

      {/* Type filter */}
      <div className="flex flex-wrap gap-2">
        <button
          onClick={() => changeFilter('')}
          type="button"
          aria-pressed={!typeFilter}
          className={filterChipClass(!typeFilter)}
        >
          All Packages
        </button>
        {Object.entries(PACKAGE_TYPE_LABELS).map(([key, label]) => (
          <button
            key={key}
            onClick={() => changeFilter(key)}
            type="button"
            aria-pressed={typeFilter === key}
            className={filterChipClass(typeFilter === key)}
          >
            {label}
          </button>
        ))}
      </div>

      {error && packages.length > 0 ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {loading ? (
        <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-64 rounded-2xl" />
          ))}
        </div>
      ) : (
        <>
          {/* Package comparison grid */}
          {error && packages.length === 0 ? (
            <ErrorState message={error} />
          ) : packages.length === 0 ? (
            <EmptyState
              icon={<Package className="h-8 w-8" />}
              title="No packages available"
              description="Check back soon for new content packages."
            />
          ) : (
            <MotionSection>
              <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
                {displayPackages.map((pkg, index) => {
                  const websitePackage = resolveWebsitePackageWithOverlay(pkg.code, presentation);
                  const packageNo = websitePackage ? websitePackageNumber(websitePackage) : null;
                  // The packages API returns comparisonFeaturesJson, not this array, so it can be absent.
                  const features = websitePackage?.features ?? pkg.comparisonFeatures ?? [];
                  return (
                    <MotionItem key={pkg.id} delayIndex={Math.min(index, 5)}>
                      <Card
                        padding="lg"
                        className={cn('relative flex h-full flex-col', websitePackage?.featured && 'ring-2 ring-primary')}
                      >
                        {websitePackage ? (
                          <>
                            {packageNo != null ? (
                              <p className="eyebrow text-muted">
                                Package {packageNo}
                              </p>
                            ) : null}
                            <div className="mt-2 flex flex-wrap gap-1.5">
                              {websitePackage.badges.map((badge, badgeIndex) => (
                                <Badge key={`${badgeIndex}-${badge}`}>
                                  {badge}
                                </Badge>
                              ))}
                            </div>
                            <div className="mt-3 flex flex-wrap gap-1.5">
                              {websitePackage.metaChips.map((chip, chipIndex) => (
                                <span
                                  key={`${chipIndex}-${chip}`}
                                  className="rounded-full bg-background-light px-2.5 py-0.5 text-2xs font-semibold text-muted"
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
                            variant={PACKAGE_TYPE_BADGE[pkg.packageType] ?? 'muted'}
                            className="mb-3 self-start text-3xs"
                          >
                            {PACKAGE_TYPE_LABELS[pkg.packageType] ?? pkg.packageType}
                          </Badge>
                        )}

                        <h3 className="mt-3 text-lg font-semibold text-navy">
                          {websitePackage?.name ?? pkg.title}
                        </h3>
                        {websitePackage?.description ?? pkg.description ? (
                          <p className="mt-2 whitespace-pre-line text-sm leading-6 text-muted">
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
                            {features.map((feature, featureIndex) => (
                              <li key={`${featureIndex}-${feature}`} className="flex items-start gap-2 text-sm">
                                <Check className="mt-0.5 h-4 w-4 shrink-0 text-success-strong" aria-hidden="true" />
                                <span>{feature}</span>
                              </li>
                            ))}
                          </ul>
                        ) : (
                          <div className="flex-1" />
                        )}

                        {websitePackage ? (
                          <p className="mb-4 whitespace-pre-line rounded-xl bg-background-light px-3 py-2 text-sm">
                            <span className="font-semibold">Best for:</span>{' '}
                            {websitePackage.bestFor}
                          </p>
                        ) : null}

                        <Button asChild fullWidth className="mt-auto">
                          <Link
                            href={
                              websitePackage
                                ? websitePackagePurchaseHref(websitePackage)
                                : `/marketplace/packages/${encodeURIComponent(pkg.code)}`
                            }
                          >
                            View Details <ChevronRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
                          </Link>
                        </Button>
                      </Card>
                    </MotionItem>
                  );
                })}
              </div>
            </MotionSection>
          )}

          {/* Free previews section */}
          {previews.length > 0 && (
            <MotionSection delayIndex={1} className="space-y-4">
              <LearnerSurfaceSectionHeader title="Free Previews" />
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
                {previews.map((preview, index) => (
                  <MotionItem key={preview.id} delayIndex={Math.min(index, 5)}>
                    <Link
                      href="/videos"
                      className={cn(cardClassName({ hoverable: true, interactive: true, padding: 'md' }), 'group block h-full')}
                    >
                      <Badge variant="muted" className="mb-2 text-3xs">{preview.previewType.replaceAll('_', ' ')}</Badge>
                      <h3 className="text-sm font-medium leading-tight text-navy transition-colors group-hover:text-primary">{preview.title}</h3>
                      {preview.conversionCtaText && (
                        <p className="mt-2 text-xs text-primary">{preview.conversionCtaText}</p>
                      )}
                    </Link>
                  </MotionItem>
                ))}
              </div>
            </MotionSection>
          )}
        </>
      )}
    </>
  );
}
