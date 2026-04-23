'use client';

import { useEffect, useState } from 'react';
import { Package, Check, Star, ChevronRight } from 'lucide-react';
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
          className={`px-3 py-1.5 rounded-full text-sm font-medium transition-colors ${!typeFilter ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground hover:bg-muted/80'}`}
        >
          All Packages
        </button>
        {Object.entries(PACKAGE_TYPE_LABELS).map(([key, label]) => (
          <button
            key={key}
            onClick={() => changeFilter(key)}
            className={`px-3 py-1.5 rounded-full text-sm font-medium transition-colors ${typeFilter === key ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground hover:bg-muted/80'}`}
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
            <div className="text-center py-12 text-muted-foreground">
              <Package className="w-12 h-12 mx-auto mb-3 opacity-40" />
              <p className="text-lg font-medium">No packages available</p>
              <p className="text-sm mt-1">Check back soon for new content packages.</p>
            </div>
          ) : (
            <MotionSection>
              <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
                {packages.map((pkg, i) => (
                  <MotionItem key={pkg.id}>
                    <div className={`relative rounded-xl border bg-card p-6 flex flex-col h-full ${i === 0 ? 'ring-2 ring-primary' : ''}`}>
                      {i === 0 && (
                        <div className="absolute -top-3 left-1/2 -translate-x-1/2">
                          <Badge className="bg-primary text-primary-foreground">
                            <Star className="w-3 h-3 mr-1" /> Most Popular
                          </Badge>
                        </div>
                      )}

                      <Badge className={`self-start text-[10px] mb-3 ${PACKAGE_TYPE_COLORS[pkg.packageType] ?? 'bg-muted'}`}>
                        {PACKAGE_TYPE_LABELS[pkg.packageType] ?? pkg.packageType}
                      </Badge>

                      <h3 className="text-lg font-semibold mb-1">{pkg.title}</h3>
                      {pkg.description && (
                        <p className="text-sm text-muted-foreground mb-4 line-clamp-2">{pkg.description}</p>
                      )}

                      {/* Feature comparison list */}
                      {pkg.comparisonFeatures.length > 0 && (
                        <ul className="space-y-2 mb-4 flex-1">
                          {pkg.comparisonFeatures.map((feature, fi) => (
                            <li key={fi} className="flex items-start gap-2 text-sm">
                              <Check className="w-4 h-4 text-success shrink-0 mt-0.5" />
                              <span>{feature}</span>
                            </li>
                          ))}
                        </ul>
                      )}

                      <Link
                        href={`/marketplace/packages/${pkg.id}`}
                        className="mt-auto flex items-center justify-center gap-1 rounded-lg bg-primary text-primary-foreground px-4 py-2.5 text-sm font-medium hover:bg-primary/90 transition-colors"
                      >
                        View Details <ChevronRight className="w-4 h-4" />
                      </Link>
                    </div>
                  </MotionItem>
                ))}
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
                        href={preview.contentItemId ? `/lessons/${preview.contentItemId}` : '#'}
                        className="group block rounded-lg border bg-card p-4 hover:shadow-sm transition-shadow"
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
