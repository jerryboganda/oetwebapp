import { redirect } from 'next/navigation';

export default function ReadingMockSessionRedirectPage() {
  redirect('/mocks?subtest=reading');
}