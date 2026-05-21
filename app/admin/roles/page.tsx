// Legacy Roles surface. Consolidated into the unified User Operations hub at
// /admin/users → "Admins & Permissions" tab. Server-side redirect avoids any
// flash of intermediate UI.

import { redirect } from 'next/navigation';

export default function RolesRedirectPage() {
  redirect('/admin/users?tab=admins');
}
