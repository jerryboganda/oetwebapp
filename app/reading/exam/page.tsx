import { redirect } from 'next/navigation';

// Per the 2026-05-27 OET sample-test alignment, the "Full Reading Exam" entry
// on the Reading hub funnels into the canonical Mocks tab pre-filtered to
// reading. Owner directive §6 — full mocks must live only in /mocks and must
// not be duplicated inside Listening/Reading/Writing — so this page does not
// host its own listing; it bounces straight to the single source of truth.
export default function ReadingExamRedirectPage() {
  redirect('/mocks?subtest=reading');
}
