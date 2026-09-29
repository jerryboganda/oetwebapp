import { redirect } from 'next/navigation';

export default function ReadingMocksRedirectPage() {
  redirect('/mocks?subtest=reading');
}