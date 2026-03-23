import { LearnerStaticPage } from "@/Component/OET/Learner/LearnerStaticPages";

export default async function LearnerStaticRoute({
  params,
}: {
  params: Promise<{ slug?: string[] }>;
}) {
  const { slug } = await params;
  return <LearnerStaticPage slug={slug ?? []} />;
}
