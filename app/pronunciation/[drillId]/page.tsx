import { redirect } from 'next/navigation';

export default function PronunciationDrillRedirectPage() {
  redirect('/recalls/words');
}