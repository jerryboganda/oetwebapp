'use client';

import { LearnerRouteError, type LearnerRouteErrorProps } from '@/components/domain/learner-route-error';

export default function Error(props: LearnerRouteErrorProps) {
  return <LearnerRouteError {...props} title="Speaking Practice Error" />;
}
