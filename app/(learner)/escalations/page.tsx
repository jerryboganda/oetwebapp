'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  AlertTriangle,
  Clock,
  CheckCircle2,
  FileText,
  XCircle,
  Search,
  Plus,
  Send,
  X,
} from 'lucide-react';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { Badge, Button, Card, InlineAlert, Input, Textarea } from '@/components/ui';
import { cardClassName } from '@/components/ui/card';
import { LearnerPageHero } from '@/components/domain';
import { fetchMyEscalations, submitEscalation } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { EscalationStatus, LearnerEscalation } from '@/lib/types/learner';

const STATUS_CONFIG: Record<EscalationStatus, { label: string; icon: React.ElementType; variant: 'warning' | 'info' | 'success' | 'danger' }> = {
  Pending:  { label: 'Pending',   icon: Clock,        variant: 'warning' },
  InReview: { label: 'In Review', icon: Search,       variant: 'info' },
  Resolved: { label: 'Resolved',  icon: CheckCircle2, variant: 'success' },
  Rejected: { label: 'Rejected',  icon: XCircle,      variant: 'danger' },
};

function StatusBadge({ status }: { status: EscalationStatus }) {
  const config = STATUS_CONFIG[status] ?? STATUS_CONFIG.Pending;
  const Icon = config.icon;
  return (
    <Badge variant={config.variant} className="gap-1">
      <Icon className="h-3.5 w-3.5" aria-hidden="true" />
      {config.label}
    </Badge>
  );
}

function formatDate(value: string) {
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  }).format(parsed);
}

function truncate(text: string, max: number) {
  return text.length > max ? `${text.slice(0, max)}…` : text;
}

export default function EscalationsPage() {
  const router = useRouter();
  const [escalations, setEscalations] = useState<LearnerEscalation[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [formSubmissionId, setFormSubmissionId] = useState('');
  const [formReason, setFormReason] = useState('');
  const [formDetails, setFormDetails] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitSuccess, setSubmitSuccess] = useState(false);

  useEffect(() => {
    analytics.track('page_viewed', { page: 'escalations' });
    loadEscalations();
  }, []);

  function loadEscalations() {
    setLoading(true);
    setError(null);
    fetchMyEscalations()
      .then((data) => setEscalations(data))
      .catch(() => setError('Failed to load escalations. Please try again.'))
      .finally(() => setLoading(false));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!formSubmissionId.trim() || !formReason.trim() || !formDetails.trim()) return;

    setSubmitting(true);
    setSubmitError(null);
    try {
      await submitEscalation(formSubmissionId.trim(), formReason.trim(), formDetails.trim());
      setSubmitSuccess(true);
      setFormSubmissionId('');
      setFormReason('');
      setFormDetails('');
      setShowForm(false);
      loadEscalations();
      analytics.track('escalation_submitted', { submissionId: formSubmissionId.trim() });
    } catch {
      setSubmitError('Failed to submit escalation. Please try again.');
    } finally {
      setSubmitting(false);
    }
  }

  const openEscalation = (id: string) => router.push(`/escalations/${id}`);

  return (
    <>
      {/* The hero names the list, so the CTA lives in its aside instead of a
          second "Your Escalations" header. Counts appear once they are real. */}
      <LearnerPageHero
        eyebrow="Disputes & Escalations"
        icon={AlertTriangle}
        accent="amber"
        title="Your Escalations"
        description="Submit a dispute if you believe a score or review was inaccurate. Track the status of each escalation here."
        highlights={loading || error ? undefined : [
          { icon: Clock, label: 'Total', value: String(escalations.length) },
          { icon: Search, label: 'In Review', value: String(escalations.filter((e) => e.status === 'InReview').length) },
          { icon: CheckCircle2, label: 'Resolved', value: String(escalations.filter((e) => e.status === 'Resolved').length) },
        ]}
        aside={(
          <Button variant="primary" onClick={() => { setShowForm(true); setSubmitSuccess(false); }}>
            <Plus className="h-4 w-4" aria-hidden="true" />
            Submit Escalation
          </Button>
        )}
      />

      {submitSuccess && !showForm ? (
        <InlineAlert variant="success" live="polite">Escalation submitted successfully. It will be reviewed shortly.</InlineAlert>
      ) : null}

      {/* ─── Submit Form ─── */}
      {showForm ? (
        <MotionSection>
          <Card padding="lg" className="space-y-4">
            <div className="flex items-center justify-between gap-3">
              <h2 className="text-lg font-semibold text-navy">Submit New Escalation</h2>
              <Button variant="ghost" size="sm" className="w-11 px-0" onClick={() => setShowForm(false)} aria-label="Close form">
                <X className="h-4 w-4" aria-hidden="true" />
              </Button>
            </div>

            {submitError ? <InlineAlert variant="error">{submitError}</InlineAlert> : null}

            <form onSubmit={handleSubmit} className="max-w-2xl space-y-4">
              <Input
                id="esc-submission-id"
                label="Submission ID"
                value={formSubmissionId}
                onChange={(e) => setFormSubmissionId(e.target.value)}
                placeholder="Enter the submission ID to dispute"
                required
              />
              <Input
                id="esc-reason"
                label="Reason"
                value={formReason}
                onChange={(e) => setFormReason(e.target.value)}
                placeholder="Brief reason for the escalation"
                required
              />
              <Textarea
                id="esc-details"
                label="Details"
                value={formDetails}
                onChange={(e) => setFormDetails(e.target.value)}
                placeholder="Provide full details about your dispute"
                rows={4}
                required
              />
              <div className="flex flex-wrap justify-end gap-3">
                <Button type="button" variant="ghost" onClick={() => setShowForm(false)}>
                  Cancel
                </Button>
                <Button type="submit" variant="primary" disabled={submitting}>
                  <Send className="h-4 w-4" aria-hidden="true" />
                  {submitting ? 'Submitting…' : 'Submit'}
                </Button>
              </div>
            </form>
          </Card>
        </MotionSection>
      ) : null}

      {/* ─── Loading ─── */}
      {loading ? (
        <div className="space-y-4" role="status" aria-busy="true" aria-label="Loading">
          {[1, 2, 3].map((i) => (
            <Skeleton aria-hidden key={i} className="h-24 rounded-2xl" />
          ))}
        </div>
      ) : null}

      {/* ─── Error ─── */}
      {!loading && error ? <ErrorState message={error} onRetry={loadEscalations} /> : null}

      {/* ─── Empty ─── */}
      {!loading && !error && escalations.length === 0 ? (
        <EmptyState
          icon={<FileText className="h-8 w-8" />}
          title="No escalations yet"
          description="You haven't submitted any escalations. If you believe a score is incorrect, submit one above."
        />
      ) : null}

      {/* ─── Escalation List ─── */}
      {!loading && !error && escalations.length > 0 ? (
        <div className="space-y-3">
          {escalations.map((esc, index) => (
            <MotionItem key={esc.id} delayIndex={Math.min(index, 5)}>
              <div
                data-escalation-id={esc.id}
                onClick={() => openEscalation(esc.id)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openEscalation(esc.id);
                  }
                }}
                role="button"
                tabIndex={0}
                className={cardClassName({ hoverable: true, interactive: true })}
              >
                <div className="flex items-start justify-between gap-4">
                  <div className="min-w-0 flex-1 space-y-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="break-all text-sm font-semibold text-navy">{esc.submissionId}</span>
                      <StatusBadge status={esc.status} />
                    </div>
                    <p className="text-sm text-muted">{truncate(esc.reason, 80)}</p>
                  </div>
                  <span className="whitespace-nowrap text-xs text-muted tabular-nums">{formatDate(esc.createdAt)}</span>
                </div>
              </div>
            </MotionItem>
          ))}
        </div>
      ) : null}
    </>
  );
}
