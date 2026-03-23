import { AdminContentRevisionsPage } from "@/Component/OET/Admin/AdminPages";

export default async function AdminContentRevisionsRoute({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;
  return <AdminContentRevisionsPage contentId={id} />;
}
