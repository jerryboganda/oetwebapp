'use client';

import { useCallback, useEffect, useState } from 'react';
import { motion } from 'motion/react';
import {
  AlertTriangle,
  CheckCircle2,
  PenSquare,
  Save,
  RotateCcw,
  ToggleLeft,
  ToggleRight,
} from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  adminGetWritingOptions,
  adminUpdateWritingOptions,
  type AdminWritingOptions,
} from '@/lib/api';

type EditableOptions = Omit<AdminWritingOptions, 'updatedAt' | 'updatedByAdminId'>;

const DEFAULTS: EditableOptions = {
  aiGradingEnabled: true,
  aiCoachEnabled: true,
  killSwitchReason: null,
  freeTierEnabled: false,
  freeTierLimit: 0,
  freeTierWindowDays: 7,
};

function toEditable(opts: AdminWritingOptions): EditableOptions {
  const { updatedAt: _u, updatedByAdminId: _b, ...rest } = opts;
  void _u;
  void _b;
  return rest;
}

interface ToggleRowProps {
  label: string;
  description: string;
  value: boolean;
  onChange: (v: boolean) => void;
}

function ToggleRow({ label, description, value, onChange }: ToggleRowProps) {
  return (
    <div className="flex items-start justify-between gap-4 py-3">
      <div className="min-w-0">
        <div className="flex items-center gap-2">
          <span className="text-sm font-medium">{label}</span>
          {value ? (
            <Badge variant="success">On</Badge>
          ) : (
            <Badge variant="danger">Off</Badge>
          )}
        </div>
        <p className="mt-1 text-xs text-muted">{description}</p>
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={value}
        aria-label={label}
        onClick={() => onChange(!value)}
        className="shrink-0"
      >
        {value ? (
          <ToggleRight className="h-7 w-7 text-success" />
        ) : (
          <ToggleLeft className="h-7 w-7 text-muted" />
        )}
      </button>
    </div>
  );
}

export default function WritingOptionsPage() {
  const [server, setServer] = useState<AdminWritingOptions | null>(null);
  const [draft, setDraft] = useState<EditableOptions>(DEFAULTS);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedAt, setSavedAt] = useState<number | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const opts = await adminGetWritingOptions();
      setServer(opts);
      setDraft(toEditable(opts));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load Writing options');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const dirty = server ? JSON.stringify(toEditable(server)) !== JSON.stringify(draft) : false;
  const killSwitchActive = !draft.aiGradingEnabled || !draft.aiCoachEnabled;

  const handleReset = () => {
    if (server) setDraft(toEditable(server));
  };

  const handleSave = async () => {
    setSaving(true);
    setError(null);
    try {
      const payload: EditableOptions = {
        ...draft,
        // Normalise empty strings to null for nullable fields.
        killSwitchReason: draft.killSwitchReason?.trim() ? draft.killSwitchReason.trim() : null,
        freeTierLimit: Math.max(0, Math.floor(Number(draft.freeTierLimit) || 0)),
        freeTierWindowDays: Math.max(1, Math.floor(Number(draft.freeTierWindowDays) || 7)),
      };
      const saved = await adminUpdateWritingOptions(payload);
      setServer(saved);
      setDraft(toEditable(saved));
      setSavedAt(Date.now());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save Writing options');
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <main className="mx-auto max-w-3xl space-y-4 p-4 sm:p-6">
        <Skeleton className="h-10 w-72" />
        <Skeleton className="h-4 w-96" />
        <Skeleton className="h-48 w-full" />
        <Skeleton className="h-48 w-full" />
        <Skeleton className="h-48 w-full" />
      </main>
    );
  }

  return (
    <main
      className="mx-auto max-w-3xl space-y-6 p-4 sm:p-6"
      aria-label="Writing AI Options"
    >
      <motion.div
        initial={{ opacity: 0, y: -8 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.25 }}
      >
        <Card className="p-5">
          <div className="flex items-start gap-3">
            <span className="rounded-lg bg-primary/10 p-2 text-primary">
              <PenSquare className="h-5 w-5" />
            </span>
            <div className="min-w-0">
              <h1 className="text-xl font-semibold">Writing AI Options</h1>
              <p className="mt-1 text-sm text-muted">
                Control the Writing AI kill-switch (grading + coach), the free-tier
                entitlement that gates non-paying learners, and global learner access.
                Changes apply immediately.
              </p>
            </div>
          </div>
        </Card>
      </motion.div>

      {error && (
        <motion.div
          initial={{ opacity: 0, y: -4 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.2 }}
          role="alert"
        >
          <Card className="border-danger/40 bg-danger/5 p-4">
            <div className="flex items-start gap-2 text-sm text-danger">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <span>{error}</span>
            </div>
          </Card>
        </motion.div>
      )}

      {savedAt && !error && (
        <motion.div
          key={savedAt}
          initial={{ opacity: 0, y: -4 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.2 }}
          role="status"
        >
          <Card className="border-success/40 bg-success/5 p-4">
            <div className="flex items-center gap-2 text-sm text-success">
              <CheckCircle2 className="h-4 w-4" />
              <span>Writing options saved.</span>
            </div>
          </Card>
        </motion.div>
      )}

      {/* Section 1 — AI Kill Switches */}
      <motion.section
        initial={{ opacity: 0, y: 8 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.25, delay: 0.05 }}
        aria-labelledby="ai-kill-switches-heading"
      >
        <Card className="p-5">
          <h2 id="ai-kill-switches-heading" className="text-base font-semibold">
            AI Kill Switches
          </h2>
          <p className="mt-1 text-xs text-muted">
            Disabling either toggle immediately stops the corresponding Writing AI
            feature for every learner. Record a brief reason so future admins
            understand why it was turned off.
          </p>
          <div className="mt-2 divide-y divide-border">
            <ToggleRow
              label="AI Grading"
              description="When off, AI Writing scoring is disabled and submissions queue for human review only."
              value={draft.aiGradingEnabled}
              onChange={(v) => setDraft((d) => ({ ...d, aiGradingEnabled: v }))}
            />
            <ToggleRow
              label="AI Coach"
              description="When off, the in-editor Writing Coach (suggestions, rewrites) is disabled."
              value={draft.aiCoachEnabled}
              onChange={(v) => setDraft((d) => ({ ...d, aiCoachEnabled: v }))}
            />
          </div>

          {killSwitchActive && (
            <motion.div
              initial={{ opacity: 0, height: 0 }}
              animate={{ opacity: 1, height: 'auto' }}
              transition={{ duration: 0.2 }}
              className="mt-3"
            >
              <label className="block">
                <span className="text-xs font-medium text-navy">
                  Kill-switch reason
                </span>
                <textarea
                  value={draft.killSwitchReason ?? ''}
                  onChange={(e) =>
                    setDraft((d) => ({ ...d, killSwitchReason: e.target.value }))
                  }
                  placeholder="e.g. Provider incident — fall back to human review until resolved."
                  rows={3}
                  className="mt-1 w-full rounded-lg border bg-muted/30 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary/50"
                  aria-label="Kill-switch reason"
                />
              </label>
            </motion.div>
          )}
        </Card>
      </motion.section>

      {/* Section 2 — Entitlement (Free Tier) */}
      <motion.section
        initial={{ opacity: 0, y: 8 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.25, delay: 0.1 }}
        aria-labelledby="entitlement-heading"
      >
        <Card className="p-5">
          <h2 id="entitlement-heading" className="text-base font-semibold">
            Entitlement (Free Tier)
          </h2>
          <p className="mt-1 text-xs text-muted">
            By default Writing AI is premium-only. Turn the free tier on to grant
            non-paying learners a quota per rolling window. A limit of 0 keeps
            access premium-only even when the tier is enabled.
          </p>
          <div className="mt-2 divide-y divide-border">
            <ToggleRow
              label="Free tier enabled"
              description="If off, only paying learners can use Writing AI grading + coach."
              value={draft.freeTierEnabled}
              onChange={(v) => setDraft((d) => ({ ...d, freeTierEnabled: v }))}
            />
          </div>

          <div className="mt-4 grid gap-4 sm:grid-cols-2">
            <label className="block">
              <span className="text-xs font-medium text-navy">
                Free tier limit (uses per window)
              </span>
              <input
                type="number"
                min={0}
                step={1}
                value={draft.freeTierLimit}
                onChange={(e) =>
                  setDraft((d) => ({
                    ...d,
                    freeTierLimit: Number(e.target.value),
                  }))
                }
                className="mt-1 w-full rounded-lg border bg-muted/30 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary/50"
                aria-label="Free tier limit"
              />
              <span className="mt-1 block text-[11px] text-muted">
                0 = premium-only access.
              </span>
            </label>

            <label className="block">
              <span className="text-xs font-medium text-navy">
                Window length (days)
              </span>
              <input
                type="number"
                min={1}
                step={1}
                value={draft.freeTierWindowDays}
                onChange={(e) =>
                  setDraft((d) => ({
                    ...d,
                    freeTierWindowDays: Number(e.target.value),
                  }))
                }
                className="mt-1 w-full rounded-lg border bg-muted/30 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary/50"
                aria-label="Free tier window days"
              />
              <span className="mt-1 block text-[11px] text-muted">
                Default 7 days (rolling).
              </span>
            </label>
          </div>
        </Card>
      </motion.section>

      {/* Footer / actions */}
      <div className="sticky bottom-[calc(var(--bottom-nav-height,0px)+0.5rem)] z-10 -mx-4 border-t bg-background/95 px-4 py-4 backdrop-blur lg:bottom-0">
        <div className="mx-auto flex max-w-3xl flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          <p className="text-[11px] text-muted">
            {server?.updatedAt
              ? `Last saved ${new Date(server.updatedAt).toLocaleString()}`
              : 'Not yet saved'}
            {server?.updatedByAdminId ? ` · by ${server.updatedByAdminId}` : ''}
          </p>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={handleReset}
              disabled={!dirty || saving}
            >
              <RotateCcw className="mr-1.5 h-4 w-4" />
              Reset to last saved
            </Button>
            <Button
              variant="primary"
              size="sm"
              onClick={handleSave}
              disabled={!dirty || saving}
              loading={saving}
            >
              <Save className="mr-1.5 h-4 w-4" />
              {saving ? 'Saving…' : 'Save changes'}
            </Button>
          </div>
        </div>
      </div>
    </main>
  );
}
