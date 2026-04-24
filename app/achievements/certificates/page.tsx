'use client';

import { useEffect, useState } from 'react';
import { Award, Download, Calendar } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { getCertificatesData } from '@/lib/learner-data';
import { analytics } from '@/lib/analytics';
import type { LearnerCertificate } from '@/lib/types/learner';

const TYPE_LABELS: Record<string, { label: string; color: string }> = {
  study_plan_complete: { label: 'Study Plan', color: 'bg-info/10 text-info' },
  mock_exam: { label: 'Mock Exam', color: 'bg-primary/10 text-primary' },
  readiness_threshold: { label: 'Readiness', color: 'bg-success/10 text-success' },
  streak_milestone: { label: 'Streak', color: 'bg-warning/10 text-warning' },
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

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-4 p-6">
          <Skeleton className="h-10 w-48" />
          <div className="grid gap-4 sm:grid-cols-2">
            <Skeleton className="h-40 rounded-xl" />
            <Skeleton className="h-40 rounded-xl" />
          </div>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="My Certificates"
        description="Certificates earned through your study achievements and milestones."
        icon={<Award className="w-7 h-7" />}
      />

      {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

      {certificates.length === 0 && !error && (
        <EmptyState
          icon={<Award className="w-12 h-12 text-muted/60" />}
          title="No certificates yet"
          description="Complete study plans, mock exams, or reach milestones to earn certificates."
        />
      )}

      {certificates.length > 0 && (
        <MotionSection>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {certificates.map((cert) => {
              const typeInfo = TYPE_LABELS[cert.certificateType] ?? { label: cert.certificateType, color: 'bg-background-light text-muted' };
              return (
                <MotionItem key={cert.id}>
                  <Card className="p-5 h-full flex flex-col">
                    <div className="flex items-start justify-between mb-3">
                      <Award className="w-8 h-8 text-warning" />
                      <span className={`text-xs font-medium px-2 py-0.5 rounded-full ${typeInfo.color}`}>
                        {typeInfo.label}
                      </span>
                    </div>
                    <h3 className="font-semibold text-navy text-sm mb-1">{cert.title}</h3>
                    <p className="text-xs text-muted flex-1">{cert.description}</p>
                    <div className="mt-4 flex items-center justify-between">
                      <span className="flex items-center gap-1 text-xs text-muted/60">
                        <Calendar className="w-3.5 h-3.5" />
                        {new Date(cert.issuedAt).toLocaleDateString()}
                      </span>
                      {cert.downloadUrl && (
                        <a href={cert.downloadUrl} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 rounded-lg border border-border px-3 py-2.5 text-xs font-semibold hover:bg-muted transition-colors">
                            <Download className="w-3.5 h-3.5" /> Download
                        </a>
                      )}
                    </div>
                  </Card>
                </MotionItem>
              );
            })}
          </div>
        </MotionSection>
      )}
    </LearnerDashboardShell>
  );
}
