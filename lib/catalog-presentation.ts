import type { ElementType } from 'react';
import {
  GraduationCap,
  BookOpen,
  Mic,
  FilePenLine,
  Sparkles,
  Layers,
  Crown,
  Package,
  Star,
  Headphones,
  Brain,
  Rocket,
  Trophy,
  Gem,
  Library,
} from 'lucide-react';
import type { LearnerSurfaceAccent } from '@/lib/learner-surface';
import type { PublicCatalogPlanRow, PublicCatalogAddOnRow, PublicCatalogResponse } from '@/lib/types/admin';

/*
 * Catalog "storefront" presentation model.
 *
 * The catalog DATA (plans, add-ons, prices) is owned by the backend. This
 * module owns the PRESENTATION layer: hero copy, category labels/order,
 * section visibility, the add-on legend, the footer CTA, and per-package
 * marketing overlays (tagline, feature bullets, icon, featured flag, etc.).
 *
 * Presentation is resolved by deep-merging an optional presentation block
 * (returned by the backend / edited in the admin CMS) over these defaults,
 * which reproduce the historical hardcoded copy so nothing visually regresses.
 */

export interface CatalogHeroHighlightConfig {
  label: string;
  value: string;
  iconKey?: string;
}

export interface CatalogStorefrontHeroConfig {
  eyebrow: string;
  title: string;
  subtitle: string;
  accent: LearnerSurfaceAccent;
  highlights: CatalogHeroHighlightConfig[];
}

export interface CatalogCategoryConfig {
  key: string;
  label: string;
  description?: string;
  visible: boolean;
  displayOrder: number;
}

export interface CatalogLegendItem {
  key: string;
  label: string;
  description: string;
}

export interface CatalogSectionToggles {
  showFilters: boolean;
  showCompareMatrix: boolean;
  showAddOns: boolean;
  showCta: boolean;
}

export interface CatalogCtaConfig {
  title: string;
  subtitle: string;
  primaryLabel: string;
  primaryHref: string;
  secondaryLabel: string;
  secondaryHref: string;
}

export interface CatalogStorefrontConfig {
  accent: LearnerSurfaceAccent;
  hero: CatalogStorefrontHeroConfig;
  categories: CatalogCategoryConfig[];
  professionLabels: Record<string, string>;
  legend: CatalogLegendItem[];
  sections: CatalogSectionToggles;
  cta: CatalogCtaConfig;
}

export interface CatalogCardPresentation {
  tagline?: string;
  featureBullets?: string[];
  iconKey?: string;
  imageUrl?: string;
  featured?: boolean;
  badgeLabel?: string;
  accent?: LearnerSurfaceAccent;
  displayOrder?: number | null;
}

export interface CatalogPresentation {
  storefront: Partial<CatalogStorefrontConfig>;
  byCode: Record<string, CatalogCardPresentation>;
}

/** GET /v1/catalog/pricing may include the presentation overlay. */
export interface PublicCatalogResponseWithPresentation extends PublicCatalogResponse {
  presentation?: CatalogPresentation | null;
}

const ACCENTS: readonly LearnerSurfaceAccent[] = [
  'primary', 'navy', 'amber', 'blue', 'indigo', 'purple', 'rose', 'emerald', 'slate',
];

export function normalizeAccent(value: string | null | undefined, fallback: LearnerSurfaceAccent = 'primary'): LearnerSurfaceAccent {
  return (ACCENTS as readonly string[]).includes(value ?? '') ? (value as LearnerSurfaceAccent) : fallback;
}

export const CATALOG_ICON_REGISTRY: Record<string, ElementType> = {
  course: GraduationCap,
  book: BookOpen,
  library: Library,
  speaking: Mic,
  writing: FilePenLine,
  listening: Headphones,
  recalls: Brain,
  sparkles: Sparkles,
  bundle: Layers,
  crown: Crown,
  package: Package,
  star: Star,
  rocket: Rocket,
  trophy: Trophy,
  gem: Gem,
};

export const CATALOG_ICON_KEYS = Object.keys(CATALOG_ICON_REGISTRY);

export function resolveCatalogIcon(iconKey?: string | null): ElementType | undefined {
  if (!iconKey) return undefined;
  return CATALOG_ICON_REGISTRY[iconKey];
}

/** Heuristic default icon per product category, used when admin sets none. */
export function defaultIconForCategory(category: string): ElementType {
  if (category.startsWith('writing')) return FilePenLine;
  if (category.startsWith('speaking')) return Mic;
  if (category.startsWith('combo')) return Sparkles;
  if (category.includes('bundle')) return Layers;
  if (category === 'book') return BookOpen;
  if (category === 'foundation') return Rocket;
  return GraduationCap;
}

export const DEFAULT_CATALOG_CATEGORIES: CatalogCategoryConfig[] = [
  { key: 'full_course', label: 'Full Recorded Courses', visible: true, displayOrder: 10 },
  { key: 'full_course_bundle', label: 'Full Course Bundles', visible: true, displayOrder: 20 },
  { key: 'crash_course', label: 'Crash Courses', visible: true, displayOrder: 30 },
  { key: 'crash_course_bundle', label: 'Crash Course Bundles', visible: true, displayOrder: 40 },
  { key: 'writing_crash', label: 'Writing Crash Courses', visible: true, displayOrder: 50 },
  { key: 'writing_crash_bundle', label: 'Writing Bundles', visible: true, displayOrder: 60 },
  { key: 'speaking_crash', label: 'Speaking Crash Course', visible: true, displayOrder: 70 },
  { key: 'speaking_session', label: 'Private Speaking Sessions', visible: true, displayOrder: 80 },
  { key: 'combo_double', label: 'Double Special: Writing + Speaking', visible: true, displayOrder: 90 },
  { key: 'combo_mega', label: 'Mega Special Package', visible: true, displayOrder: 100 },
  { key: 'foundation', label: 'Foundation', visible: true, displayOrder: 110 },
  { key: 'book', label: 'The Tutor Book', visible: true, displayOrder: 120 },
];

export const DEFAULT_CATALOG_STOREFRONT: CatalogStorefrontConfig = {
  accent: 'primary',
  hero: {
    eyebrow: 'OET with Dr. Ahmed Hesham · 2026 Portfolio',
    title: 'Subscriptions & Packages',
    subtitle:
      'Recorded courses, writing letter assessments, private speaking sessions and The Tutor Book — every option from the 2026 portfolio, with current and original pricing.',
    accent: 'primary',
    highlights: [
      { label: 'Recorded courses', value: 'Full & crash', iconKey: 'course' },
      { label: 'Writing & speaking', value: 'Assessed by tutors', iconKey: 'writing' },
      { label: 'The Tutor Book', value: '2026 edition', iconKey: 'book' },
    ],
  },
  categories: DEFAULT_CATALOG_CATEGORIES,
  professionLabels: {
    all: 'All disciplines',
    medicine: 'Medicine',
    nursing: 'Nursing',
    pharmacy: 'Pharmacy',
    physiotherapy: 'Physiotherapy',
    allied_health: 'Allied health professions',
  },
  legend: [
    { key: 'W', label: 'Writing add-ons', description: 'Writing letter assessment add-ons are offered on this product.' },
    { key: 'S', label: 'Speaking add-ons', description: 'Extra private speaking sessions can be added to this product.' },
    { key: 'TB', label: 'Tutor Book £32', description: 'Discounted £32 Tutor Book add-on is available with this product.' },
  ],
  sections: {
    showFilters: true,
    showCompareMatrix: true,
    showAddOns: true,
    showCta: true,
  },
  cta: {
    title: 'Ready to start your OET preparation?',
    subtitle: 'Choose the package that fits your exam date and unlock your dedicated dashboard.',
    primaryLabel: 'Get started',
    primaryHref: '/register',
    secondaryLabel: 'Browse the marketplace',
    secondaryHref: '/marketplace/packages',
  },
};

export function resolveStorefrontConfig(presentation?: CatalogPresentation | null): CatalogStorefrontConfig {
  const overlay = presentation?.storefront;
  if (!overlay) return DEFAULT_CATALOG_STOREFRONT;

  const categories = (overlay.categories && overlay.categories.length > 0
    ? overlay.categories
    : DEFAULT_CATALOG_STOREFRONT.categories
  )
    .filter((category) => typeof category?.key === 'string' && category.key.length > 0)
    .map((category) => ({
      key: category.key,
      label: category.label || category.key,
      description: category.description,
      visible: category.visible !== false,
      displayOrder: Number.isFinite(category.displayOrder) ? category.displayOrder : 999,
    }));

  return {
    accent: normalizeAccent(overlay.accent, DEFAULT_CATALOG_STOREFRONT.accent),
    hero: {
      eyebrow: overlay.hero?.eyebrow ?? DEFAULT_CATALOG_STOREFRONT.hero.eyebrow,
      title: overlay.hero?.title ?? DEFAULT_CATALOG_STOREFRONT.hero.title,
      subtitle: overlay.hero?.subtitle ?? DEFAULT_CATALOG_STOREFRONT.hero.subtitle,
      accent: normalizeAccent(overlay.hero?.accent, DEFAULT_CATALOG_STOREFRONT.hero.accent),
      highlights:
        overlay.hero?.highlights && overlay.hero.highlights.length > 0
          ? overlay.hero.highlights.filter((h) => h?.label && h?.value).slice(0, 6)
          : DEFAULT_CATALOG_STOREFRONT.hero.highlights,
    },
    categories,
    professionLabels: { ...DEFAULT_CATALOG_STOREFRONT.professionLabels, ...(overlay.professionLabels ?? {}) },
    legend:
      overlay.legend && overlay.legend.length > 0
        ? overlay.legend.filter((l) => l?.key && l?.label)
        : DEFAULT_CATALOG_STOREFRONT.legend,
    sections: { ...DEFAULT_CATALOG_STOREFRONT.sections, ...(overlay.sections ?? {}) },
    cta: { ...DEFAULT_CATALOG_STOREFRONT.cta, ...(overlay.cta ?? {}) },
  };
}

export function resolveCardPresentation(
  code: string,
  presentation?: CatalogPresentation | null,
): CatalogCardPresentation {
  return presentation?.byCode?.[code] ?? {};
}

export function professionLabel(config: CatalogStorefrontConfig, profession: string): string {
  return config.professionLabels[profession] ?? profession;
}

export function categoryLabel(config: CatalogStorefrontConfig, key: string): string {
  return config.categories.find((c) => c.key === key)?.label ?? key.replace(/_/g, ' ');
}

export function formatAccessDuration(days: number): string {
  if (days >= 9000) return 'Permanent access';
  if (days >= 365) return `${Math.round(days / 365)} year${days >= 730 ? 's' : ''} access`;
  if (days >= 30) return `${Math.round(days / 30)} months access`;
  return `${days} days access`;
}

export function formatPrice(amount: number, currency = 'GBP'): string {
  const symbol = currency === 'GBP' ? '£' : currency === 'USD' ? '$' : currency === 'EUR' ? '€' : '';
  if (symbol) return `${symbol}${Math.round(amount)}`;
  return `${Math.round(amount)} ${currency}`;
}

export function deriveDefaultBullets(plan: PublicCatalogPlanRow): string[] {
  const bullets: string[] = [];
  if (plan.bundledWritingAssessments > 0) {
    bullets.push(`${plan.bundledWritingAssessments} bundled writing assessment${plan.bundledWritingAssessments === 1 ? '' : 's'}`);
  }
  if (plan.bundledSpeakingSessions > 0) {
    bullets.push(`${plan.bundledSpeakingSessions} private speaking session${plan.bundledSpeakingSessions === 1 ? '' : 's'}`);
  }
  if (plan.bundledAiCredits > 0) {
    bullets.push(`${plan.bundledAiCredits} AI practice credits`);
  }
  if (plan.bundledTutorBook) {
    bullets.push('The Tutor Book, First Edition 2026 (PDF + Telegram)');
  }
  if (plan.bundledBasicEnglish) {
    bullets.push('Basic English foundation course');
  }
  if (Array.isArray(plan.dashboardModules) && plan.dashboardModules.length > 0) {
    bullets.push(`Dashboard modules: ${plan.dashboardModules.join(', ')}`);
  }
  return bullets;
}

export function planFeatureBullets(plan: PublicCatalogPlanRow, presentation: CatalogCardPresentation): string[] {
  if (presentation.featureBullets && presentation.featureBullets.length > 0) {
    return presentation.featureBullets.filter((b) => typeof b === 'string' && b.trim().length > 0);
  }
  return deriveDefaultBullets(plan);
}

export function addOnEnabledFlags(plan: PublicCatalogPlanRow): Array<{ key: string; label: string }> {
  const flags: Array<{ key: string; label: string }> = [];
  if (plan.writingAddonsEnabled) flags.push({ key: 'W', label: 'Writing add-ons' });
  if (plan.speakingAddonsEnabled) flags.push({ key: 'S', label: 'Speaking add-ons' });
  if (plan.tutorBookDiscountEnabled) flags.push({ key: 'TB', label: 'Tutor Book £32' });
  return flags;
}

export interface ResolvedCatalogGroup {
  key: string;
  label: string;
  description?: string;
  plans: PublicCatalogPlanRow[];
}

export function groupPlansByCategory(
  plans: PublicCatalogPlanRow[],
  config: CatalogStorefrontConfig,
  presentation?: CatalogPresentation | null,
): ResolvedCatalogGroup[] {
  const byCategory = new Map<string, PublicCatalogPlanRow[]>();
  for (const plan of plans) {
    const key = plan.productCategory || 'standalone';
    const bucket = byCategory.get(key) ?? [];
    bucket.push(plan);
    byCategory.set(key, bucket);
  }

  const orderedCategories = [...config.categories].sort((a, b) => a.displayOrder - b.displayOrder);
  const groups: ResolvedCatalogGroup[] = [];

  for (const category of orderedCategories) {
    if (!category.visible) continue;
    const items = byCategory.get(category.key);
    if (!items || items.length === 0) continue;
    const sorted = [...items].sort((a, b) => {
      const aOrder = resolveCardPresentation(a.code, presentation).displayOrder ?? a.displayOrder;
      const bOrder = resolveCardPresentation(b.code, presentation).displayOrder ?? b.displayOrder;
      return (aOrder ?? 0) - (bOrder ?? 0);
    });
    groups.push({ key: category.key, label: category.label, description: category.description, plans: sorted });
    byCategory.delete(category.key);
  }

  for (const [key, items] of byCategory.entries()) {
    if (items.length === 0) continue;
    groups.push({ key, label: key.replace(/_/g, ' '), plans: [...items].sort((a, b) => a.displayOrder - b.displayOrder) });
  }

  return groups;
}

export function sortAddOns(addOns: PublicCatalogAddOnRow[]): PublicCatalogAddOnRow[] {
  return [...addOns].sort((a, b) => a.displayOrder - b.displayOrder);
}
