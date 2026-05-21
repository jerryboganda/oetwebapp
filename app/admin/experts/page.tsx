// Legacy Expert Management surface. Consolidated into the unified User
// Operations hub at /admin/users → "Tutors" tab. Server-side redirect avoids
// any flash of intermediate UI.

import { redirect } from 'next/navigation';

export default function ExpertsRedirectPage() {
  redirect('/admin/users?tab=tutors');
}
