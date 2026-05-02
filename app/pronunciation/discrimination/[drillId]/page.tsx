import { redirect } from 'next/navigation';

export default function PronunciationDiscriminationRedirectPage() {
  redirect('/recalls/words');
}