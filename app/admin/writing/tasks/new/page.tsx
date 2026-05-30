'use client';

/**
 * Admin · Writing · New task.
 *
 * Repointed (WS-F2) to render the rich WritingTaskBuilder in "new" mode. The
 * builder starts from a blank form and, on first save, creates the task via
 * `createWritingTask` and redirects into the canonical edit route
 * (`/admin/writing/tasks/{id}/edit`).
 *
 * Integrity-acknowledgement, source-provenance, and permission gating
 * (`useAdminAuth` + `hasPermission(..., ContentWrite)`) are all enforced inside
 * the builder, preserving the guarantees of the previous standalone page. The
 * authoring-started analytics event is preserved here for funnel continuity.
 */

import { useEffect } from 'react';

import { WritingTaskBuilder } from '@/components/domain/writing/admin';
import { analytics } from '@/lib/analytics';

export default function NewWritingTaskPage() {
  useEffect(() => {
    analytics.track('admin_writing_task_authoring_started');
  }, []);

  return <WritingTaskBuilder mode="new" />;
}
