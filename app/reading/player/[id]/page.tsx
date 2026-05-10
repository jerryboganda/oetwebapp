import { redirect } from 'next/navigation';

export default function LegacyReadingPlayerClosedPage() {
  redirect('/reading');
}