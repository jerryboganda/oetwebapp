import { WritingAttemptPage } from "@/Component/OET/Learner/LearnerDynamicPages";

export default async function WritingAttemptRoute({
  params,
}: {
  params: Promise<{ attemptId: string }>;
}) {
  const { attemptId } = await params;
  return <WritingAttemptPage attemptId={attemptId} />;
}
