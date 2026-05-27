'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { Gauge, Award, AlertTriangle } from 'lucide-react';
import { TutorRouteHero, TutorRouteWorkspace } from '@/components/domain/tutor-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { apiClient } from '@/lib/api';

interface CalibrationDto {
  tutorId: string;
  agreementCoefficient: number;
  requiresRecalibration: boolean;
  lastCalibratedAt: string | null;
}

export default function TutorWritingCalibrationPage() {
  const [data, setData] = useState<CalibrationDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void apiClient.get<CalibrationDto>('/v1/tutors/writing/calibration')
      .then((r) => {
        if (cancelled) return;
        setData(r);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Could not load calibration.');
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const pct = data ? Math.round(data.agreementCoefficient * 100) : null;
  const tone = pct === null ? 'muted' : pct >= 80 ? 'success' : pct >= 60 ? 'warning' : 'danger';

  return (
    <TutorRouteWorkspace>
      <TutorRouteHero
        eyebrow="Calibration"
        icon={Gauge}
        title="Your calibration status"
        description="Agreement coefficient measures how closely your reviews match Dr Ahmed's gold-standard grades."
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <Card padding="lg" className="mt-4">
        <CardContent>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <p className="text-xs font-bold uppercase tracking-wider text-muted">Agreement coefficient</p>
              <p className="text-4xl font-extrabold text-navy tabular-nums">{pct === null ? '—' : `${pct}%`}</p>
              {data?.lastCalibratedAt ? <p className="mt-1 text-xs text-muted">Last calibrated: {new Date(data.lastCalibratedAt).toLocaleString()}</p> : null}
            </div>
            <div>
              <Badge variant={tone} size="sm">
                {pct === null ? 'Not measured' : pct >= 80 ? 'In good standing' : pct >= 60 ? 'Borderline' : 'Re-calibration required'}
              </Badge>
            </div>
          </div>

          {data?.requiresRecalibration ? (
            <div className="mt-4 rounded-xl border border-amber-300/70 bg-amber-50/60 p-3 text-sm text-amber-900">
              <p className="flex items-start gap-2">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                <span>
                  Your reviews diverge from the gold-standard more than the threshold. New queue claims are paused until you complete a calibration test.
                </span>
              </p>
            </div>
          ) : null}

          <div className="mt-4 flex flex-wrap items-center justify-end gap-2">
            <Button asChild variant="outline">
              <Link href="/tutor/writing/queue">Back to queue</Link>
            </Button>
            {data?.requiresRecalibration ? (
              <Button asChild>
                <Link href="/tutor/calibration"><Award className="h-4 w-4" aria-hidden="true" /> Take calibration test</Link>
              </Button>
            ) : null}
          </div>
        </CardContent>
      </Card>
    </TutorRouteWorkspace>
  );
}
