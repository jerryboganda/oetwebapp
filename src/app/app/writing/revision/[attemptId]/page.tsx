import { WritingRevisionPage } from "@/Component/OET/Learner/LearnerDynamicPages";

export default async function WritingRevisionRoute({
  params,
}: {
  params: Promise<{ attemptId: string }>;
}) {
  const { attemptId } = await params;
  return <WritingRevisionPage attemptId={attemptId} />;
}
