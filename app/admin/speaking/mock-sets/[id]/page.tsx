'use client';

import { useEffect } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { buildMockSetStepHref } from '@/components/domain/speaking/mock-set-wizard/mock-set-wizard-config';

/** A bare mock-set URL lands on the first wizard step. */
export default function MockSetIndexPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const mockSetId = params?.id ?? '';

  useEffect(() => {
    if (mockSetId) router.replace(buildMockSetStepHref(mockSetId, 'details'));
  }, [mockSetId, router]);

  return null;
}
