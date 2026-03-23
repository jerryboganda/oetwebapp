import { SpeakingAttemptPage } from "@/Component/OET/Learner/LearnerDynamicPages";

export default async function SpeakingAttemptRoute({
  params,
}: {
  params: Promise<{ attemptId: string }>;
}) {
  const { attemptId } = await params;
  return <SpeakingAttemptPage attemptId={attemptId} />;
}
