'use client';

import { useParams } from 'next/navigation';
import { AdminContentEditor } from '@/components/domain/admin-content-editor';

export default function AdminContentEditPage() {
  const params = useParams<{ id: string }>();
  return <AdminContentEditor contentId={params.id} />;
}
