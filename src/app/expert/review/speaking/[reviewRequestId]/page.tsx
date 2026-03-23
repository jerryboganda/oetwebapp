import { ExpertSpeakingReviewPage } from "@/Component/OET/Expert/ExpertPages";

export default async function ExpertSpeakingReviewRoute({
  params,
}: {
  params: Promise<{ reviewRequestId: string }>;
}) {
  const { reviewRequestId } = await params;
  return <ExpertSpeakingReviewPage reviewRequestId={reviewRequestId} />;
}
