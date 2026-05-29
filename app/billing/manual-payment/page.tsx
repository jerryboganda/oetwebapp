'use client';

import { useCallback, useEffect, useState } from 'react';
import { Receipt, Upload } from 'lucide-react';
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
  fetchMyBankAccounts,
  type ManualPaymentDto,
  type ManualPaymentSubmitRequest,
  type BankAccountConfigDto,
} from '@/lib/api';

const METHODS = [
  { value: 'bank_transfer', label: 'Bank transfer' },
  { value: 'wise', label: 'Wise (TransferWise)' },
  { value: 'fawry_offline', label: 'Fawry voucher (Egypt)' },
  { value: 'other', label: 'Other' },
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
  if (status === 'approved') return 'success' as const;
  if (status === 'rejected') return 'danger' as const;
  return 'default' as const;
}

export default function ManualPaymentPage() {
  const [amount, setAmount] = useState('');
  const [currency, setCurrency] = useState('GBP');
  const [method, setMethod] = useState('bank_transfer');
  const [reference, setReference] = useState('');
  const [proofFile, setProofFile] = useState<File | null>(null);

  const [history, setHistory] = useState<ManualPaymentDto[] | null>(null);
  const [bankAccounts, setBankAccounts] = useState<BankAccountConfigDto[] | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    try {
      setHistory(await listOwnManualPayments());
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load history.');
    }
  }, []);

  useEffect(() => {
    void load();
    fetchMyBankAccounts().then(setBankAccounts).catch(() => {});
  }, [load]);

  async function handleSubmit() {
    setError(null);
    if (!proofFile) {
      setError('Please attach a payment proof (screenshot or PDF).');
      return;
    }
    const numericAmount = Number(amount);
    if (!Number.isFinite(numericAmount) || numericAmount <= 0) {
      setError('Enter the amount you paid.');
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
        proofBase64,
      };
      await submitManualPayment(payload);
      setToast({ variant: 'success', message: 'Payment proof submitted. We’ll review within 24 hours.' });
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
        title="Manual / bank-transfer payment"
        description="Pay by bank transfer, Wise, or Fawry voucher. Upload the receipt and our team will activate access within 24 hours."
      />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      {bankAccounts && bankAccounts.length > 0 && (
        <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <h2 className="mb-4 text-sm font-semibold text-navy">Payment destination</h2>
          <div className="space-y-3">
            {bankAccounts.map((account) => (
              <div key={account.id} className="rounded-xl border border-border/60 bg-background-light/60 p-4">
                <p className="font-semibold text-sm text-navy">{account.bankName}</p>
                {account.accountHolderName ? <p className="mt-1 text-sm text-muted">Account name: <span className="font-medium text-navy">{account.accountHolderName}</span></p> : null}
                {account.iban ? <p className="text-sm text-muted">IBAN: <span className="font-mono text-xs text-navy">{account.iban}</span></p> : null}
                {account.swiftBic ? <p className="text-sm text-muted">BIC/SWIFT: <span className="font-mono text-xs text-navy">{account.swiftBic}</span></p> : null}
                {account.accountNumber ? <p className="text-sm text-muted">Account no: <span className="font-mono text-xs text-navy">{account.accountNumber}</span></p> : null}
                {account.routingOrSortCode ? <p className="text-sm text-muted">Sort/routing code: <span className="font-mono text-xs text-navy">{account.routingOrSortCode}</span></p> : null}
                {account.instructionsMarkdown ? <p className="mt-2 text-xs text-muted">{account.instructionsMarkdown}</p> : null}
              </div>
            ))}
          </div>
          <p className="mt-3 text-xs text-muted">After sending payment, fill in the form below to submit your proof.</p>
        </section>
      )}

      <div className="space-y-4 rounded-2xl border border-border bg-surface p-6 shadow-sm">
        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input label="Amount paid" type="number" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="e.g. 49.99" />
          <Input label="Currency" value={currency} onChange={(e) => setCurrency(e.target.value.toUpperCase())} maxLength={3} />
          <Select label="Method" value={method} options={METHODS} onChange={(e) => setMethod(e.target.value)} />
          <Input label="Bank reference / voucher code" value={reference} onChange={(e) => setReference(e.target.value)} placeholder="e.g. TRX12345" />
        </div>

        <label className="flex flex-col gap-1 text-sm">
          <span className="font-medium">Payment proof (PNG, JPG, PDF)</span>
          <input
            type="file"
            accept="image/*,application/pdf"
            onChange={(e) => setProofFile(e.target.files?.[0] ?? null)}
            className="block w-full text-sm"
          />
        </label>

        <div className="flex justify-end">
          <Button onClick={handleSubmit} disabled={submitting}>
            <Upload className="mr-2 h-4 w-4" />
            {submitting ? 'Submitting…' : 'Submit for review'}
          </Button>
        </div>
      </div>

      <div className="mt-6 space-y-3">
        <h2 className="text-lg font-semibold">Your submissions</h2>
        {history === null ? (
          <Skeleton className="h-24 w-full" />
        ) : history.length === 0 ? (
          <p className="text-sm text-muted">No submissions yet.</p>
        ) : (
          <div className="space-y-2">
            {history.map((row) => (
              <div key={row.id} className="flex items-center justify-between rounded-lg border border-border bg-surface p-3 text-sm">
                <div>
                  <p className="font-medium">{row.method.replace('_', ' ')} · {row.amountAmount.toFixed(2)} {row.currency}</p>
                  <p className="text-xs text-muted">Ref: {row.reference || '—'} · {new Date(row.submittedAt).toLocaleString()}</p>
                </div>
                <Badge variant={statusVariant(row.status)}>{row.status}</Badge>
              </div>
            ))}
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
