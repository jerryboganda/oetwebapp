'use client';

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { ArrowLeft, CalendarPlus } from 'lucide-react';

import { TutorRouteHero, TutorRouteWorkspace } from '@/components/domain/tutor-route-surface';
import { ClassEditorForm } from '@/components/tutor/ClassEditorForm';
import { createTutorClass, type TutorClassCreatePayload } from '@/lib/api';

export default function NewTutorClassPage() {
  const router = useRouter();
  const [submitting, setSubmitting] = useState(false);
  const [apiError, setApiError] = useState<string | null>(null);

  async function handleSubmit(payload: TutorClassCreatePayload) {
    setSubmitting(true);
    setApiError(null);
    try {
      const created = await createTutorClass(payload);
      router.push(`/tutor/classes/${created.id}`);
    } catch (err: unknown) {
      setApiError(err instanceof Error ? err.message : 'Could not create class.');
      setSubmitting(false);
    }
  }

  return (
    <TutorRouteWorkspace>
      <Link href="/tutor/classes" className="inline-flex items-center gap-1.5 text-sm text-muted hover:text-navy">
        <ArrowLeft className="h-4 w-4" /> Back to classes
      </Link>

      <TutorRouteHero
        title="Schedule a class"
        description="Fill in the details below. You can keep it as a draft or publish immediately."
        icon={CalendarPlus}
      />

      <ClassEditorForm
        onSubmit={handleSubmit}
        onCancel={() => router.push('/tutor/classes')}
        submitting={submitting}
        apiError={apiError}
      />
    </TutorRouteWorkspace>
  );
}
