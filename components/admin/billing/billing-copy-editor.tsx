'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { RotateCcw, Save } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { fetchAdminBillingContent, replaceAdminBillingContent } from '@/lib/api';
import {
  BILLING_COPY_FIELDS,
  BILLING_COPY_SECTIONS,
  type BillingCopyField,
} from '@/lib/billing-copy-defaults';

export interface BillingCopyEditorProps {
  canWrite?: boolean;
}

export function BillingCopyEditor({ canWrite = true }: BillingCopyEditorProps) {
  // Stored overrides as loaded from the server (key → value). Defaults live in code.
  const [stored, setStored] = useState<Record<string, string>>({});
  const [values, setValues] = useState<Record<string, string>>({});
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);

  const initFromOverrides = useCallback((overrides: Record<string, string>) => {
    const next: Record<string, string> = {};
    for (const field of BILLING_COPY_FIELDS) {
      next[field.key] = overrides[field.key] ?? field.default;
    }
    setStored(overrides);
    setValues(next);
  }, []);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const response = await fetchAdminBillingContent();
        if (cancelled) return;
        const overrides: Record<string, string> = {};
        for (const entry of response.entries ?? []) overrides[entry.key] = entry.value;
        initFromOverrides(overrides);
        setStatus('success');
      } catch (error) {
        if (cancelled) return;
        setLoadError(error instanceof Error ? error.message : 'Failed to load billing copy.');
        setStatus('error');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [initFromOverrides]);

  const fieldsBySection = useMemo(() => {
    const map = new Map<string, BillingCopyField[]>();
    for (const field of BILLING_COPY_FIELDS) {
      const list = map.get(field.section) ?? [];
      list.push(field);
      map.set(field.section, list);
    }
    return map;
  }, []);

  const setValue = useCallback((key: string, value: string) => {
    setValues((prev) => ({ ...prev, [key]: value }));
  }, []);

  const resetField = useCallback((field: BillingCopyField) => {
    setValues((prev) => ({ ...prev, [field.key]: field.default }));
  }, []);

  // Desired stored value: empty when equal to the default (so the learner falls back),
  // otherwise the custom override text.
  const desiredFor = useCallback((field: BillingCopyField): string => {
    const current = (values[field.key] ?? field.default).trim();
    return current === field.default.trim() ? '' : (values[field.key] ?? '');
  }, [values]);

  const dirtyCount = useMemo(() => {
    let count = 0;
    for (const field of BILLING_COPY_FIELDS) {
      if (desiredFor(field) !== (stored[field.key] ?? '')) count += 1;
    }
    return count;
  }, [desiredFor, stored]);

  const handleSave = useCallback(async () => {
    if (!canWrite) {
      setFeedback({ tone: 'error', message: 'You have read-only billing access.' });
      return;
    }
    const entries = BILLING_COPY_FIELDS
      .filter((field) => desiredFor(field) !== (stored[field.key] ?? ''))
      .map((field) => ({ key: field.key, value: desiredFor(field), section: field.section }));

    if (entries.length === 0) {
      setFeedback({ tone: 'success', message: 'No changes to save.' });
      return;
    }

    setSaving(true);
    setFeedback(null);
    try {
      const response = await replaceAdminBillingContent(entries);
      const overrides: Record<string, string> = {};
      for (const entry of response.entries ?? []) overrides[entry.key] = entry.value;
      initFromOverrides(overrides);
      setFeedback({ tone: 'success', message: `Saved ${entries.length} change(s). Learners see the new copy on next load.` });
    } catch (error) {
      setFeedback({ tone: 'error', message: error instanceof Error ? error.message : 'Failed to save billing copy.' });
    } finally {
      setSaving(false);
    }
  }, [canWrite, desiredFor, initFromOverrides, stored]);

  if (status === 'loading') {
    return <p className="text-sm text-muted" data-testid="billing-copy-loading">Loading copy…</p>;
  }
  if (status === 'error') {
    return (
      <InlineAlert variant="error" title="Couldn’t load billing copy">
        {loadError ?? 'An unexpected error occurred.'}
      </InlineAlert>
    );
  }

  return (
    <div className="space-y-4" data-testid="billing-copy-editor">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="max-w-2xl text-sm text-muted">
          Every static label, heading, badge, and button on the learner billing page. Blank/default values fall back to
          the built-in copy, so nothing ever renders empty. Changes apply on the learner’s next page load.
        </p>
        <Button variant="primary" onClick={handleSave} disabled={!canWrite || saving} data-testid="billing-copy-save">
          <Save className="mr-1 h-4 w-4" /> {saving ? 'Saving…' : `Save all copy${dirtyCount > 0 ? ` (${dirtyCount})` : ''}`}
        </Button>
      </div>

      {!canWrite ? (
        <InlineAlert variant="info" title="Read-only access">
          You can review billing copy, but saving requires Billing catalog write permission.
        </InlineAlert>
      ) : null}

      {feedback ? (
        <InlineAlert variant={feedback.tone === 'success' ? 'success' : 'error'} title={feedback.tone === 'success' ? 'Saved' : 'Error'}>
          {feedback.message}
        </InlineAlert>
      ) : null}

      {BILLING_COPY_SECTIONS.map((section) => {
        const fields = fieldsBySection.get(section) ?? [];
        if (fields.length === 0) return null;
        return (
          <Card key={section} className="p-5">
            <h3 className="mb-4 text-sm font-semibold uppercase tracking-[0.12em] text-muted">{section}</h3>
            <div className="grid gap-4 md:grid-cols-2">
              {fields.map((field) => {
                const current = values[field.key] ?? field.default;
                const isOverridden = current.trim() !== field.default.trim();
                const control = field.multiline ? (
                  <Textarea
                    label={field.label}
                    value={current}
                    onChange={(e) => setValue(field.key, e.target.value)}
                    disabled={!canWrite}
                  />
                ) : (
                  <Input
                    label={field.label}
                    value={current}
                    onChange={(e) => setValue(field.key, e.target.value)}
                    disabled={!canWrite}
                  />
                );
                return (
                  <div key={field.key} className={field.multiline ? 'md:col-span-2' : undefined}>
                    {control}
                    <div className="mt-1 flex items-center justify-between gap-2">
                      <span className="font-mono text-[10px] text-muted/70">{field.key}</span>
                      {isOverridden ? (
                        <button
                          type="button"
                          onClick={() => resetField(field)}
                          disabled={!canWrite}
                          className="inline-flex items-center gap-1 text-[11px] font-medium text-primary transition-colors hover:text-primary/80 disabled:opacity-50"
                        >
                          <RotateCcw className="h-3 w-3" /> Reset to default
                        </button>
                      ) : (
                        <span className="text-[10px] text-muted/60">default</span>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          </Card>
        );
      })}
    </div>
  );
}
