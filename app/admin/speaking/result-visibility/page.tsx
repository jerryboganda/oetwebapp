'use client';

/**
 * Admin · Speaking · Result visibility.
 *
 * Controls which parts of a graded Speaking result are revealed to the learner.
 * Each toggle maps 1:1 to a boolean field on `SpeakingResultVisibilityDto`.
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
  adminGetSpeakingResultVisibility,
  adminUpsertSpeakingResultVisibility,
} from '@/lib/api/speaking-result-visibility';
import type { SpeakingResultVisibilityDto } from '@/lib/api/speaking-result-visibility';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Speaking', href: '/admin/speaking' },
  { label: 'Result visibility' },
];

type VisibilityKey = keyof SpeakingResultVisibilityDto;

interface ToggleSpec {
  key: VisibilityKey;
  label: string;
  description: string;
}

const DEFAULTS: SpeakingResultVisibilityDto = {
  rolePlayCardId: null,
  showSubmissionReceived: true,
  showAiEstimate: true,
  showReadinessBand: true,
  showTutorScore: true,
  showFullCriteria: true,
  showTranscript: true,
  showTutorComments: true,
  showRecommendedDrills: true,
  allowReattempt: true,
  updatedAt: '',
};

const SCORE_TOGGLES: ToggleSpec[] = [
  {
    key: 'showSubmissionReceived',
    label: 'Submission received',
    description: 'Confirm that the learner recording was received and queued for marking.',
  },
  {
    key: 'showAiEstimate',
    label: 'AI estimate',
    description: 'Reveal the provisional AI estimate before a tutor review is complete.',
  },
  {
    key: 'showReadinessBand',
    label: 'Readiness band',
    description: 'Reveal the readiness band label alongside the estimated scaled score.',
  },
  {
    key: 'showTutorScore',
    label: 'Tutor score',
    description: 'Reveal the tutor / moderated result once the review is complete.',
  },
  {
    key: 'showFullCriteria',
    label: 'Full criteria breakdown',
    description: 'Show the per-criterion speaking scores, not just the summary score.',
  },
];

const DETAIL_TOGGLES: ToggleSpec[] = [
  {
    key: 'showTranscript',
    label: 'Transcript',
    description: 'Allow learners to review the role-play transcript.',
  },
  {
    key: 'showTutorComments',
    label: 'Tutor comments',
    description: 'Expose tutor annotations on the transcript if they exist.',
  },
  {
    key: 'showRecommendedDrills',
    label: 'Recommended drills',
    description: 'Show follow-up drills derived from the assessment.',
  },
  {
    key: 'allowReattempt',
    label: 'Allow reattempt',
    description: 'Let the learner start another attempt from the Speaking check flow.',
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

export default function SpeakingResultVisibilityPage() {
  const [server, setServer] = useState<SpeakingResultVisibilityDto | null>(null);
  const [draft, setDraft] = useState<SpeakingResultVisibilityDto>(DEFAULTS);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedAt, setSavedAt] = useState<number | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const dto = await adminGetSpeakingResultVisibility();
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
    setDraft((current) => ({ ...current, [key]: value }));

  const handleSave = async () => {
    setSaving(true);
    setError(null);
    try {
      const saved = await adminUpsertSpeakingResultVisibility({ ...draft, rolePlayCardId: null });
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
        eyebrow="Speaking"
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
      description="Control which parts of a graded Speaking result are revealed to learners. Changes apply to the global default until you add a card override."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Speaking"
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
              value={draft[spec.key] === true}
              onChange={(next) => setField(spec.key, next)}
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
              value={draft[spec.key] === true}
              onChange={(next) => setField(spec.key, next)}
            />
          ))}
        </div>
      </SettingsSection>
    </AdminSettingsLayout>
  );
}