'use client';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EmptyState } from '@/components/ui/empty-error';
import { Input, Textarea } from "@/components/ui/form-controls";
import { MotionItem } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { fetchMyEscalations, submitEscalation } from '@/lib/api';
import type { EscalationStatus, LearnerEscalation } from '@/lib/types/learner';
import {
    AlertTriangle, CheckCircle2, Clock, Plus, Search, Send,
    X, XCircle
} from 'lucide-react';
import { useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';

const STATUS_CONFIG: Record<EscalationStatus, { label: string; icon: React.ElementType; classes: string }> = {
  Pending:  { label: 'Pending',   icon: Clock,        classes: 'bg-warning/10 text-warning' },
  InReview: { label: 'In Review', icon: Search,       classes: 'bg-info/10 text-info' },
  Resolved: { label: 'Resolved',  icon: CheckCircle2, classes: 'bg-success/10 text-success' },
  Rejected: { label: 'Rejected',  icon: XCircle,      classes: 'bg-danger/10 text-danger' },
};

function StatusBadge({ status }: { status: EscalationStatus }) {
  const config = STATUS_CONFIG[status] ?? STATUS_CONFIG.Pending;
  const Icon = config.icon;
  return (
    <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-bold ${config.classes}`}>
      <Icon className="w-3.5 h-3.5" />
      {config.label}
    </span>
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

  return (
    <LearnerDashboardShell
      pageTitle="Escalations"
      subtitle="Submit and track disputes for your graded submissions"
      backHref="/"
    >
      <div className="space-y-8">
        <LearnerPageHero
          eyebrow="Disputes & Escalations"
          icon={AlertTriangle}
          accent="amber"
          title="Your Escalations"
          description="Submit a dispute if you believe a score or review was inaccurate. Track the status of each escalation here."
          highlights={[
            { icon: Clock, label: 'Total', value: String(escalations.length) },
            { icon: Search, label: 'In Review', value: String(escalations.filter((e) => e.status === 'InReview').length) },
            { icon: CheckCircle2, label: 'Resolved', value: String(escalations.filter((e) => e.status === 'Resolved').length) },
          ]}
        />

        <div className="flex items-center justify-between">
          <LearnerSurfaceSectionHeader title="Your Escalations" />
          <Button variant="primary" className="gap-2" onClick={() => { setShowForm(true); setSubmitSuccess(false); }}>
            <Plus className="h-4 w-4" />
            Submit Escalation
          </Button>
        </div>

        {submitSuccess && !showForm ? (
          <InlineAlert variant="success">Escalation submitted successfully. It will be reviewed shortly.</InlineAlert>
        ) : null}

        {/* ─── Submit Form ─── */}
        {showForm ? (
          <div className="rounded-2xl border border-border bg-surface p-6 space-y-4">
            <div className="flex items-center justify-between">
              <h3 className="text-lg font-semibold text-navy">Submit New Escalation</h3>
              <Button variant="ghost" size="sm" onClick={() => setShowForm(false)} aria-label="Close form">
                <X className="h-4 w-4" />
              </Button>
            </div>

            {submitError ? <InlineAlert variant="error">{submitError}</InlineAlert> : null}

            <form onSubmit={handleSubmit} className="space-y-4">
              <div>
                <label htmlFor="esc-submission-id" className="block text-sm font-medium text-navy mb-1">
                  Submission ID
                </label>
                <Input
                  id="esc-submission-id"
                  value={formSubmissionId}
                  onChange={(e) => setFormSubmissionId(e.target.value)}
                  placeholder="Enter the submission ID to dispute"
                  required
                />
              </div>
              <div>
                <label htmlFor="esc-reason" className="block text-sm font-medium text-navy mb-1">
                  Reason
                </label>
                <Input
                  id="esc-reason"
                  value={formReason}
                  onChange={(e) => setFormReason(e.target.value)}
                  placeholder="Brief reason for the escalation"
                  required
                />
              </div>
              <div>
                <label htmlFor="esc-details" className="block text-sm font-medium text-navy mb-1">
                  Details
                </label>
                <Textarea
                  id="esc-details"
                  value={formDetails}
                  onChange={(e) => setFormDetails(e.target.value)}
                  placeholder="Provide full details about your dispute"
                  rows={4}
                  required
                />
              </div>
              <div className="flex gap-3 justify-end">
                <Button type="button" variant="ghost" onClick={() => setShowForm(false)}>
                  Cancel
                </Button>
                <Button type="submit" variant="primary" className="gap-2" disabled={submitting}>
                  <Send className="h-4 w-4" />
                  {submitting ? 'Submitting…' : 'Submit'}
                </Button>
              </div>
            </form>
          </div>
        ) : null}

        {/* ─── Loading ─── */}
        {loading ? (
          <div className="space-y-4">
            {[1, 2, 3].map((i) => (
              <Skeleton key={i} className="h-24 rounded-[24px]" />
            ))}
          </div>
        ) : null}

        {/* ─── Error ─── */}
        {!loading && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {/* ─── Empty ─── */}
        {!loading && !error && escalations.length === 0 ? (
          <EmptyState
            title="No escalations yet"
            description="You haven't submitted any escalations. If you believe a score is incorrect, submit one above."
          />
        ) : null}

        {/* ─── Escalation List ─── */}
        {!loading && !error && escalations.length > 0 ? (
          <div className="space-y-3">
            {escalations.map((esc, index) => (
              <MotionItem key={esc.id} delayIndex={index}>
                <div
                  data-escalation-id={esc.id}
                  onClick={() => router.push(`/escalations/${esc.id}`)}
                  onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') router.push(`/escalations/${esc.id}`); }}
                  role="button"
                  tabIndex={0}
                  className="rounded-2xl border border-border bg-surface p-5 cursor-pointer hover:border-primary/30 hover:shadow-sm transition-all"
                >
                  <div className="flex items-start justify-between gap-4">
                    <div className="min-w-0 flex-1 space-y-1">
                      <div className="flex items-center gap-3">
                        <span className="text-sm font-semibold text-navy">{esc.submissionId}</span>
                        <StatusBadge status={esc.status} />
                      </div>
                      <p className="text-sm text-muted">{truncate(esc.reason, 80)}</p>
                    </div>
                    <span className="text-xs text-muted whitespace-nowrap">{formatDate(esc.createdAt)}</span>
                  </div>
                </div>
              </MotionItem>
            ))}
          </div>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
