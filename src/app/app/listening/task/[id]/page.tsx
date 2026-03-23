import { ListeningTaskPage } from "@/Component/OET/Learner/LearnerDynamicPages";

export default async function ListeningTaskRoute({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;
  return <ListeningTaskPage taskId={id} />;
}
