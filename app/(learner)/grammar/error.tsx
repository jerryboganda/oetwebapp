'use client';

import { LearnerRouteError, type LearnerRouteErrorProps } from '@/components/domain/learner-route-error';

export default function GrammarError(props: LearnerRouteErrorProps) {
  return (
    <LearnerRouteError
      {...props}
      title="We couldn't load this view"
      message="Something went wrong while loading grammar. Try again, or head back to your dashboard if the issue persists."
    />
  );
}
