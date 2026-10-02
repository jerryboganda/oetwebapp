'use client';

import { useEffect, useState } from 'react';
import { Award, Download, Calendar } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { getCertificatesData } from '@/lib/learner-data';
import { analytics } from '@/lib/analytics';
import type { LearnerCertificate } from '@/lib/types/learner';

const TYPE_LABELS: Record<string, { label: string; variant: BadgeProps['variant'] }> = {
  study_plan_complete: { label: 'Study Plan', variant: 'info' },
  mock_exam: { label: 'Mock Exam', variant: 'default' },
  readiness_threshold: { label: 'Readiness', variant: 'success' },
  streak_milestone: { label: 'Streak', variant: 'warning' },
};

export default function CertificatesPage() {
  const [certificates, setCertificates] = useState<LearnerCertificate[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { page: 'certificates' });
    getCertificatesData()
      .then(setCertificates)
      .catch(() => setError('Unable to load certificates.'))
      .finally(() => setLoading(false));
  }, []);

  return (
    <>
      <LearnerPageHero
        title="My Certificates"
        description="Certificates earned through your study achievements and milestones."
        icon={Award}
      />

      {loading ? (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2" aria-hidden="true">
          <Skeleton className="h-40 rounded-2xl" />
          <Skeleton className="h-40 rounded-2xl" />
        </div>
      ) : null}

      {!loading && error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

      {!loading && certificates.length === 0 && !error && (
        <EmptyState
          icon={<Award className="h-8 w-8" />}
          title="No certificates yet"
          description="Complete study plans, mock exams, or reach milestones to earn certificates."
        />
      )}

      {!loading && certificates.length > 0 && (
        <MotionSection>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {certificates.map((cert, index) => {
              const typeInfo = TYPE_LABELS[cert.certificateType] ?? { label: cert.certificateType, variant: 'slate' as const };
              return (
                <MotionItem key={cert.id} delayIndex={Math.min(index, 5)} className="h-full">
                  <Card className="flex h-full flex-col">
                    <div className="mb-3 flex items-start justify-between gap-2">
                      <Award className="h-8 w-8 shrink-0 text-gold-fg" aria-hidden="true" />
                      <Badge variant={typeInfo.variant}>{typeInfo.label}</Badge>
                    </div>
                    <h2 className="mb-1 text-sm font-semibold text-navy">{cert.title}</h2>
                    <p className="flex-1 text-xs text-muted">{cert.description}</p>
                    <div className="mt-4 flex flex-wrap items-center justify-between gap-2">
                      <span className="flex items-center gap-1 text-xs tabular-nums text-muted">
                        <Calendar className="h-3.5 w-3.5" aria-hidden="true" />
                        {new Date(cert.issuedAt).toLocaleDateString()}
                      </span>
                      {cert.downloadUrl && (
                        <Button asChild variant="outline" size="sm">
                          <a href={cert.downloadUrl} target="_blank" rel="noopener noreferrer">
                            <Download className="h-3.5 w-3.5" aria-hidden="true" /> Download
                          </a>
                        </Button>
                      )}
                    </div>
                  </Card>
                </MotionItem>
              );
            })}
          </div>
        </MotionSection>
      )}
    </>
  );
}
