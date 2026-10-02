import { redirect } from 'next/navigation';

// The grammar hub lists every topic; /grammar/topics was otherwise caught by
// /grammar/[lessonId] and showed "Lesson not found" from the Topics breadcrumb.
export default function GrammarTopicsIndexPage() {
  redirect('/grammar');
}
