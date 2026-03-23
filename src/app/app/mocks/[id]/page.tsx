import { MockReportPage } from "@/Component/OET/Learner/LearnerDynamicPages";

export default async function MockReportRoute({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;
  return <MockReportPage mockId={id} />;
}
