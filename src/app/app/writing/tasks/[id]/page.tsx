import { WritingTaskDetailPage } from "@/Component/OET/Learner/LearnerDynamicPages";

export default async function WritingTaskRoute({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;
  return <WritingTaskDetailPage taskId={id} />;
}
