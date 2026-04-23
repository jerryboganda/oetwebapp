'use client';

import { useEffect, useState } from 'react';
import { Award, Download, Calendar, Trophy, Star, FileText } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { analytics } from '@/lib/analytics';

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
async function apiRequest<T = unknown>(path: string): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { headers: { Authorization: `Bearer ${token}` } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

/* ── certificate type config ──────────────────── */
const TYPE_CONFIG: Record<string, { icon: typeof Award; color: string; bg: string }> = {
  study_plan_complete:  { icon: Trophy,   color: 'text-warning',  bg: 'bg-warning/10' },
  mock_exam_passed:     { icon: Star,     color: 'text-info',     bg: 'bg-info/10' },
  readiness_threshold:  { icon: Award,    color: 'text-success',  bg: 'bg-success/10' },
  diagnostic_complete:  { icon: FileText, color: 'text-primary',  bg: 'bg-primary/10' },
  streak_milestone:     { icon: Trophy,   color: 'text-warning',  bg: 'bg-warning/10' },
};

const DEFAULT_TYPE = { icon: Award, color: 'text-primary', bg: 'bg-primary/5' };

export default function CertificatePage() {
  const [certificates, setCertificates] = useState<Certificate[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    analytics.track('certificates_viewed');
    apiRequest<{ certificates: Certificate[] }>('/v1/learner/certificates')
      .then(data => setCertificates(data.certificates || []))
      .catch(() => {})
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="max-w-3xl mx-auto space-y-4">
          <Skeleton className="h-8 w-48" /><Skeleton className="h-4 w-80" />
          {[1,2,3].map(i => <Skeleton key={i} className="h-32" />)}
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="My Certificates"
        description="Downloadable certificates for study plan milestones, mock exams, and readiness achievements"
      />

      <div className="max-w-3xl mx-auto">

        {/* summary */}
        <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 mb-8">
          <Card className="p-4 text-center">
            <Award className="h-5 w-5 mx-auto mb-1.5 text-primary" />
            <p className="text-2xl font-bold">{certificates.length}</p>
            <p className="text-xs text-muted-foreground">Total Certificates</p>
          </Card>
          <Card className="p-4 text-center">
            <Download className="h-5 w-5 mx-auto mb-1.5 text-muted-foreground" />
            <p className="text-2xl font-bold">{certificates.filter(c => c.downloadUrl).length}</p>
            <p className="text-xs text-muted-foreground">Downloads Available</p>
          </Card>
          <Card className="p-4 text-center col-span-2 sm:col-span-1">
            <Calendar className="h-5 w-5 mx-auto mb-1.5 text-muted-foreground" />
            <p className="text-sm font-medium">
              {certificates.length > 0
                ? new Date(certificates[0].issuedAt).toLocaleDateString()
                : '—'}
            </p>
            <p className="text-xs text-muted-foreground">Latest Issued</p>
          </Card>
        </div>

        {/* certificate list */}
        {certificates.length > 0 ? (
          <MotionSection className="space-y-4">
            {certificates.map(cert => {
              const cfg = TYPE_CONFIG[cert.type] || DEFAULT_TYPE;
              const IconComp = cfg.icon;
              return (
                <MotionItem key={cert.id}>
                  <Card className="overflow-hidden">
                    <div className="flex">
                      {/* icon strip */}
                      <div className={`w-16 sm:w-20 ${cfg.bg} flex items-center justify-center shrink-0`}>
                        <IconComp className={`h-8 w-8 ${cfg.color}`} />
                      </div>

                      {/* content */}
                      <div className="flex-1 p-4 min-w-0">
                        <div className="flex items-start justify-between gap-3">
                          <div className="min-w-0 flex-1">
                            <h3 className="font-semibold text-sm mb-1">{cert.title}</h3>
                            <p className="text-xs text-muted-foreground mb-2">{cert.description}</p>
                            <div className="flex items-center gap-2">
                              <Badge variant="outline" className="text-[10px] capitalize">
                                {cert.type.replace(/_/g, ' ')}
                              </Badge>
                              <span className="text-[10px] text-muted-foreground">
                                Issued {new Date(cert.issuedAt).toLocaleDateString()}
                              </span>
                            </div>
                          </div>

                          {cert.downloadUrl && (
                            <a
                              href={cert.downloadUrl}
                              target="_blank"
                              rel="noopener noreferrer"
                              onClick={() => analytics.track('certificate_downloaded', { certId: cert.id })}
                            >
                              <Button size="sm" variant="outline" className="shrink-0">
                                <Download className="h-3.5 w-3.5 mr-1" />Download
                              </Button>
                            </a>
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
          <div className="text-center py-16 text-muted-foreground">
            <Award className="h-12 w-12 mx-auto mb-4 opacity-20" />
            <h3 className="font-medium mb-1">No certificates yet</h3>
            <p className="text-sm max-w-sm mx-auto">
              Complete study plan milestones, pass mock exams, or reach readiness thresholds to earn certificates.
            </p>
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
