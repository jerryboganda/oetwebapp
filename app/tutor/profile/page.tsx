'use client';

import { useEffect, useState, type FormEvent } from 'react';
import { ExternalLink, User } from 'lucide-react';

import { TutorRouteHero, TutorRouteSectionHeader, TutorRouteWorkspace } from '@/components/domain/tutor-route-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Input, Textarea } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import {
  createTutorProfile,
  fetchTutorProfile,
  provisionTutorZoomUser,
  updateTutorProfile,
  type TutorProfile,
  type TutorUpsertPayload,
} from '@/lib/api';

const FALLBACK_TZ =
  typeof Intl !== 'undefined' ? Intl.DateTimeFormat().resolvedOptions().timeZone : 'UTC';

interface FormState {
  displayName: string;
  displayNameAr: string;
  bio: string;
  bioAr: string;
  avatarUrl: string;
  specialties: string; // comma-separated
  languages: string; // comma-separated
  hourlyRateUsd: string;
  timeZone: string;
}

function fromProfile(p: TutorProfile | null): FormState {
  return {
    displayName: p?.displayName ?? '',
    displayNameAr: p?.displayNameAr ?? '',
    bio: p?.bio ?? '',
    bioAr: p?.bioAr ?? '',
    avatarUrl: p?.avatarUrl ?? '',
    specialties: (p?.specialties ?? []).join(', '),
    languages: (p?.languages ?? []).join(', '),
    hourlyRateUsd: p?.hourlyRateUsd != null ? String(p.hourlyRateUsd) : '',
    timeZone: p?.timeZone ?? FALLBACK_TZ,
  };
}

function toPayload(form: FormState): TutorUpsertPayload {
  return {
    displayName: form.displayName.trim(),
    displayNameAr: form.displayNameAr.trim() || null,
    bio: form.bio.trim() || null,
    bioAr: form.bioAr.trim() || null,
    avatarUrl: form.avatarUrl.trim() || null,
    specialties: form.specialties.split(',').map((s) => s.trim()).filter(Boolean),
    languages: form.languages.split(',').map((s) => s.trim()).filter(Boolean),
    hourlyRateUsd: form.hourlyRateUsd === '' ? null : Number(form.hourlyRateUsd),
    timeZone: form.timeZone.trim() || null,
  };
}

export default function TutorProfilePage() {
  const [profile, setProfile] = useState<TutorProfile | null>(null);
  const [form, setForm] = useState<FormState>(fromProfile(null));
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [provisioning, setProvisioning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    fetchTutorProfile()
      .then((p) => {
        if (cancelled) return;
        setProfile(p);
        setForm(fromProfile(p));
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load profile.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  function update<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((prev) => ({ ...prev, [key]: value }));
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    setSuccess(null);
    try {
      if (!form.displayName.trim()) {
        setError('Display name is required.');
        return;
      }
      const payload = toPayload(form);
      const saved = profile ? await updateTutorProfile(payload) : await createTutorProfile(payload);
      setProfile(saved);
      setSuccess('Profile saved.');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not save profile.');
    } finally {
      setSaving(false);
    }
  }

  async function handleProvisionZoom() {
    setProvisioning(true);
    setError(null);
    setSuccess(null);
    try {
      const res = await provisionTutorZoomUser();
      setSuccess(res.zoomUserId ? `Zoom user provisioned (${res.zoomUserId}).` : 'Zoom provisioning request accepted.');
      const refreshed = await fetchTutorProfile();
      setProfile(refreshed);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not provision Zoom user.');
    } finally {
      setProvisioning(false);
    }
  }

  return (
    <TutorRouteWorkspace>
      <TutorRouteHero
        title="Tutor profile"
        description="Public profile shown to learners. Bilingual fields display when an Arabic version is provided."
        icon={User}
      />

      {error ? (
        <InlineAlert variant="warning" className="flex items-center justify-between gap-3">
          <span>{error}</span>
          <Button type="button" variant="ghost" size="sm" onClick={() => setError(null)}>
            Dismiss
          </Button>
        </InlineAlert>
      ) : null}
      {success ? (
        <InlineAlert variant="success" className="flex items-center justify-between gap-3">
          <span>{success}</span>
          <Button type="button" variant="ghost" size="sm" onClick={() => setSuccess(null)}>
            Dismiss
          </Button>
        </InlineAlert>
      ) : null}

      {loading ? (
        <div className="space-y-3">
          <Skeleton className="h-32 rounded-2xl" />
          <Skeleton className="h-32 rounded-2xl" />
        </div>
      ) : (
        <form onSubmit={(e) => void handleSubmit(e)} className="space-y-6" noValidate>
          <section className="space-y-4 rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <TutorRouteSectionHeader eyebrow="Identity" title="Display name & bio" />
            <div className="grid gap-4 sm:grid-cols-2">
              <Input
                label="Display name"
                value={form.displayName}
                onChange={(e) => update('displayName', e.target.value)}
                required
                maxLength={120}
              />
              <Input
                label="Display name (Arabic)"
                value={form.displayNameAr}
                onChange={(e) => update('displayNameAr', e.target.value)}
                dir="rtl"
                lang="ar"
                maxLength={120}
              />
            </div>
            <Textarea
              label="Bio"
              value={form.bio}
              onChange={(e) => update('bio', e.target.value)}
              maxLength={2000}
            />
            <Textarea
              label="Bio (Arabic)"
              value={form.bioAr}
              onChange={(e) => update('bioAr', e.target.value)}
              dir="rtl"
              lang="ar"
              maxLength={2000}
            />
            <Input
              type="url"
              label="Avatar URL"
              value={form.avatarUrl}
              onChange={(e) => update('avatarUrl', e.target.value)}
              placeholder="https://cdn.example.com/me.jpg"
            />
          </section>

          <section className="space-y-4 rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <TutorRouteSectionHeader eyebrow="Teaching" title="Specialties, languages & rate" />
            <Input
              label="Specialties"
              value={form.specialties}
              onChange={(e) => update('specialties', e.target.value)}
              hint="Comma-separated, e.g. speaking, IELTS, medicine"
            />
            <Input
              label="Languages"
              value={form.languages}
              onChange={(e) => update('languages', e.target.value)}
              hint="Comma-separated, e.g. English, Arabic"
            />
            <div className="grid gap-4 sm:grid-cols-2">
              <Input
                type="number"
                step="0.01"
                min={0}
                label="Hourly rate (USD)"
                value={form.hourlyRateUsd}
                onChange={(e) => update('hourlyRateUsd', e.target.value)}
                placeholder="e.g. 45.00"
              />
              <Input
                label="Time zone"
                value={form.timeZone}
                onChange={(e) => update('timeZone', e.target.value)}
                placeholder="Australia/Sydney"
                hint="IANA time zone identifier."
              />
            </div>
          </section>

          <section className="space-y-4 rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <TutorRouteSectionHeader
              eyebrow="Zoom"
              title="Host integration"
              description="Required before you can host live classes. Provisioning creates your Zoom user automatically."
            />
            <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
              <span className="text-sm text-muted">
                {profile?.zoomUserId ? (
                  <>Zoom user ID: <span className="font-mono text-navy">{profile.zoomUserId}</span></>
                ) : (
                  'Not yet provisioned.'
                )}
              </span>
              <Button
                type="button"
                variant="outline"
                size="sm"
                loading={provisioning}
                onClick={() => void handleProvisionZoom()}
              >
                {profile?.zoomUserId ? 'Re-provision Zoom user' : 'Provision Zoom user'}
              </Button>
            </div>
          </section>

          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            {profile ? (
              <a
                href={`/tutors/${profile.id}`}
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center gap-1.5 text-sm text-primary hover:underline"
              >
                <ExternalLink className="h-4 w-4" /> Public preview
              </a>
            ) : (
              <span className="text-sm text-muted">Save first to enable public preview.</span>
            )}
            <Button type="submit" variant="primary" loading={saving}>
              {profile ? 'Save changes' : 'Create profile'}
            </Button>
          </div>
        </form>
      )}
    </TutorRouteWorkspace>
  );
}
