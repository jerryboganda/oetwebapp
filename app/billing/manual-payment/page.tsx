'use client';

import { useCallback, useEffect, useState } from 'react';
import { CreditCard, Landmark, QrCode, Receipt, Upload, WalletCards } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import {
  listOwnManualPayments,
  submitManualPayment,
  type ManualPaymentDto,
  type ManualPaymentSubmitRequest,
} from '@/lib/api';

const PAYMENT_METHODS = [
  {
    value: 'instapay_qr_link',
    label: 'InstaPay QR / link',
    category: 'inside_egypt',
    detail: 'Handle: drahmedhesham_work@instapay',
    meta: 'https://ipn.eg/S/drahmedhesham_work/instapay/2wqbVW',
    icon: QrCode,
    showQr: true,
  },
  {
    value: 'vodafone_cash_fawry',
    label: 'Vodafone Cash / Fawry',
    category: 'inside_egypt',
    detail: '+201062365271',
    meta: 'Ahmed Hesham Ibrahim Abdrabu',
    icon: WalletCards,
    showQr: false,
  },
  {
    value: 'qnb_egypt',
    label: 'QNB Egypt',
    category: 'inside_egypt',
    detail: 'AHMED HISHAM IBRAHIM ABDRABO IBRAHIM',
    meta: 'Account 1002506251368',
    icon: Landmark,
    showQr: false,
  },
  {
    value: 'stripe_card',
    label: 'Stripe card',
    category: 'international',
    detail: 'Use the card checkout route for instant verified activation.',
    meta: 'Manual proof is only needed if support asks for it.',
    icon: CreditCard,
    showQr: false,
  },
  {
    value: 'paypal_business',
    label: 'PayPal Business',
    category: 'international',
    detail: 'support@oetwithdrhesham.co.uk',
    meta: '+447961725989',
    icon: WalletCards,
    showQr: false,
  },
  {
    value: 'uk_monzo_transfer',
    label: 'UK Monzo transfer',
    category: 'international',
    detail: 'Ahmed Ibrahim',
    meta: 'Account 98630202 · Sort 04-00-03',
    icon: Landmark,
    showQr: false,
  },
  {
    value: 'international_monzo_transfer',
    label: 'International Monzo transfer',
    category: 'international',
    detail: 'IBAN GB44MONZ04000398630202',
    meta: 'BIC MONZGB2L / MONZGB2LXXX',
    icon: Landmark,
    showQr: false,
  },
] as const;

const METHOD_OPTIONS = PAYMENT_METHODS.map((method) => ({ value: method.value, label: method.label }));

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

export default function ManualPaymentPage() {
  const [candidateFullName, setCandidateFullName] = useState('');
  const [candidateEmail, setCandidateEmail] = useState('');
  const [candidateWhatsApp, setCandidateWhatsApp] = useState('');
  const [courseName, setCourseName] = useState('');
  const [amount, setAmount] = useState('');
  const [currency, setCurrency] = useState('GBP');
  const [method, setMethod] = useState<(typeof PAYMENT_METHODS)[number]['value']>('instapay_qr_link');
  const [reference, setReference] = useState('');
  const [proofFile, setProofFile] = useState<File | null>(null);

  const [history, setHistory] = useState<ManualPaymentDto[] | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const selectedMethod = PAYMENT_METHODS.find((item) => item.value === method) ?? PAYMENT_METHODS[0];

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
        amountAmount: numericAmount,
        currency: currency.toUpperCase(),
        method,
        reference,
        proofUrl: '',
        candidateFullName,
        candidateEmail,
        candidateWhatsApp,
        courseName,
        courseId: courseName,
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
        eyebrow="Billing"
        title="Payment options"
        description="Choose one of the approved payment routes, then submit proof for admin review when the payment is manual."
      />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <section className="grid gap-4 lg:grid-cols-[1.1fr_0.9fr]">
        <div className="space-y-4">
          <PaymentCategory title="Payment Inside Egypt" category="inside_egypt" />
          <PaymentCategory title="International / Worldwide Payment" category="international" />
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
            <Input label="Selected course" value={courseName} onChange={(e) => setCourseName(e.target.value)} placeholder="Course or package name" />
            <div className="grid gap-4 sm:grid-cols-2">
              <Input label="Paid amount" type="number" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} />
              <Input label="Currency" value={currency} onChange={(e) => setCurrency(e.target.value.toUpperCase())} maxLength={3} />
            </div>
            <Select label="Payment method" value={method} options={METHOD_OPTIONS} onChange={(e) => setMethod(e.target.value as typeof method)} />
            <Input label="Transaction reference" value={reference} onChange={(e) => setReference(e.target.value)} />

            <label className="flex flex-col gap-1 text-sm">
              <span className="font-medium">Proof file</span>
              <input
                type="file"
                accept="image/*,application/pdf"
                onChange={(e) => setProofFile(e.target.files?.[0] ?? null)}
                className="block w-full text-sm"
              />
            </label>
          </div>

          <div className="mt-5 flex justify-end">
            <Button onClick={handleSubmit} disabled={submitting}>
              <Upload className="mr-2 h-4 w-4" />
              {submitting ? 'Submitting...' : 'Submit for review'}
            </Button>
          </div>
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

function PaymentCategory({ title, category }: { title: string; category: 'inside_egypt' | 'international' }) {
  const rows = PAYMENT_METHODS.filter((method) => method.category === category);
  return (
    <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
      <h2 className="text-base font-semibold text-navy">{title}</h2>
      <div className="mt-4 space-y-3">
        {rows.map((method) => {
          const Icon = method.icon;
          return (
            <div key={method.value} className="rounded-xl border border-border/70 bg-background-light/50 p-4">
              <div className="flex items-start gap-3">
                <Icon className="mt-0.5 h-5 w-5 text-primary" />
                <div className="min-w-0 flex-1">
                  <p className="font-semibold text-navy">{method.label}</p>
                  <p className="mt-1 break-words text-sm text-muted">{method.detail}</p>
                  <p className="break-words text-xs text-muted">{method.meta}</p>
                </div>
              </div>
              {method.showQr ? (
                <div className="mt-3 flex justify-center">
                  <img src="/payment/instapay-qr.jpg" alt="InstaPay QR" className="h-44 w-44 rounded-lg border border-border bg-white object-contain p-2" />
                </div>
              ) : null}
            </div>
          );
        })}
      </div>
    </section>
  );
}
