'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import {
  AlertTriangle,
  ArrowLeft,
  Clock,
  CheckCircle2,
  XCircle,
  Search,
  FileText,
  MessageSquare,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero } from '@/components/domain';
import { fetchEscalationDetails } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { EscalationStatus, LearnerEscalation } from '@/lib/types/learner';

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
    hour: 'numeric',
    minute: '2-digit',
  }).format(parsed);
}

export default function EscalationDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const escalationId = params?.id;
  const [escalation, setEscalation] = useState<LearnerEscalation | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!escalationId) return;
    analytics.track('content_view', { page: 'escalation-detail', escalationId });
    fetchEscalationDetails(escalationId)
      .then(setEscalation)
      .catch(() => setError('Failed to load escalation details. Please try again.'))
      .finally(() => setLoading(false));
  }, [escalationId]);

  return (
    <LearnerDashboardShell
      pageTitle="Escalation Details"
      subtitle="View the full details of your dispute"
      backHref="/escalations"
    >
      <div className="space-y-8">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/escalations')}>
          <ArrowLeft className="h-4 w-4" />
          Back to escalations
        </Button>

        {loading ? (
          <div className="space-y-4">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-32 rounded-[24px]" />)}
          </div>
        ) : null}

        {!loading && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {!loading && escalation ? (
          <>
            <LearnerPageHero
              eyebrow="Escalation"
              icon={AlertTriangle}
              accent="amber"
              title={`Dispute for Submission ${escalation.submissionId}`}
              description={escalation.reason}
              highlights={[
                { icon: FileText, label: 'Submission', value: escalation.submissionId },
                { icon: Clock, label: 'Submitted', value: formatDate(escalation.createdAt) },
                { icon: MessageSquare, label: 'Status', value: STATUS_CONFIG[escalation.status]?.label ?? escalation.status },
              ]}
            />

            <div className="space-y-6">
              {/* Status */}
              <div className="rounded-[24px] border border-border bg-surface p-6 space-y-3">
                <h3 className="text-sm font-semibold uppercase tracking-wide text-muted">Status</h3>
                <StatusBadge status={escalation.status} />
              </div>

              {/* Reason */}
              <div className="rounded-[24px] border border-border bg-surface p-6 space-y-3">
                <h3 className="text-sm font-semibold uppercase tracking-wide text-muted">Reason</h3>
                <p className="text-navy">{escalation.reason}</p>
              </div>

              {/* Details */}
              <div className="rounded-[24px] border border-border bg-surface p-6 space-y-3">
                <h3 className="text-sm font-semibold uppercase tracking-wide text-muted">Details</h3>
                <p className="text-navy whitespace-pre-wrap">{escalation.details}</p>
              </div>

              {/* Dates */}
              <div className="rounded-[24px] border border-border bg-surface p-6 space-y-3">
                <h3 className="text-sm font-semibold uppercase tracking-wide text-muted">Timeline</h3>
                <dl className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-sm">
                  <div>
                    <dt className="text-muted">Submitted</dt>
                    <dd className="text-navy font-medium">{formatDate(escalation.createdAt)}</dd>
                  </div>
                  {escalation.updatedAt ? (
                    <div>
                      <dt className="text-muted">Last Updated</dt>
                      <dd className="text-navy font-medium">{formatDate(escalation.updatedAt)}</dd>
                    </div>
                  ) : null}
                </dl>
              </div>

              {/* Resolution Note */}
              {escalation.resolutionNote ? (
                <div className="rounded-[24px] border border-border bg-surface p-6 space-y-3">
                  <h3 className="text-sm font-semibold uppercase tracking-wide text-muted">Resolution Note</h3>
                  <p className="text-navy whitespace-pre-wrap">{escalation.resolutionNote}</p>
                </div>
              ) : null}
            </div>
          </>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
