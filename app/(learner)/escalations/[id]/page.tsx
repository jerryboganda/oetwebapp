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
  RotateCcw,
} from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero } from '@/components/domain';
import { fetchEscalationDetails } from '@/lib/api';
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
    <Badge variant={config.variant} size="md" className="gap-1.5">
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
    <>
      <div>
        <Button variant="ghost" size="sm" onClick={() => router.push('/escalations')}>
          <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
          Back to escalations
        </Button>
      </div>

      {loading ? (
        <div className="space-y-4" role="status" aria-busy="true" aria-label="Loading">
          {[1, 2, 3].map((i) => <Skeleton aria-hidden key={i} className="h-32 rounded-2xl" />)}
        </div>
      ) : null}

      {!loading && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {/* The reason is the hero description, the status is the hero badge and the
          dates are hero chips, so the separate Status, Reason and Timeline cards
          that repeated them are gone. */}
      {!loading && escalation ? (
        <>
          <LearnerPageHero
            eyebrow="Escalation"
            icon={AlertTriangle}
            accent="amber"
            title={`Dispute for Submission ${escalation.submissionId}`}
            description={escalation.reason}
            aside={<StatusBadge status={escalation.status} />}
            highlights={[
              { icon: FileText, label: 'Submission', value: escalation.submissionId },
              { icon: Clock, label: 'Submitted', value: formatDate(escalation.createdAt) },
              ...(escalation.updatedAt ? [{ icon: RotateCcw, label: 'Last Updated', value: formatDate(escalation.updatedAt) }] : []),
            ]}
          />

          <MotionSection>
            <Card padding="lg" className="space-y-2">
              <h2 className="text-base font-bold text-navy">Details</h2>
              <p className="max-w-3xl whitespace-pre-wrap break-words text-sm leading-6 text-navy">{escalation.details}</p>
            </Card>
          </MotionSection>

          {escalation.resolutionNote ? (
            <MotionSection delayIndex={1}>
              <Card padding="lg" className="space-y-2">
                <h2 className="text-base font-bold text-navy">Resolution Note</h2>
                <p className="max-w-3xl whitespace-pre-wrap break-words text-sm leading-6 text-navy">{escalation.resolutionNote}</p>
              </Card>
            </MotionSection>
          ) : null}
        </>
      ) : null}
    </>
  );
}
