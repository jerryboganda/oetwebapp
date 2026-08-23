export const CHECKOUT_RETURN_REF_KEY = 'oet.checkout.return-ref';

export type CheckoutReturnRef = {
  quoteId: string;
  sessionId: string;
  gateway: string;
};

type SearchParamReader = {
  get(name: string): string | null;
};

function firstNonEmpty(searchParams: SearchParamReader | null | undefined, names: string[]): string | null {
  if (!searchParams) return null;
  for (const name of names) {
    const value = searchParams.get(name)?.trim();
    if (value) return value;
  }
  return null;
}

export function saveCheckoutReturnRef(ref: CheckoutReturnRef): void {
  if (typeof window === 'undefined') return;
  try {
    window.sessionStorage.setItem(CHECKOUT_RETURN_REF_KEY, JSON.stringify(ref));
  } catch {
    // sessionStorage may be blocked in private mode.
  }
}

export function readCheckoutReturnRef(): CheckoutReturnRef | null {
  if (typeof window === 'undefined') return null;
  try {
    const raw = window.sessionStorage.getItem(CHECKOUT_RETURN_REF_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<CheckoutReturnRef>;
    const quoteId = typeof parsed.quoteId === 'string' ? parsed.quoteId.trim() : '';
    const sessionId = typeof parsed.sessionId === 'string' ? parsed.sessionId.trim() : '';
    if (!quoteId && !sessionId) return null;
    return {
      quoteId,
      sessionId,
      gateway: typeof parsed.gateway === 'string' ? parsed.gateway : '',
    };
  } catch {
    return null;
  }
}

export function clearCheckoutReturnRef(): void {
  if (typeof window === 'undefined') return;
  try {
    window.sessionStorage.removeItem(CHECKOUT_RETURN_REF_KEY);
  } catch {
    // ignore
  }
}

export function resolveCheckoutReturnRefsFromSearch(searchParams: SearchParamReader | null | undefined): {
  quoteId: string | null;
  sessionId: string | null;
} {
  return {
    quoteId: firstNonEmpty(searchParams, ['quote', 'quoteId', 'payLoad', 'payload']),
    sessionId: firstNonEmpty(searchParams, ['session', 'session_id', 'invoice_id', 'invoiceId']),
  };
}

export function resolveCheckoutReturnRefs(searchParams: SearchParamReader | null | undefined): {
  quoteId: string | null;
  sessionId: string | null;
} {
  const fromSearch = resolveCheckoutReturnRefsFromSearch(searchParams);
  const stored = readCheckoutReturnRef();
  return {
    quoteId: fromSearch.quoteId ?? (stored?.quoteId ? stored.quoteId : null),
    sessionId: fromSearch.sessionId ?? (stored?.sessionId ? stored.sessionId : null),
  };
}

export function buildPaymentReturnHref(input: {
  status?: string | null;
  gateway?: string | null;
  quoteId?: string | null;
  sessionId?: string | null;
}): string {
  const params = new URLSearchParams();
  if (input.status) params.set('status', input.status);
  if (input.gateway) params.set('gateway', input.gateway);
  if (input.quoteId) params.set('quote', input.quoteId);
  if (input.sessionId) params.set('session', input.sessionId);
  const query = params.toString();
  return query ? `/billing/payment-return?${query}` : '/billing/payment-return';
}
