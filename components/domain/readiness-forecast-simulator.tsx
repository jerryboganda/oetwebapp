'use client';

import { useEffect, useState } from 'react';
import { ArrowRight } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { fetchReadinessForecast } from '@/lib/api';
import type { ReadinessForecast } from '@/lib/mock-data';

interface ReadinessForecastSimulatorProps {
  open: boolean;
  onClose: () => void;
  initialForecast?: ReadinessForecast;
}

export function ReadinessForecastSimulator({ open, onClose, initialForecast }: ReadinessForecastSimulatorProps) {
  const [hours, setHours] = useState<number>(initialForecast?.scenarios.find((s) => s.label === 'Recommended')?.hoursPerWeek ?? 10);
  const [forecast, setForecast] = useState<ReadinessForecast | null>(initialForecast ?? null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true);
    fetchReadinessForecast(hours)
      .then((res) => {
        if (!cancelled) setForecast(res);
      })
      .catch((e) => {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Could not run scenario.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, [hours, open]);

  // The shared Modal brings the dialog role, focus trap, Escape and backdrop close, and focus restore.
  return (
    <Modal open={open} onClose={onClose} title="Forecast simulator">
      <p className="mb-5 text-sm text-muted">Adjust weekly study hours to preview the impact on your target-date probability.</p>

      <div className="mb-6">
        <label htmlFor="forecast-hours" className="eyebrow mb-2 flex items-center justify-between text-muted">
          <span>Hours per week</span>
          <span className="tabular-nums text-navy">{hours} hrs</span>
        </label>
        <input
          id="forecast-hours"
          type="range"
          min={1}
          max={25}
          step={1}
          value={hours}
          onChange={(e) => setHours(Number(e.target.value))}
          className="w-full accent-primary"
        />
      </div>

      {error && <p className="mb-3 text-xs text-danger-strong" role="alert">{error}</p>}

      {forecast && (
        <div className="space-y-3">
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-background-light p-4">
            <div>
              <p className="tile-label text-muted">Projected probability</p>
              <p className="text-3xl font-bold tabular-nums text-navy">{Math.round(forecast.probability)}%</p>
            </div>
            <div className="text-end">
              <p className="tile-label text-muted">Projected readiness</p>
              <p className="text-xl font-bold tabular-nums text-navy">
                {forecast.scenarios[0]?.projectedReadinessAtTarget != null
                  ? Math.round(forecast.scenarios[0].projectedReadinessAtTarget)
                  : 'N/A'}
              </p>
            </div>
          </div>
          <div className="text-xs leading-relaxed text-muted">
            {forecast.requiredImprovement > 0
              ? `You need to gain ${Math.round(forecast.requiredImprovement)} points in ${forecast.weeksAvailable} weeks. At this pace you would need ${Math.round(forecast.weeksNeeded)} weeks.`
              : 'You are already at or above your target. Maintain your current pace.'}
          </div>
        </div>
      )}

      {loading && <p className="mt-3 text-xs text-muted" role="status">Calculating…</p>}

      <div className="mt-6 flex justify-end">
        <Button variant="ghost" onClick={onClose} className="text-primary">
          Done <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
        </Button>
      </div>
    </Modal>
  );
}
