import { ExpertLearnerPage } from "@/Component/OET/Expert/ExpertPages";

export default async function ExpertLearnerRoute({
  params,
}: {
  params: Promise<{ learnerId: string }>;
}) {
  const { learnerId } = await params;
  return <ExpertLearnerPage learnerId={learnerId} />;
}
