'use client';

import { useEffect, useState } from 'react';
import { adminGetAudienceOptions, type AudienceOptions, type AudienceRow, type MaterialAudienceMode } from '@/lib/materials-api';

interface Props {
  audienceMode: MaterialAudienceMode;
  audiences: AudienceRow[];
  onChange: (mode: MaterialAudienceMode, audiences: AudienceRow[]) => void;
  disabled?: boolean;
}

const MODE_OPTIONS: { value: MaterialAudienceMode; label: string; description: string }[] = [
  {
    value: 'Inherit',
    label: 'Inherit from parent',
    description: 'Uses the audience setting of the nearest parent folder.',
  },
  {
    value: 'Everyone',
    label: 'Everyone (all signed-in candidates)',
    description: 'Visible to any authenticated learner regardless of plan.',
  },
  {
    value: 'Restricted',
    label: 'Restricted — specific plans / cohorts',
    description: 'Only visible to learners with a matching plan or cohort membership.',
  },
];

export function AudiencePicker({ audienceMode, audiences, onChange, disabled }: Props) {
  const [options, setOptions] = useState<AudienceOptions | null>(null);
  const [loadingOptions, setLoadingOptions] = useState(false);

  useEffect(() => {
    if (audienceMode !== 'Restricted') return;
    if (options) return;
    setLoadingOptions(true);
    adminGetAudienceOptions()
      .then(setOptions)
      .catch(() => setOptions({ plans: [], cohorts: [], institutions: [] }))
      .finally(() => setLoadingOptions(false));
  }, [audienceMode, options]);

  function handleModeChange(mode: MaterialAudienceMode) {
    onChange(mode, mode === 'Restricted' ? audiences : []);
  }

  function toggleRow(type: AudienceRow['targetType'], id: string) {
    const exists = audiences.some((a) => a.targetType === type && a.targetId === id);
    const next = exists
      ? audiences.filter((a) => !(a.targetType === type && a.targetId === id))
      : [...audiences, { targetType: type, targetId: id }];
    onChange(audienceMode, next);
  }

  function isChecked(type: AudienceRow['targetType'], id: string) {
    return audiences.some((a) => a.targetType === type && a.targetId === id);
  }

  return (
    <div className="space-y-3">
      <div className="space-y-2">
        {MODE_OPTIONS.map((opt) => (
          <label
            key={opt.value}
            className={[
              'flex cursor-pointer items-start gap-3 rounded-lg border p-3 transition-colors',
              audienceMode === opt.value
                ? 'border-primary bg-primary/5 text-primary-dark'
                : 'border-border hover:border-primary/40',
              disabled ? 'cursor-not-allowed opacity-50' : '',
            ].join(' ')}
          >
            <input
              type="radio"
              name="audienceMode"
              value={opt.value}
              checked={audienceMode === opt.value}
              onChange={() => handleModeChange(opt.value)}
              disabled={disabled}
              className="mt-0.5 accent-primary"
            />
            <div>
              <div className="text-sm font-semibold">{opt.label}</div>
              <div className="text-xs text-muted">{opt.description}</div>
            </div>
          </label>
        ))}
      </div>

      {audienceMode === 'Restricted' && (
        <div className="rounded-lg border border-border bg-surface/60 p-3 space-y-4">
          {loadingOptions && (
            <p className="text-sm text-muted">Loading options…</p>
          )}

          {options && options.plans.length > 0 && (
            <section>
              <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-muted">Billing Plans</p>
              <div className="space-y-1 max-h-40 overflow-y-auto">
                {options.plans.map((p) => (
                  <label key={p.id} className="flex cursor-pointer items-center gap-2 rounded px-2 py-1 text-sm hover:bg-primary/5">
                    <input
                      type="checkbox"
                      checked={isChecked('plan', p.id) || isChecked('plan', p.code)}
                      onChange={() => toggleRow('plan', p.id)}
                      disabled={disabled}
                      className="accent-primary"
                    />
                    <span>{p.name}</span>
                    <span className="ml-auto text-xs text-muted">{p.code}</span>
                  </label>
                ))}
              </div>
            </section>
          )}

          {options && options.cohorts.length > 0 && (
            <section>
              <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-muted">Cohorts / Batches</p>
              <div className="space-y-1 max-h-40 overflow-y-auto">
                {options.cohorts.map((c) => (
                  <label key={c.id} className="flex cursor-pointer items-center gap-2 rounded px-2 py-1 text-sm hover:bg-primary/5">
                    <input
                      type="checkbox"
                      checked={isChecked('cohort', c.id)}
                      onChange={() => toggleRow('cohort', c.id)}
                      disabled={disabled}
                      className="accent-primary"
                    />
                    <span>{c.name}</span>
                  </label>
                ))}
              </div>
            </section>
          )}

          {options && options.institutions.length > 0 && (
            <section>
              <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-muted">Institutions</p>
              <div className="space-y-1 max-h-40 overflow-y-auto">
                {options.institutions.map((i) => (
                  <label key={i.id} className="flex cursor-pointer items-center gap-2 rounded px-2 py-1 text-sm hover:bg-primary/5">
                    <input
                      type="checkbox"
                      checked={isChecked('institution', i.id)}
                      onChange={() => toggleRow('institution', i.id)}
                      disabled={disabled}
                      className="accent-primary"
                    />
                    <span>{i.name}</span>
                  </label>
                ))}
              </div>
            </section>
          )}

          {options && options.plans.length === 0 && options.cohorts.length === 0 && options.institutions.length === 0 && (
            <p className="text-sm text-muted">No plans or cohorts found. Create them in the Billing section first.</p>
          )}
        </div>
      )}
    </div>
  );
}
