import { redirect } from 'next/navigation';

export default async function LegacyReadingResultsRedirect({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  redirect(`/reading?legacyReadingResultId=${encodeURIComponent(id)}`);
}
