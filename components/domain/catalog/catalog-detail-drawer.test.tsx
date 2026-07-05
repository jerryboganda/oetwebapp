import { describe, it, expect, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { CatalogPlanDetailDrawer } from './catalog-detail-drawer';
import { DEFAULT_CATALOG_STOREFRONT } from '@/lib/catalog-presentation';
import { renderWithRouter } from '@/tests/test-utils';
import type { PublicCatalogPlanRow } from '@/lib/types/admin';

function planFixture(overrides: Partial<PublicCatalogPlanRow> = {}): PublicCatalogPlanRow {
  return {
    code: 'full-nursing-assessment',
    name: 'Nursing Course + Assessment Package',
    description: 'Full nursing course.',
    price: 70,
    originalPrice: null,
    currency: 'GBP',
    accessDurationDays: 180,
    productCategory: 'full_course_bundle',
    profession: 'nursing',
    writingAddonsEnabled: true,
    speakingAddonsEnabled: false,
    speakingPracticeAccessEnabled: true,
    tutorBookDiscountEnabled: true,
    bundledWritingAssessments: 5,
    bundledSpeakingSessions: 0,
    bundledAiCredits: 5,
    bundledTutorBook: false,
    bundledBasicEnglish: false,
    dashboardModules: ['Listening', 'Reading'],
    displayOrder: 1,
    ...overrides,
  };
}

describe('CatalogPlanDetailDrawer', () => {
  it('adds the plan to the cart instead of deep-linking to checkout (dashboard variant)', () => {
    const onAddToCart = vi.fn();
    renderWithRouter(
      <CatalogPlanDetailDrawer
        plan={planFixture()}
        config={DEFAULT_CATALOG_STOREFRONT}
        owned={false}
        variant="dashboard"
        onClose={() => {}}
        onAddToCart={onAddToCart}
      />,
    );

    const button = screen.getByRole('button', { name: /add to cart/i });
    button.click();
    expect(onAddToCart).toHaveBeenCalledWith(planFixture());
  });

  it('adds the plan to the cart for the public variant too', () => {
    const onAddToCart = vi.fn();
    renderWithRouter(
      <CatalogPlanDetailDrawer
        plan={planFixture()}
        config={DEFAULT_CATALOG_STOREFRONT}
        owned={false}
        variant="public"
        onClose={() => {}}
        onAddToCart={onAddToCart}
      />,
    );

    const button = screen.getByRole('button', { name: /add to cart/i });
    button.click();
    expect(onAddToCart).toHaveBeenCalledTimes(1);
  });
});
