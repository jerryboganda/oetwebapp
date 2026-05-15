'use client';

import { useParams } from 'next/navigation';
import { ConversationTemplateEditor } from '@/components/domain/admin/ConversationTemplateEditor';

export default function EditConversationTemplatePage() {
  const params = useParams();
  const rawId = params?.id;
  const id = Array.isArray(rawId) ? rawId[0] ?? '' : rawId ?? '';
  if (!id) return null;
  return <ConversationTemplateEditor templateId={id} />;
}
