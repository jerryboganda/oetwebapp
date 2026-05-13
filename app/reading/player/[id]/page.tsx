import { redirect } from 'next/navigation';

export default async function LegacyReadingPlayerClosedPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  redirect(`/reading?legacyReadingTaskId=${encodeURIComponent(id)}`);
}
