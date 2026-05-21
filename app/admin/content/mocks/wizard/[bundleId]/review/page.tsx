import { StepReview } from '@/components/domain/mock-wizard/StepReview';
import { AdminMocksReviewStageStepper } from '@/components/admin/admin-mocks-review-stage-stepper';

// The route page hosts both the Phase 3 multi-stage review stepper (academic →
// medical → language → technical → pilot → published) and the pre-existing
// StepReview publish-gate UI so existing publish behaviour is preserved.
export default async function ReviewStepPage({
  params,
}: {
  params: Promise<{ bundleId: string }>;
}) {
  const { bundleId } = await params;
  return (
    <div className="space-y-6">
      <AdminMocksReviewStageStepper bundleId={bundleId} />
      <StepReview />
    </div>
  );
}
