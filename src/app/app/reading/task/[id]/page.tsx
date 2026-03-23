import { ReadingTaskPage } from "@/Component/OET/Learner/LearnerDynamicPages";

export default async function ReadingTaskRoute({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;
  return <ReadingTaskPage taskId={id} />;
}
