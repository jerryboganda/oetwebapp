'use client';

/**
 * Admin · Writing · Task Builder (edit route).
 *
 * Renders the rich WritingTaskBuilder for an existing task id. The builder owns
 * loading, editing, validation, publish/clone/archive, and import/export, plus
 * permission gating (ContentWrite / ContentPublish) and the integrity
 * acknowledgement.
 *
 * Spec §3/§4/§5/§6/§18/§19.2.
 */

import { useParams } from 'next/navigation';

import { WritingTaskBuilder } from '@/components/domain/writing/admin';

export default function EditWritingTaskPage() {
  const params = useParams<{ id: string }>();
  const rawId = params?.id;
  const id = Array.isArray(rawId) ? rawId[0] : rawId;

  if (!id) {
    return null;
  }

  return <WritingTaskBuilder taskId={id} mode="edit" />;
}
