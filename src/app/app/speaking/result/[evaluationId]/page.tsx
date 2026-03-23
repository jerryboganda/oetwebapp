import { SpeakingResultPage } from "@/Component/OET/Learner/LearnerDynamicPages";

export default async function SpeakingResultRoute({
  params,
}: {
  params: Promise<{ evaluationId: string }>;
}) {
  const { evaluationId } = await params;
  return <SpeakingResultPage evaluationId={evaluationId} />;
}
