'use client';

import { Suspense, useCallback, useEffect, useMemo, useState, type ElementType } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { CreditCard, Info, Landmark, Mail, QrCode, Receipt, Upload, WalletCards } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import {
  fetchAvailablePaymentGateways,
  fetchPaymentMethodQrBlob,
  fetchPublicPlans,
  listOwnManualPayments,
  listPublicPaymentMethods,
  submitManualPayment,
  type ManualPaymentDto,
  type ManualPaymentSubmitRequest,
  type PaymentMethodConfigDto,
  type PublicBillingPlan,
} from '@/lib/api';

const SUPPORT_EMAIL = 'support@oetwithdrhesham.co.uk';

/** Lucide icon names → components for the admin-configurable `iconName` field. */
const ICON_MAP: Record<string, ElementType> = {
  QrCode,
  WalletCards,
  Landmark,
  CreditCard,
  Receipt,
  Mail,
  Info,
};

function resolveIcon(name: string | null | undefined): ElementType {
  return (name && ICON_MAP[name]) || WalletCards;
}

/**
 * Fallback list used only when the admin-configurable payment methods can't be
 * loaded from the API (keeps the page usable offline / pre-seed). Mirrors the
 * migration seed in 20260620120000_AddPaymentMethodConfig.
 */
const FALLBACK_PAYMENT_METHODS: PaymentMethodConfigDto[] = [
  {
    id: 'fallback-instapay', key: 'instapay_qr_link', label: 'InstaPay QR / link', category: 'inside_egypt',
    detail: 'Handle: drahmedhesham_work@instapay', meta: 'https://ipn.eg/S/drahmedhesham_work/instapay/2wqbVW',
    instructions: 'Open InstaPay or a supported banking app, scan the QR code or use the payment link, enter the required amount, complete the payment, then send proof of the successful transaction.',
    note: null, referenceRule: false, showQr: true, hasQrImage: false, iconName: 'QrCode', isActive: true, displayOrder: 1, createdAt: '', updatedAt: '',
  },
  {
    id: 'fallback-vodafone', key: 'vodafone_cash_fawry', label: 'Vodafone Cash / Fawry', category: 'inside_egypt',
    detail: '+201062365271', meta: 'Ahmed Hesham Ibrahim Abdrabu',
    instructions: 'Transfer the required amount to the number above, then send a screenshot of the confirmation message.',
    note: null, referenceRule: false, showQr: false, hasQrImage: false, iconName: 'WalletCards', isActive: true, displayOrder: 2, createdAt: '', updatedAt: '',
  },
  {
    id: 'fallback-qnb', key: 'qnb_egypt', label: 'QNB Egypt bank transfer', category: 'inside_egypt',
    detail: 'AHMED HISHAM IBRAHIM ABDRABO IBRAHIM', meta: 'QNB · Account 1002506251368',
    instructions: 'Transfer the required amount to the account above, then send proof of payment.',
    note: 'Inside Egypt only.', referenceRule: false, showQr: false, hasQrImage: false, iconName: 'Landmark', isActive: true, displayOrder: 3, createdAt: '', updatedAt: '',
  },
  {
    id: 'fallback-stripe', key: 'stripe_card', label: 'Stripe card', category: 'international',
    detail: 'Use the card checkout route for instant, verified activation.', meta: 'Manual proof is only needed if support asks for it.',
    instructions: 'Pay by card through the secure Stripe checkout — access is activated automatically once the payment is confirmed.',
    note: null, referenceRule: false, showQr: false, hasQrImage: false, iconName: 'CreditCard', isActive: true, displayOrder: 4, createdAt: '', updatedAt: '',
  },
  {
    id: 'fallback-paypal', key: 'paypal_business', label: 'PayPal Business', category: 'international',
    detail: 'support@oetwithdrhesham.co.uk', meta: '+447961725989',
    instructions: 'Pay through PayPal Business, then send proof of payment if access is not activated automatically.',
    note: null, referenceRule: false, showQr: false, hasQrImage: false, iconName: 'WalletCards', isActive: true, displayOrder: 5, createdAt: '', updatedAt: '',
  },
  {
    id: 'fallback-uk-monzo', key: 'uk_monzo_transfer', label: 'UK bank transfer — Monzo', category: 'international',
    detail: 'Ahmed Ibrahim · Monzo Bank', meta: 'Account 98630202 · Sort code 04-00-03',
    instructions: 'Send a UK bank transfer using the details above, then send proof of payment.',
    note: null, referenceRule: true, showQr: false, hasQrImage: false, iconName: 'Landmark', isActive: true, displayOrder: 6, createdAt: '', updatedAt: '',
  },
  {
    id: 'fallback-intl-monzo', key: 'international_monzo_transfer', label: 'International bank transfer — Monzo', category: 'international',
    detail: 'Ahmed Ibrahim · IBAN GB44MONZ04000398630202', meta: 'BIC / SWIFT MONZGB2L (some banks use MONZGB2LXXX)',
    instructions: 'Send the transfer using the IBAN and BIC/SWIFT details above, then send proof of payment.',
    note: null, referenceRule: true, showQr: false, hasQrImage: false, iconName: 'Landmark', isActive: true, displayOrder: 7, createdAt: '', updatedAt: '',
  },
];

const OTHER_PLAN_OPTION = '__other';

function defaultCurrencyForCategory(category: string): string {
  return category === 'inside_egypt' ? 'EGP' : 'GBP';
}

function readFileAsBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = reader.result;
      if (typeof result === 'string') {
        const commaIndex = result.indexOf(',');
        resolve(commaIndex >= 0 ? result.slice(commaIndex + 1) : result);
      } else {
        reject(new Error('Unsupported reader result type.'));
      }
    };
    reader.onerror = () => reject(reader.error ?? new Error('File read failed.'));
    reader.readAsDataURL(file);
  });
}

function statusVariant(status: string) {
  if (status === 'approved' || status === 'paid') return 'success' as const;
  if (status === 'rejected') return 'danger' as const;
  if (status === 'needs_review') return 'warning' as const;
  return 'default' as const;
}

function ManualPaymentContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const focusEgypt = (searchParams.get('region') ?? '').toLowerCase() === 'egypt';

  const [candidateFullName, setCandidateFullName] = useState('');
  const [candidateEmail, setCandidateEmail] = useState('');
  const [candidateWhatsApp, setCandidateWhatsApp] = useState('');
  const [plans, setPlans] = useState<PublicBillingPlan[] | null>(null);
  const [selectedPlanCode, setSelectedPlanCode] = useState('');
  const [courseName, setCourseName] = useState('');
  const [courseId, setCourseId] = useState<string | null>(null);
  const [amount, setAmount] = useState('');
  const [currency, setCurrency] = useState('EGP');
  const [method, setMethod] = useState<string>('instapay_qr_link');
  const [reference, setReference] = useState('');
  const [proofFile, setProofFile] = useState<File | null>(null);
  const [quoteId, setQuoteId] = useState<string | null>(null);

  const [paymentMethods, setPaymentMethods] = useState<PaymentMethodConfigDto[]>([]);
  const [qrUrls, setQrUrls] = useState<Record<string, string>>({});
  const [availableGateways, setAvailableGateways] = useState<string[]>([]);

  const [history, setHistory] = useState<ManualPaymentDto[] | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const selectedMethod = paymentMethods.find((item) => item.key === method) ?? paymentMethods[0] ?? FALLBACK_PAYMENT_METHODS[0];

  const methodOptions = useMemo(
    () => paymentMethods.map((m) => ({ value: m.key, label: m.label })),
    [paymentMethods],
  );

  const planOptions = useMemo(() => {
    const items = (plans ?? []).map((plan) => ({ value: plan.code, label: plan.label }));
    return [{ value: '', label: 'Select your course / plan…' }, ...items, { value: OTHER_PLAN_OPTION, label: 'Other / not listed' }];
  }, [plans]);

  const load = useCallback(async () => {
    try {
      setHistory(await listOwnManualPayments());
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load payment history.');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  // Load admin-configurable payment methods (fall back to the bundled list so the
  // page is never blank), plus the available hosted gateways for the PayPal CTA.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      let methods: PaymentMethodConfigDto[] = FALLBACK_PAYMENT_METHODS;
      try {
        const res = await listPublicPaymentMethods();
        if (Array.isArray(res) && res.length > 0) methods = res;
      } catch {
        methods = FALLBACK_PAYMENT_METHODS;
      }
      if (cancelled) return;
      setPaymentMethods(methods);
      setMethod((current) => (methods.some((m) => m.key === current) ? current : methods[0]?.key ?? current));
    })();
    void fetchAvailablePaymentGateways()
      .then((res) => {
        if (!cancelled) setAvailableGateways(res.gateways ?? []);
      })
      .catch(() => {
        /* hosted gateways are optional; manual instructions remain */
      });
    return () => {
      cancelled = true;
    };
  }, []);

  // Resolve object URLs for any admin-uploaded QR images.
  useEffect(() => {
    const created: string[] = [];
    let cancelled = false;
    (async () => {
      const next: Record<string, string> = {};
      for (const m of paymentMethods) {
        if (m.showQr && m.hasQrImage) {
          try {
            const blob = await fetchPaymentMethodQrBlob(m.key);
            const url = URL.createObjectURL(blob);
            created.push(url);
            next[m.key] = url;
          } catch {
            /* fall back to static asset below */
          }
        }
      }
      if (!cancelled) setQrUrls(next);
    })();
    return () => {
      cancelled = true;
      created.forEach((url) => URL.revokeObjectURL(url));
    };
  }, [paymentMethods]);

  // When arriving from checkout with ?region=egypt, focus the Egypt section.
  useEffect(() => {
    if (!focusEgypt) return;
    const el = document.getElementById('pay-inside-egypt');
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }, [focusEgypt]);

  // Load purchasable plans for the course dropdown, then apply any pre-fill from
  // the checkout link (?quoteId=&course=&amount=&currency=).
  useEffect(() => {
    let cancelled = false;
    (async () => {
      let loaded: PublicBillingPlan[] = [];
      try {
        const res = await fetchPublicPlans();
        loaded = res.items ?? [];
      } catch {
        loaded = [];
      }
      if (cancelled) return;
      setPlans(loaded);

      const qpQuote = searchParams.get('quoteId');
      const qpCourse = searchParams.get('course');
      const qpAmount = searchParams.get('amount');
      const qpCurrency = searchParams.get('currency');

      if (qpQuote) setQuoteId(qpQuote);
      if (qpAmount && Number.isFinite(Number(qpAmount))) setAmount(qpAmount);
      if (qpCurrency) setCurrency(qpCurrency.toUpperCase());

      if (qpCourse) {
        const match = loaded.find(
          (p) => p.code === qpCourse || p.label.toLowerCase() === qpCourse.toLowerCase(),
        );
        if (match) {
          setSelectedPlanCode(match.code);
          setCourseId(match.code);
          setCourseName(match.label);
          if (!qpAmount) setAmount(String(match.price.amount));
          if (!qpCurrency) setCurrency(match.price.currency.toUpperCase());
        } else {
          setSelectedPlanCode(OTHER_PLAN_OPTION);
          setCourseName(qpCourse);
          setCourseId(null);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
    // searchParams is stable for the life of the page render; run once.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function handlePlanChange(nextCode: string) {
    setSelectedPlanCode(nextCode);
    if (nextCode === OTHER_PLAN_OPTION || nextCode === '') {
      setCourseId(null);
      if (nextCode === '') setCourseName('');
      return;
    }
    const plan = (plans ?? []).find((p) => p.code === nextCode);
    if (plan) {
      setCourseId(plan.code);
      setCourseName(plan.label);
      setAmount(String(plan.price.amount));
      setCurrency(plan.price.currency.toUpperCase());
    }
  }

  function handleMethodChange(nextMethod: string) {
    setMethod(nextMethod);
    const next = paymentMethods.find((m) => m.key === nextMethod);
    if (next) setCurrency(defaultCurrencyForCategory(next.category));
  }

  function applyReferenceRule() {
    const composed = [candidateFullName.trim(), courseName.trim()].filter(Boolean).join(' - ');
    if (composed) setReference(composed);
  }

  function qrSrcFor(m: PaymentMethodConfigDto): string | null {
    if (!m.showQr) return null;
    if (m.hasQrImage) return qrUrls[m.key] ?? null;
    return m.key === 'instapay_qr_link' ? '/payment/instapay-qr.jpg' : null;
  }

  function handlePayWithPayPal() {
    if (quoteId) {
      router.push(`/checkout/review?quoteId=${encodeURIComponent(quoteId)}&gateway=paypal`);
    } else {
      router.push('/billing?tab=plans');
    }
  }

  async function handleSubmit() {
    setError(null);
    if (!candidateFullName.trim() || !candidateEmail.trim() || !candidateWhatsApp.trim() || !courseName.trim()) {
      setError('Full name, email, WhatsApp number, and selected course are required.');
      return;
    }
    if (!reference.trim()) {
      setError('Transaction reference is required.');
      return;
    }
    if (!proofFile) {
      setError('Please attach a payment proof file.');
      return;
    }
    const numericAmount = Number(amount);
    if (!Number.isFinite(numericAmount) || numericAmount <= 0) {
      setError('Enter the paid amount.');
      return;
    }

    setSubmitting(true);
    try {
      const proofBase64 = await readFileAsBase64(proofFile);
      const payload: ManualPaymentSubmitRequest = {
        quoteId: quoteId ?? null,
        amountAmount: numericAmount,
        currency: currency.toUpperCase(),
        method,
        reference,
        proofUrl: '',
        candidateFullName,
        candidateEmail,
        candidateWhatsApp,
        courseName,
        courseId: courseId ?? courseName,
        paymentCategory: selectedMethod.category,
        proofBase64,
      };
      await submitManualPayment(payload);
      setToast({ variant: 'success', message: 'Payment proof submitted for admin review.' });
      setAmount('');
      setReference('');
      setProofFile(null);
      await load();
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Submission failed.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        icon={<Receipt className="h-6 w-6" />}
        eyebrow="OET with Dr. Ahmed Hesham"
        title="Payment options"
        description="Choose one of the approved payment routes below, then submit your proof of payment. For manual methods, access is activated after our team verifies your payment."
      />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <section className="grid gap-4 lg:grid-cols-[1.1fr_0.9fr]">
        <div className="space-y-4">
          <div
            id="pay-inside-egypt"
            className={focusEgypt ? 'rounded-2xl ring-2 ring-primary ring-offset-2 ring-offset-background-light' : undefined}
          >
            <PaymentCategory
              title="Payment Inside Egypt"
              category="inside_egypt"
              methods={paymentMethods}
              qrSrcFor={qrSrcFor}
              availableGateways={availableGateways}
              onPayWithPayPal={handlePayWithPayPal}
            />
          </div>
          <PaymentCategory
            title="International / Worldwide Payment"
            category="international"
            methods={paymentMethods}
            qrSrcFor={qrSrcFor}
            availableGateways={availableGateways}
            onPayWithPayPal={handlePayWithPayPal}
          />
        </div>

        <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <h2 className="text-base font-semibold text-navy">Submit payment proof</h2>
          <p className="mt-1 text-sm text-muted">Access is activated only after admin approval.</p>
          {error && <InlineAlert variant="error" className="mt-4">{error}</InlineAlert>}

          <div className="mt-4 grid gap-4">
            <Input label="Full name" value={candidateFullName} onChange={(e) => setCandidateFullName(e.target.value)} />
            <div className="grid gap-4 sm:grid-cols-2">
              <Input label="Email" type="email" value={candidateEmail} onChange={(e) => setCandidateEmail(e.target.value)} />
              <Input label="WhatsApp number" value={candidateWhatsApp} onChange={(e) => setCandidateWhatsApp(e.target.value)} />
            </div>
            <Select
              label="Selected course / plan"
              value={selectedPlanCode}
              options={planOptions}
              onChange={(e) => handlePlanChange(e.target.value)}
            />
            {selectedPlanCode === OTHER_PLAN_OPTION ? (
              <Input
                label="Course name"
                value={courseName}
                onChange={(e) => setCourseName(e.target.value)}
                placeholder="Type the course or package name"
              />
            ) : null}
            <div className="grid gap-4 sm:grid-cols-2">
              <Input label="Paid amount" type="number" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} />
              <Input label="Currency" value={currency} onChange={(e) => setCurrency(e.target.value.toUpperCase())} maxLength={3} />
            </div>
            <Select label="Payment method" value={method} options={methodOptions} onChange={(e) => handleMethodChange(e.target.value)} />
            <div>
              <Input label="Transaction reference" value={reference} onChange={(e) => setReference(e.target.value)} />
              {selectedMethod.referenceRule ? (
                <button
                  type="button"
                  onClick={applyReferenceRule}
                  className="mt-1 text-xs font-medium text-primary underline-offset-2 hover:underline"
                >
                  Payment reference must be your full name + course name — use “{candidateFullName.trim() || 'Full name'} - {courseName.trim() || 'Course name'}”
                </button>
              ) : null}
            </div>

            <label className="flex flex-col gap-1 text-sm">
              <span className="font-medium">Proof file</span>
              <input
                type="file"
                accept="image/*,application/pdf"
                onChange={(e) => setProofFile(e.target.files?.[0] ?? null)}
                className="block w-full text-sm"
              />
            </label>

            <InlineAlert variant="info">
              <span className="flex items-start gap-2">
                <Info className="mt-0.5 h-4 w-4 shrink-0" />
                <span>
                  <strong>Required proof:</strong> a screenshot of the successful transaction, plus your full name,
                  course name, WhatsApp number, and email.
                </span>
              </span>
            </InlineAlert>
          </div>

          <div className="mt-5 flex justify-end">
            <Button onClick={handleSubmit} disabled={submitting}>
              <Upload className="mr-2 h-4 w-4" />
              {submitting ? 'Submitting...' : 'Submit for review'}
            </Button>
          </div>

          <p className="mt-4 flex items-center gap-2 text-xs text-muted">
            <Mail className="h-3.5 w-3.5" />
            Need help? Contact{' '}
            <a href={`mailto:${SUPPORT_EMAIL}`} className="font-medium text-primary hover:underline">
              {SUPPORT_EMAIL}
            </a>
          </p>
        </div>
      </section>

      <section className="mt-6 space-y-3">
        <h2 className="text-lg font-semibold">Your submissions</h2>
        {history === null ? (
          <Skeleton className="h-24 w-full" />
        ) : history.length === 0 ? (
          <p className="text-sm text-muted">No submissions yet.</p>
        ) : (
          <div className="space-y-2">
            {history.map((row) => (
              <div key={row.id} className="flex flex-col gap-2 rounded-lg border border-border bg-surface p-3 text-sm sm:flex-row sm:items-center sm:justify-between">
                <div>
                  <p className="font-medium">{row.method.replaceAll('_', ' ')} · {row.amountAmount.toFixed(2)} {row.currency}</p>
                  <p className="text-xs text-muted">{row.courseName || 'Course not set'} · Ref: {row.reference || '-'}</p>
                  <p className="text-xs text-muted">{new Date(row.submittedAt).toLocaleString()}</p>
                </div>
                <Badge variant={statusVariant(row.status)}>{row.status}</Badge>
              </div>
            ))}
          </div>
        )}
      </section>
    </LearnerDashboardShell>
  );
}

export default function ManualPaymentPage() {
  return (
    <Suspense fallback={null}>
      <ManualPaymentContent />
    </Suspense>
  );
}

function PaymentCategory({
  title,
  category,
  methods,
  qrSrcFor,
  availableGateways,
  onPayWithPayPal,
}: {
  title: string;
  category: 'inside_egypt' | 'international';
  methods: PaymentMethodConfigDto[];
  qrSrcFor: (m: PaymentMethodConfigDto) => string | null;
  availableGateways: string[];
  onPayWithPayPal: () => void;
}) {
  const rows = methods.filter((method) => method.category === category);
  return (
    <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
      <h2 className="text-base font-semibold text-navy">{title}</h2>
      <div className="mt-4 space-y-3">
        {rows.map((method) => {
          const Icon = resolveIcon(method.iconName);
          const qrSrc = qrSrcFor(method);
          const showPayPal = method.key === 'paypal_business' && availableGateways.includes('paypal');
          return (
            <div key={method.key} className="rounded-xl border border-border/70 bg-background-light/50 p-4">
              <div className="flex items-start gap-3">
                <Icon className="mt-0.5 h-5 w-5 text-primary" />
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <p className="font-semibold text-navy">{method.label}</p>
                    {method.note ? <Badge variant="warning">{method.note}</Badge> : null}
                  </div>
                  <p className="mt-1 break-words text-sm text-muted">{method.detail}</p>
                  {method.meta ? <p className="break-words text-xs text-muted">{method.meta}</p> : null}
                  <p className="mt-2 break-words text-xs text-muted">{method.instructions}</p>
                  {method.referenceRule ? (
                    <p className="mt-1 break-words text-xs font-medium text-navy/80">
                      Payment reference: your full name + course name.
                    </p>
                  ) : null}
                  {showPayPal ? (
                    <Button variant="outline" className="mt-3" onClick={onPayWithPayPal}>
                      Pay with PayPal via secure checkout
                    </Button>
                  ) : null}
                </div>
              </div>
              {qrSrc ? (
                <div className="mt-3 flex justify-center">
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img src={qrSrc} alt={`${method.label} QR`} className="h-44 w-44 rounded-lg border border-border bg-white object-contain p-2" />
                </div>
              ) : null}
            </div>
          );
        })}
      </div>
    </section>
  );
}
