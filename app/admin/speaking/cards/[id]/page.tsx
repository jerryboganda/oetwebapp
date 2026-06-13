'use client';

import { useEffect } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { buildCardStepHref } from '@/components/domain/speaking/wizard/card-wizard-config';

/** A bare card URL lands on the first wizard step. */
export default function CardIndexPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const cardId = params?.id ?? '';

  useEffect(() => {
    if (cardId) router.replace(buildCardStepHref(cardId, 'classification'));
  }, [cardId, router]);

  return null;
}
