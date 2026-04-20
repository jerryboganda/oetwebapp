import { redirect } from 'next/navigation';

export default async function LegacyWritingAttemptPage({ params }: { params: Promise<{ attemptId: string }> }) {
  const { attemptId } = await params;
  redirect(`/writing/player?attemptId=${encodeURIComponent(attemptId)}`);
}
