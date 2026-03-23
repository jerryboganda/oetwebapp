import { WritingModelAnswerPage } from "@/Component/OET/Learner/LearnerDynamicPages";

export default async function WritingModelAnswerRoute({
  params,
}: {
  params: Promise<{ contentId: string }>;
}) {
  const { contentId } = await params;
  return <WritingModelAnswerPage contentId={contentId} />;
}
