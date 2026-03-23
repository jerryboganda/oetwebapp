import { ExpertWritingReviewPage } from "@/Component/OET/Expert/ExpertPages";

export default async function ExpertWritingReviewRoute({
  params,
}: {
  params: Promise<{ reviewRequestId: string }>;
}) {
  const { reviewRequestId } = await params;
  return <ExpertWritingReviewPage reviewRequestId={reviewRequestId} />;
}
