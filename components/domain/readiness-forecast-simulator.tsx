'use client';

import { useEffect, useState } from 'react';
import { X, Sliders, ArrowRight } from 'lucide-react';
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

  if (!open) return null;

  return (
    <div className="overlay-safe-area fixed inset-0 z-50 flex items-center justify-center bg-navy/30 backdrop-blur-sm">
      <div className="bg-surface rounded-3xl shadow-xl border border-border max-w-lg w-full p-6 sm:p-8 max-h-[calc(100dvh-2rem-env(safe-area-inset-top)-env(safe-area-inset-bottom))] overflow-y-auto">
        <div className="flex items-start justify-between mb-4">
          <div className="flex items-center gap-2">
            <span className="inline-flex h-8 w-8 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <Sliders className="w-4 h-4" />
            </span>
            <div>
              <h3 className="text-base font-bold text-navy">Forecast simulator</h3>
              <p className="text-xs text-muted">Adjust weekly study hours to preview the impact on your target-date probability.</p>
            </div>
          </div>
          <button onClick={onClose} className="inline-flex h-8 w-8 items-center justify-center rounded-lg hover:bg-background-light" aria-label="Close">
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="mb-6">
          <label htmlFor="forecast-hours" className="flex items-center justify-between text-xs font-bold text-muted uppercase tracking-widest mb-2">
            <span>Hours per week</span>
            <span className="text-navy">{hours} hrs</span>
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

        {error && <p className="text-xs text-danger mb-3">{error}</p>}

        {forecast && (
          <div className="space-y-3">
            <div className="rounded-2xl border border-border bg-background-light p-4 flex items-center justify-between">
              <div>
                <p className="text-[10px] uppercase tracking-widest font-bold text-muted">Projected probability</p>
                <p className="text-3xl font-bold text-navy">{Math.round(forecast.probability)}%</p>
              </div>
              <div className="text-right">
                <p className="text-[10px] uppercase tracking-widest font-bold text-muted">Projected readiness</p>
                <p className="text-xl font-bold text-navy">
                  {forecast.scenarios[0]?.projectedReadinessAtTarget != null
                    ? Math.round(forecast.scenarios[0].projectedReadinessAtTarget)
                    : 'N/A'}
                </p>
              </div>
            </div>
            <div className="text-xs text-muted leading-relaxed">
              {forecast.requiredImprovement > 0
                ? `You need to gain ${Math.round(forecast.requiredImprovement)} points in ${forecast.weeksAvailable} weeks. At this pace you would need ${Math.round(forecast.weeksNeeded)} weeks.`
                : 'You are already at or above your target. Maintain your current pace.'}
            </div>
          </div>
        )}

        {loading && <p className="text-xs text-muted mt-3">Calculating…</p>}

        <div className="mt-6 flex justify-end">
          <button
            onClick={onClose}
            className="inline-flex items-center gap-1.5 text-sm font-bold text-primary hover:underline"
          >
            Done <ArrowRight className="w-4 h-4" />
          </button>
        </div>
      </div>
    </div>
  );
}
