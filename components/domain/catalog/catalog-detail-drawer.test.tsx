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
  it('routes "Continue to purchase" straight to checkout, not the marketplace package page', () => {
    renderWithRouter(
      <CatalogPlanDetailDrawer
        plan={planFixture()}
        config={DEFAULT_CATALOG_STOREFRONT}
        owned={false}
        variant="dashboard"
        onClose={() => {}}
      />,
    );

    const link = screen.getByRole('link', { name: /continue to purchase/i });
    expect(link).toHaveAttribute(
      'href',
      '/checkout/review?productType=plan_purchase&priceId=full-nursing-assessment&quantity=1',
    );
    expect(link.getAttribute('href')).not.toContain('/marketplace/packages/');
  });

  it('uses the same checkout route for the public "Get this package" CTA', () => {
    renderWithRouter(
      <CatalogPlanDetailDrawer
        plan={planFixture()}
        config={DEFAULT_CATALOG_STOREFRONT}
        owned={false}
        variant="public"
        onClose={() => {}}
      />,
    );

    const link = screen.getByRole('link', { name: /get this package/i });
    expect(link.getAttribute('href')).toContain('/checkout/review?productType=plan_purchase');
  });
});
