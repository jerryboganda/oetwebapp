'use client';

/**
 * Admin · Writing · Result visibility.
 *
 * Controls which parts of a graded Writing result are revealed to the learner
 * (spec §15.1): submission receipt, AI estimate, tutor score, full criteria,
 * the annotated response, missing-content list, model answer, content checklist,
 * and whether a rewrite is offered. Each toggle maps 1:1 to a boolean field on
 * `WritingResultVisibilityDto`. Backed by `getResultVisibilityConfig` /
 * `updateResultVisibilityConfig` (the marking/feedback contract). The settings
 * apply to the global default (no scenario override is edited here).
 */

import { useCallback, useEffect, useState } from 'react';
import { motion } from 'motion/react';
import {
  AlertTriangle,
  CheckCircle2,
  Eye,
  RotateCcw,
  Save,
  ToggleLeft,
  ToggleRight,
} from 'lucide-react';
import {
  AdminSettingsLayout,
  SettingsSection,
} from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Skeleton } from '@/components/admin/ui/skeleton';
import {
  getResultVisibilityConfig,
  updateResultVisibilityConfig,
} from '@/lib/writing/exam-api';
import type { WritingResultVisibilityDto } from '@/lib/writing/types';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Writing', href: '/admin/writing' },
  { label: 'Result visibility' },
];

type VisibilityKey = keyof WritingResultVisibilityDto;

interface ToggleSpec {
  key: VisibilityKey;
  label: string;
  description: string;
}

/** Sensible default = receipt + AI estimate only, until a result is reviewed. */
const DEFAULTS: WritingResultVisibilityDto = {
  showSubmissionReceived: true,
  showAiEstimate: true,
  showTutorScore: true,
  showFullCriteria: true,
  showAnnotatedResponse: true,
  showMissingContent: true,
  showModelAnswer: false,
  showContentChecklist: true,
  allowRewrite: true,
};

const SCORE_TOGGLES: ToggleSpec[] = [
  {
    key: 'showSubmissionReceived',
    label: 'Submission received',
    description: 'Confirm to the learner that their letter was received and is queued for review.',
  },
  {
    key: 'showAiEstimate',
    label: 'AI estimate',
    description: 'Reveal the provisional AI band estimate before a tutor reviews.',
  },
  {
    key: 'showTutorScore',
    label: 'Tutor score',
    description: 'Reveal the tutor / moderated score once the review is complete.',
  },
  {
    key: 'showFullCriteria',
    label: 'Full criteria breakdown',
    description: 'Show the per-criterion (C1–C6) scores, not just the overall band.',
  },
];

const DETAIL_TOGGLES: ToggleSpec[] = [
  {
    key: 'showAnnotatedResponse',
    label: 'Annotated response',
    description: "Show the learner's own letter with the tutor's inline annotations.",
  },
  {
    key: 'showMissingContent',
    label: 'Missing content',
    description: 'List the key content points the letter omitted.',
  },
  {
    key: 'showContentChecklist',
    label: 'Content checklist',
    description: 'Show how each key and irrelevant content point was handled.',
  },
  {
    key: 'showModelAnswer',
    label: 'Model answer',
    description: 'Reveal the reference model letter. Off by default to protect authored content.',
  },
  {
    key: 'allowRewrite',
    label: 'Allow rewrite',
    description: 'Let the learner submit a graded rewrite of this task.',
  },
];

function ToggleRow({
  spec,
  value,
  onChange,
}: {
  spec: ToggleSpec;
  value: boolean;
  onChange: (next: boolean) => void;
}) {
  return (
    <div className="flex items-start justify-between gap-4 py-3">
      <div className="min-w-0">
        <span className="text-sm font-medium text-admin-fg-strong">{spec.label}</span>
        <p className="mt-1 text-xs text-admin-fg-muted">{spec.description}</p>
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={value}
        aria-label={spec.label}
        onClick={() => onChange(!value)}
        className="shrink-0"
      >
        {value ? (
          <ToggleRight className="h-7 w-7 text-admin-success" />
        ) : (
          <ToggleLeft className="h-7 w-7 text-admin-fg-muted" />
        )}
      </button>
    </div>
  );
}

export default function WritingResultVisibilityPage() {
  const [server, setServer] = useState<WritingResultVisibilityDto | null>(null);
  const [draft, setDraft] = useState<WritingResultVisibilityDto>(DEFAULTS);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedAt, setSavedAt] = useState<number | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const dto = await getResultVisibilityConfig();
      setServer(dto);
      setDraft(dto);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load result visibility');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const dirty = server ? JSON.stringify(server) !== JSON.stringify(draft) : false;

  const handleReset = () => {
    if (server) setDraft(server);
  };

  const setField = (key: VisibilityKey, value: boolean) =>
    setDraft((d) => ({ ...d, [key]: value }));

  const handleSave = async () => {
    setSaving(true);
    setError(null);
    try {
      const saved = await updateResultVisibilityConfig({ ...draft, scenarioId: null });
      setServer(saved);
      setDraft(saved);
      setSavedAt(Date.now());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save result visibility');
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <AdminSettingsLayout
        title="Result visibility"
        breadcrumbs={BREADCRUMBS}
        eyebrow="Writing"
        icon={<Eye className="h-5 w-5" />}
      >
        <Skeleton className="h-48 w-full" />
        <Skeleton className="h-48 w-full" />
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      title="Result visibility"
      description="Control which parts of a graded Writing result are revealed to learners. Changes apply to feedback rendered after you save."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Writing"
      icon={<Eye className="h-5 w-5" />}
      actions={(
        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" onClick={handleReset} disabled={!dirty || saving}>
            <RotateCcw className="mr-1.5 h-4 w-4" />
            Reset
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
      )}
    >
      {error && (
        <motion.div
          initial={{ opacity: 0, y: -4 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.2 }}
          role="alert"
        >
          <Card surface="tinted-danger">
            <CardContent className="p-4">
              <div className="flex items-start gap-2 text-sm text-admin-danger">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                <span>{error}</span>
              </div>
            </CardContent>
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
          <Card surface="tinted-success">
            <CardContent className="p-4">
              <div className="flex items-center gap-2 text-sm text-admin-success">
                <CheckCircle2 className="h-4 w-4" />
                <span>Result visibility saved.</span>
              </div>
            </CardContent>
          </Card>
        </motion.div>
      )}

      <SettingsSection
        title="Scores"
        description="What scoring information the learner can see, and when."
      >
        <div className="divide-y divide-admin-border">
          {SCORE_TOGGLES.map((spec) => (
            <ToggleRow
              key={spec.key}
              spec={spec}
              value={draft[spec.key]}
              onChange={(v) => setField(spec.key, v)}
            />
          ))}
        </div>
      </SettingsSection>

      <SettingsSection
        title="Feedback detail"
        description="How much marking detail and which study aids are released to the learner."
      >
        <div className="divide-y divide-admin-border">
          {DETAIL_TOGGLES.map((spec) => (
            <ToggleRow
              key={spec.key}
              spec={spec}
              value={draft[spec.key]}
              onChange={(v) => setField(spec.key, v)}
            />
          ))}
        </div>
      </SettingsSection>
    </AdminSettingsLayout>
  );
}
