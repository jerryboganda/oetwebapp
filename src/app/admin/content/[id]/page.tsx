import { AdminContentDetailPage } from "@/Component/OET/Admin/AdminPages";

export default async function AdminContentDetailRoute({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;
  return <AdminContentDetailPage contentId={id} />;
}
