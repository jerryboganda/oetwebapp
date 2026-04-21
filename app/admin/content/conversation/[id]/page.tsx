'use client';

import { useParams } from 'next/navigation';
import { ConversationTemplateEditor } from '@/components/domain/admin/ConversationTemplateEditor';

export default function EditConversationTemplatePage() {
  const params = useParams<{ id: string }>();
  const id = params?.id as string | undefined;
  if (!id) return null;
  return <ConversationTemplateEditor templateId={id} />;
}
