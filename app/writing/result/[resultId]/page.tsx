import { redirect } from 'next/navigation';

export default async function LegacyWritingResultPage({ params }: { params: Promise<{ resultId: string }> }) {
  const { resultId } = await params;
  redirect(`/writing/result?id=${encodeURIComponent(resultId)}`);
}
