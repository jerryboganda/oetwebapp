'use client';

import { LearnerRouteError, type LearnerRouteErrorProps } from '@/components/domain/learner-route-error';

export default function CommunityError(props: LearnerRouteErrorProps) {
  return (
    <LearnerRouteError
      {...props}
      title="Something went wrong"
      message="An unexpected error occurred in Community. Please try again."
    />
  );
}
