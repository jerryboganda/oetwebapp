import { describe, expect, it } from 'vitest';
import {
  filterCatalogVersionSummary,
  isAllowedSummaryKey,
  isForbiddenSummaryKey,
} from '@/components/admin/billing/version-drawer-fields';

describe('catalog version drawer field allowlist', () => {
  it('allows curated catalog metadata keys', () => {
    for (const key of [
      'price',
      'currency',
      'interval',
      'durationMonths',
      'includedCredits',
      'isVisible',
      'discountType',
      'discountValue',
      'startsAt',
      'endsAt',
      'usageLimitTotal',
      'applicablePlanCodes',
      'compatiblePlanCodes',
      'entitlements',
      'grantEntitlements',
    ]) {
      expect(isAllowedSummaryKey(key)).toBe(true);
    }
  });

  it('hard-denies operational data keys regardless of casing', () => {
    for (const key of [
      'redemptionCount',
      'totalRedemptions',
      'redemptions_total',
      'invoiceCount',
      'invoiceIds',
      'checkoutSessionId',
      'paymentTransactionId',
      'transactionsTotal',
      'activeSubscribers',
      'subscriptionCount',
      'walletBalance',
      'webhookSignature',
      'refundIds',
      'disputeIds',
      'quoteId',
      'chargebackTotal',
    ]) {
      expect(isForbiddenSummaryKey(key)).toBe(true);
      expect(isAllowedSummaryKey(key)).toBe(false);
    }
  });

  it('strips forbidden keys and unknown keys when filtering a summary', () => {
    const filtered = filterCatalogVersionSummary({
      price: 29.99,
      currency: 'AUD',
      activeSubscribers: 12345,
      redemptionCount: 99,
      checkoutSessionId: 'cs_test_abc',
      paymentTransactionId: 'pi_123',
      invoiceCount: 7,
      mysteryField: 'unknown',
    });
    expect(filtered).toEqual({ price: 29.99, currency: 'AUD' });
  });

  it('returns an empty object for null / undefined summaries', () => {
    expect(filterCatalogVersionSummary(null)).toEqual({});
    expect(filterCatalogVersionSummary(undefined)).toEqual({});
  });
});
