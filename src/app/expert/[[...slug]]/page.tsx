import { ExpertStaticPage } from "@/Component/OET/Expert/ExpertPages";

export default async function ExpertStaticRoute({
  params,
}: {
  params: Promise<{ slug?: string[] }>;
}) {
  const { slug } = await params;
  return <ExpertStaticPage slug={slug ?? []} />;
}
