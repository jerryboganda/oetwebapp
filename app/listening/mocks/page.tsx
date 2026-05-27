import { redirect } from 'next/navigation';

// Per the 2026-05-27 OET sample-test alignment, full mocks must live only
// inside `/mocks` per the owner's directive §6 ("Mocks must appear in one
// place only"). The candidate-facing listing of full listening mocks moved
// to /mocks?subtest=listening; this page is preserved as a server redirect
// so any bookmark or in-app link bounces to the canonical entry.
// The mock-session player at app/listening/mocks/[sessionId]/page.tsx is
// untouched — it remains the runtime surface for an in-flight mock.
export default function ListeningMocksRedirectPage() {
  redirect('/mocks?subtest=listening');
}
