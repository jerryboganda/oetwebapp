import { AdminStaticPage } from "@/Component/OET/Admin/AdminPages";

export default async function AdminStaticRoute({
  params,
}: {
  params: Promise<{ slug?: string[] }>;
}) {
  const { slug } = await params;
  return <AdminStaticPage slug={slug ?? []} />;
}
