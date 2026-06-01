import { redirect } from 'next/navigation';

export default function AdminRecallsIndex() {
  redirect('/admin/content/vocabulary/import');
}
