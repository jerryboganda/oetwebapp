import { WritingResultPage } from "@/Component/OET/Learner/LearnerDynamicPages";

export default async function WritingResultRoute({
  params,
}: {
  params: Promise<{ evaluationId: string }>;
}) {
  const { evaluationId } = await params;
  return <WritingResultPage evaluationId={evaluationId} />;
}
