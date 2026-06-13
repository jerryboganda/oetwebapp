'use client';

/**
 * Entry point for a new Speaking mock set. The backend requires a title and
 * two distinct speaking role-plays to create, so this screen collects those
 * essentials, creates the draft, then routes into the wizard for the rest.
 */

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowLeft, ArrowRight, Loader2, MessageSquareText } from 'lucide-react';
import { AdminRouteHero, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { createAdminSpeakingMockSet, fetchAdminSpeakingContentOptions } from '@/lib/api';
import { buildMockSetStepHref, MOCK_SET_PROFESSION_OPTIONS } from '@/components/domain/speaking/mock-set-wizard/mock-set-wizard-config';

type ContentOption = { id: string; title: string; status: string };

export default function NewSpeakingMockSetPage() {
  const router = useRouter();
  const [options, setOptions] = useState<ContentOption[]>([]);
  const [title, setTitle] = useState('');
  const [professionId, setProfessionId] = useState('nursing');
  const [rolePlay1, setRolePlay1] = useState('');
  const [rolePlay2, setRolePlay2] = useState('');
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchAdminSpeakingContentOptions()
      .then(setOptions)
      .catch(() => setError('Could not load speaking content options.'));
  }, []);

  const duplicate = Boolean(rolePlay1) && rolePlay1 === rolePlay2;
  const canCreate = title.trim().length > 0 && Boolean(rolePlay1) && Boolean(rolePlay2) && !duplicate;

  const selectOptions = [
    { value: '', label: 'Select…' },
    ...options.map((o) => ({ value: o.id, label: `${o.title} [${o.status}]` })),
  ];

  async function handleCreate() {
    if (!canCreate) return;
    setCreating(true);
    setError(null);
    try {
      const created = await createAdminSpeakingMockSet({
        title: title.trim(),
        professionId,
        rolePlay1ContentId: rolePlay1,
        rolePlay2ContentId: rolePlay2,
      });
      router.replace(buildMockSetStepHref(created.mockSetId, 'details'));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create the mock set.');
      setCreating(false);
    }
  }

  return (
    <AdminRouteWorkspace>
      <AdminRouteHero
        eyebrow="Speaking"
        title="New mock set"
        description="Bundle two published speaking role-plays into one official OET-shape speaking mock. Pick the essentials here, then refine details and exam assets in the wizard."
        icon={MessageSquareText}
        accent="primary"
      />
      <AdminRoutePanel>
        <div className="space-y-4 py-1">
          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

          <Input label="Title" value={title} onChange={(e) => setTitle(e.target.value)} placeholder="e.g. Nursing Mock Set 3 — Discharge planning" maxLength={200} required />
          <Select label="Profession" value={professionId} onChange={(e) => setProfessionId(e.target.value)} options={MOCK_SET_PROFESSION_OPTIONS} />

          <div className="grid gap-4 md:grid-cols-2">
            <Select label="Role-play 1" value={rolePlay1} onChange={(e) => setRolePlay1(e.target.value)} options={selectOptions} required />
            <Select label="Role-play 2" value={rolePlay2} onChange={(e) => setRolePlay2(e.target.value)} options={selectOptions} required />
          </div>
          {duplicate ? <p className="text-xs text-danger">Role-play 1 and 2 must be different content items.</p> : null}

          <div className="flex flex-wrap items-center gap-3 border-t border-border pt-4">
            <Button variant="primary" onClick={() => void handleCreate()} disabled={!canCreate || creating}>
              {creating ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : null}
              Create draft &amp; open wizard <ArrowRight className="ml-1 h-4 w-4" />
            </Button>
            <Button variant="ghost" onClick={() => router.push('/admin/speaking')} disabled={creating}>
              <ArrowLeft className="mr-1 h-4 w-4" /> Back to Speaking hub
            </Button>
          </div>
        </div>
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
