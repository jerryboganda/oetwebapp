'use client';

import { useCallback, useEffect, useState } from 'react';
import { Award, Download, Calendar, Trophy, Star, FileText } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

/* ── types ─────────────────────────────────────── */
interface Certificate {
  id: string;
  type: string;
  title: string;
  description: string;
  downloadUrl: string | null;
  issuedAt: string;
}

/* ── api helper ───────────────────────────────── */
const apiRequest = apiClient.request;

/* ── certificate type config ──────────────────── */
const TYPE_CONFIG: Record<string, { icon: typeof Award; color: string; bg: string }> = {
  study_plan_complete:  { icon: Trophy,   color: 'text-warning-strong',  bg: 'bg-warning/10' },
  mock_exam_passed:     { icon: Star,     color: 'text-info',     bg: 'bg-info/10' },
  readiness_threshold:  { icon: Award,    color: 'text-success-strong',  bg: 'bg-success/10' },
  diagnostic_complete:  { icon: FileText, color: 'text-primary',  bg: 'bg-primary/10' },
  streak_milestone:     { icon: Trophy,   color: 'text-warning-strong',  bg: 'bg-warning/10' },
};

const DEFAULT_TYPE = { icon: Award, color: 'text-primary', bg: 'bg-primary/5' };

export default function CertificatePage() {
  const [certificates, setCertificates] = useState<Certificate[]>([]);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);

  const load = useCallback(() => {
    setLoading(true);
    setFailed(false);
    apiRequest<{ certificates: Certificate[] }>('/v1/learner/certificates')
      .then(data => setCertificates(data.certificates || []))
      .catch(() => setFailed(true))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    analytics.track('certificates_viewed');
    load();
  }, [load]);

  return (
    <>
      <LearnerPageHero
        icon={Award}
        title="My Certificates"
        description="Downloadable certificates for study plan milestones, mock exams, and readiness achievements"
      />

      {loading ? (
        <div className="space-y-4" aria-hidden="true">
          {[1, 2, 3].map(i => <Skeleton key={i} className="h-32 rounded-2xl" />)}
        </div>
      ) : failed ? (
        <ErrorState onRetry={load} />
      ) : (
        <>
          {/* summary */}
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
            <Card className="text-center">
              <Award className="mx-auto mb-1.5 h-5 w-5 text-primary" aria-hidden="true" />
              <p className="text-2xl font-bold tabular-nums text-navy"><CountUp value={certificates.length} /></p>
              <p className="tile-label text-muted">Total Certificates</p>
            </Card>
            <Card className="text-center">
              <Download className="mx-auto mb-1.5 h-5 w-5 text-muted" aria-hidden="true" />
              <p className="text-2xl font-bold tabular-nums text-navy"><CountUp value={certificates.filter(c => c.downloadUrl).length} /></p>
              <p className="tile-label text-muted">Downloads Available</p>
            </Card>
            <Card className="col-span-2 text-center sm:col-span-1">
              <Calendar className="mx-auto mb-1.5 h-5 w-5 text-muted" aria-hidden="true" />
              <p className="text-sm font-medium tabular-nums text-navy">
                {certificates.length > 0
                  ? new Date(certificates[0].issuedAt).toLocaleDateString()
                  : '–'}
              </p>
              <p className="tile-label text-muted">Latest Issued</p>
            </Card>
          </div>

          {/* certificate list */}
          {certificates.length > 0 ? (
            <MotionSection className="space-y-4">
              {certificates.map((cert, index) => {
                const cfg = TYPE_CONFIG[cert.type] || DEFAULT_TYPE;
                const IconComp = cfg.icon;
                return (
                  <MotionItem key={cert.id} delayIndex={Math.min(index, 5)}>
                    <Card padding="none" className="overflow-hidden">
                      <div className="flex">
                        {/* icon strip */}
                        <div className={`flex w-16 shrink-0 items-center justify-center sm:w-20 ${cfg.bg}`}>
                          <IconComp className={`h-8 w-8 ${cfg.color}`} aria-hidden="true" />
                        </div>

                        {/* content */}
                        <div className="min-w-0 flex-1 p-4">
                          <div className="flex flex-wrap items-start justify-between gap-3">
                            <div className="min-w-0 flex-1">
                              <h2 className="mb-1 text-sm font-semibold text-navy">{cert.title}</h2>
                              <p className="mb-2 text-xs text-muted">{cert.description}</p>
                              <div className="flex flex-wrap items-center gap-2">
                                <Badge variant="outline" className="capitalize">
                                  {cert.type.replace(/_/g, ' ')}
                                </Badge>
                                <span className="text-xs text-muted">
                                  Issued {new Date(cert.issuedAt).toLocaleDateString()}
                                </span>
                              </div>
                            </div>

                            {cert.downloadUrl && (
                              <Button asChild size="sm" variant="outline" className="shrink-0">
                                <a
                                  href={cert.downloadUrl}
                                  target="_blank"
                                  rel="noopener noreferrer"
                                  onClick={() => analytics.track('certificate_downloaded', { certId: cert.id })}
                                >
                                  <Download className="h-3.5 w-3.5" aria-hidden="true" />Download
                                </a>
                              </Button>
                            )}
                          </div>
                        </div>
                      </div>
                    </Card>
                  </MotionItem>
                );
              })}
            </MotionSection>
          ) : (
            <EmptyState
              icon={<Award className="h-8 w-8" />}
              title="No certificates yet"
              description="Complete study plan milestones, pass mock exams, or reach readiness thresholds to earn certificates."
            />
          )}
        </>
      )}
    </>
  );
}
