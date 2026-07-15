'use client';

import { Suspense, useCallback, useEffect, useState, type ElementType } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { CreditCard, Info, Landmark, Mail, QrCode, Receipt, Upload, WalletCards } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Modal } from '@/components/ui/modal';
import { Skeleton } from '@/components/ui/skeleton';
import { useAuth } from '@/contexts/auth-context';
import { ProofDropzone } from '@/components/billing/proof-dropzone';
import { SendProofOnWhatsAppButton } from '@/components/billing/send-proof-whatsapp-button';
import { buildManualPaymentWhatsAppLink, fetchSupportWhatsApp } from '@/lib/billing/whatsapp';
import {
  fetchAvailablePaymentGateways,
  fetchPaymentMethodQrBlob,
  listOwnManualPayments,
  listPublicPaymentMethods,
  submitManualPayment,
  type ManualPaymentDto,
  type ManualPaymentSubmitRequest,
  type PaymentMethodConfigDto,
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
  const { user } = useAuth();

  // Order context comes from the checkout deep-link (?quoteId=&course=&amount=&currency=).
  const quoteId = searchParams.get('quoteId');
  const courseParam = searchParams.get('course');
  const amountParam = searchParams.get('amount');
  const currencyParam = searchParams.get('currency');
  // `?region=egypt` comes from the Egypt route on checkout — it only picks the default
  // method; every offline method stays selectable, whatever region the learner arrived from.
  const preferredCategory = searchParams.get('region') === 'egypt' ? 'inside_egypt' : null;
  const hasOrderContext = Boolean(quoteId || amountParam);

  const registeredEmail = user?.email ?? '';
  const fullName = user?.displayName?.trim() || registeredEmail;
  const courseName = (courseParam ?? '').trim() || 'OET package';
  const orderAmount = Number(amountParam ?? '0');
  const currency = (currencyParam ?? 'EGP').toUpperCase();

  const [reference, setReference] = useState('');
  const [methodKey, setMethodKey] = useState('');
  const [candidateWhatsApp, setCandidateWhatsApp] = useState('');
  const [proofFile, setProofFile] = useState<File | null>(null);
  const [proofError, setProofError] = useState<string | null>(null);

  const [paymentMethods, setPaymentMethods] = useState<PaymentMethodConfigDto[]>([]);
  const [qrUrls, setQrUrls] = useState<Record<string, string>>({});
  const [availableGateways, setAvailableGateways] = useState<string[]>([]);

  const [history, setHistory] = useState<ManualPaymentDto[] | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successOpen, setSuccessOpen] = useState(false);

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

  // Admin-configurable methods (fall back to the bundled list) + hosted gateways.
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
      if (!cancelled) setPaymentMethods(methods);
    })();
    void fetchAvailablePaymentGateways()
      .then((res) => {
        if (!cancelled) setAvailableGateways(res.gateways ?? []);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, []);

  // The method the proof is filed against. `preferredCategory` only picks the initial
  // default — every configured method stays selectable, because the backend accepts
  // proof for any offline route, not just the Egyptian ones.
  const selectedMethodKey =
    methodKey
    || (preferredCategory ? paymentMethods.find((m) => m.category === preferredCategory)?.key : undefined)
    || paymentMethods[0]?.key
    || '';

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

  function qrSrcFor(m: PaymentMethodConfigDto): string | null {
    if (!m.showQr) return null;
    if (m.hasQrImage) return qrUrls[m.key] ?? null;
    return m.key === 'instapay_qr_link' ? '/payment/instapay-qr.jpg' : null;
  }

  function handlePayWithPayPal() {
    if (quoteId) {
      router.push(`/checkout/review?quoteId=${encodeURIComponent(quoteId)}&gateway=paypal`);
    } else {
      router.push('/catalog');
    }
  }

  async function handleSubmit() {
    setError(null);
    if (!registeredEmail) {
      setError('We could not read your account email. Please refresh and try again.');
      return;
    }
    const selectedMethod = paymentMethods.find((m) => m.key === selectedMethodKey);
    if (!selectedMethod) {
      setError('Please choose the payment method you used.');
      return;
    }
    if (!reference.trim()) {
      setError('Please enter your transaction ID.');
      return;
    }
    if (!candidateWhatsApp.trim()) {
      setError('Please enter your WhatsApp number so our team can reach you about this payment.');
      return;
    }
    if (!proofFile) {
      setProofError('Please attach your payment screenshot.');
      return;
    }
    if (!(orderAmount > 0)) {
      setError('We could not read the order amount. Please restart from Subscriptions & Packages.');
      return;
    }

    setSubmitting(true);
    try {
      const proofBase64 = await readFileAsBase64(proofFile);
      const payload: ManualPaymentSubmitRequest = {
        quoteId: quoteId ?? null,
        amountAmount: orderAmount,
        currency,
        method: selectedMethod.key,
        reference: reference.trim(),
        proofUrl: '',
        candidateFullName: fullName,
        candidateEmail: registeredEmail,
        candidateWhatsApp: candidateWhatsApp.trim(),
        courseName,
        courseId: null,
        // Follows the chosen method rather than a hardcoded 'inside_egypt' — the admin
        // dashboard filters on this, so a Wise/UK transfer must not be filed as Egyptian.
        paymentCategory: selectedMethod.category,
        proofBase64,
      };
      await submitManualPayment(payload);
      const support = await fetchSupportWhatsApp();
      const url = buildManualPaymentWhatsAppLink(
        {
          name: fullName,
          email: registeredEmail,
          course: courseName,
          amount: orderAmount,
          currency,
          reference: reference.trim(),
        },
        { number: support.whatsAppNumber, template: support.whatsAppProofTemplate },
      );
      setSuccessOpen(true);
      try { window.open(url, '_blank', 'noopener'); } catch { /* popup blocked — the modal's button is the fallback */ }
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
        title="Submit your payment proof"
        description="Pay with any of the methods below, then upload your screenshot. Access is activated after our team verifies your payment."
      />

      <section className="grid gap-4 lg:grid-cols-[1.1fr_0.9fr]">
        <div className="space-y-4">
          <PaymentCategory
            title="Payment Inside Egypt"
            category="inside_egypt"
            methods={paymentMethods}
            qrSrcFor={qrSrcFor}
            availableGateways={availableGateways}
            onPayWithPayPal={handlePayWithPayPal}
          />
          <PaymentCategory
            title="International payment"
            category="international"
            methods={paymentMethods}
            qrSrcFor={qrSrcFor}
            availableGateways={availableGateways}
            onPayWithPayPal={handlePayWithPayPal}
          />
        </div>

        {hasOrderContext ? (
          <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 className="text-base font-semibold text-navy">Submit payment proof</h2>
            <p className="mt-1 text-sm text-muted">
              {courseName}{orderAmount > 0 ? ` · ${orderAmount} ${currency}` : ''} · activated after admin approval.
            </p>
            {error && <InlineAlert variant="error" className="mt-4">{error}</InlineAlert>}

            <div className="mt-4 grid gap-4">
              <Input label="Your registered email" type="email" value={registeredEmail} readOnly />
              <Select
                label="How did you pay?"
                value={selectedMethodKey}
                onChange={(e) => setMethodKey(e.target.value)}
                options={paymentMethods.map((m) => ({
                  value: m.key,
                  label: `${m.label} · ${m.category === 'inside_egypt' ? 'Inside Egypt' : 'International'}`,
                }))}
                placeholder={paymentMethods.length === 0 ? 'Loading payment methods…' : undefined}
              />
              <Input
                label="Transaction ID"
                value={reference}
                onChange={(e) => setReference(e.target.value)}
                placeholder="The reference / ID from your transfer"
              />
              <Input
                label="Your WhatsApp number"
                type="tel"
                value={candidateWhatsApp}
                onChange={(e) => setCandidateWhatsApp(e.target.value)}
                placeholder="+20 10 1234 5678"
                hint="Include the country code — our team uses this to confirm your payment."
              />
              <div>
                <span className="mb-1 block text-sm font-semibold tracking-tight text-navy">Payment screenshot</span>
                <ProofDropzone
                  value={proofFile}
                  onChange={(file) => { setProofFile(file); setProofError(null); }}
                  error={proofError}
                />
              </div>
            </div>

            <div className="mt-5 flex flex-col gap-2 sm:flex-row sm:justify-end">
              <SendProofOnWhatsAppButton
                variant="outline"
                course={courseName}
                amount={orderAmount > 0 ? orderAmount : undefined}
                currency={currency}
                reference={reference.trim() || quoteId}
              />
              <Button onClick={handleSubmit} disabled={submitting}>
                <Upload className="mr-2 h-4 w-4" />
                {submitting ? 'Submitting...' : 'Submit'}
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
        ) : (
          <div className="rounded-2xl border border-border bg-surface p-6 text-center shadow-sm">
            <h2 className="text-base font-semibold text-navy">Pick a package first</h2>
            <p className="mt-2 text-sm text-muted">
              Choose a plan from Subscriptions &amp; Packages, then pick an offline payment method at
              checkout to submit your proof here.
            </p>
            <Link
              href="/subscriptions"
              className="mt-4 inline-flex items-center justify-center gap-2 rounded-lg bg-primary px-5 py-2.5 text-sm font-semibold text-white hover:bg-primary/90"
            >
              Browse Subscriptions &amp; Packages
            </Link>
            <div className="mt-3 flex justify-center">
              <SendProofOnWhatsAppButton variant="outline" course="" label="Message us on WhatsApp" />
            </div>
          </div>
        )}
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

      <Modal open={successOpen} onClose={() => setSuccessOpen(false)} title="Payment submitted">
        <div className="space-y-4">
          <p className="text-sm text-muted">
            Thanks! Your payment proof was submitted and our team will verify it and activate your access shortly.
          </p>
          <p className="text-sm text-muted">
            Tap below to send us a confirmation on WhatsApp — please attach the same screenshot in the chat before sending.
          </p>
          <div className="flex flex-col gap-2 sm:flex-row sm:justify-end">
            <Button variant="outline" onClick={() => setSuccessOpen(false)}>Done</Button>
            <SendProofOnWhatsAppButton
              course={courseName}
              amount={orderAmount > 0 ? orderAmount : undefined}
              currency={currency}
              reference={reference.trim() || quoteId}
              label="Notify us on WhatsApp"
            />
          </div>
        </div>
      </Modal>
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
  if (rows.length === 0) return null;
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
