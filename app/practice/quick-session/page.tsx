import { redirect } from 'next/navigation';

// Quick Session used to serve a hardcoded 8-question bank for every mode
// ("Listening Snap Quiz", "Grammar Quick-Fix") because no
// /v1/learner/quick-session endpoint exists. Owner rule: no fabricated
// content — the route now forwards to the real, API-backed vocabulary quiz.
export default function QuickSessionRedirect() {
  redirect('/vocabulary/quiz');
}
