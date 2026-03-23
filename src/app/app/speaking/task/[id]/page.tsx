import { SpeakingTaskDetailPage } from "@/Component/OET/Learner/LearnerDynamicPages";

export default async function SpeakingTaskRoute({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;
  return <SpeakingTaskDetailPage taskId={id} />;
}
