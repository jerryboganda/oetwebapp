import { afterEach, describe, expect, it } from 'vitest';

import {
  CHECKOUT_RETURN_REF_KEY,
  buildPaymentReturnHref,
  clearCheckoutReturnRef,
  readCheckoutReturnRef,
  resolveCheckoutReturnRefs,
  saveCheckoutReturnRef,
} from './checkout-return-ref';

describe('checkout return refs', () => {
  afterEach(() => {
    window.sessionStorage.clear();
  });

  it('prefers Fawaterak aliases when quote and session are missing', () => {
    const refs = resolveCheckoutReturnRefs(new URLSearchParams('status=success&invoice_id=2726912869&payLoad=quote-1'));
    expect(refs).toEqual({ quoteId: 'quote-1', sessionId: '2726912869' });
  });

  it('recovers a stored checkout reference when the return URL has none', () => {
    saveCheckoutReturnRef({ quoteId: 'quote-1', sessionId: 'inv-99', gateway: 'fawaterak' });
    expect(window.sessionStorage.getItem(CHECKOUT_RETURN_REF_KEY)).toContain('quote-1');
    expect(resolveCheckoutReturnRefs(new URLSearchParams('status=success'))).toEqual({
      quoteId: 'quote-1',
      sessionId: 'inv-99',
    });
    clearCheckoutReturnRef();
    expect(readCheckoutReturnRef()).toBeNull();
  });

  it('builds a payment-return href that only polls later', () => {
    expect(buildPaymentReturnHref({
      status: 'success',
      gateway: 'fawaterak',
      quoteId: 'quote-1',
      sessionId: 'inv-99',
    })).toBe('/billing/payment-return?status=success&gateway=fawaterak&quote=quote-1&session=inv-99');
  });
});
