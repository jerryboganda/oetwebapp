import { redirect } from 'next/navigation';

export default function LegacyReadingResultsRedirect() {
  redirect('/reading');
}
