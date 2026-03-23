import { SpeakingReviewPage } from "@/Component/OET/Learner/LearnerDynamicPages";

export default async function SpeakingReviewRoute({
  params,
}: {
  params: Promise<{ attemptId: string }>;
}) {
  const { attemptId } = await params;
  return <SpeakingReviewPage attemptId={attemptId} />;
}
