import { redirect } from 'next/navigation';

export default async function WritingResultIdRedirect({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  redirect(`/writing/result?id=${encodeURIComponent(id)}`);
}