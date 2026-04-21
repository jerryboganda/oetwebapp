import { redirect } from 'next/navigation';

export default function LegacyReadingPlayerRedirect() {
  redirect('/reading');
}
