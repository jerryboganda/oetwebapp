'use client';

import { LockKeyhole, Mail } from 'lucide-react';
import Link from 'next/link';
import { useSponsorAuth } from '@/lib/hooks/use-sponsor-auth';

function SponsorLayoutContent() {
  const { isLoading } = useSponsorAuth();

  if (isLoading) {
    return null;
  }

  return (
    <main className="min-h-screen bg-background px-4 py-10 sm:px-6 lg:px-8">
      <div className="mx-auto max-w-3xl rounded-[2rem] border border-border bg-surface p-6 shadow-sm sm:p-8">
        <div className="mb-4 flex h-14 w-14 items-center justify-center rounded-2xl bg-primary/10 text-primary">
          <LockKeyhole className="h-7 w-7" />
        </div>
        <p className="text-xs font-bold uppercase tracking-[0.25em] text-primary">Sponsor portal</p>
        <h1 className="mt-2 text-3xl font-black text-navy">Sponsor access is held for a later launch gate</h1>
        <p className="mt-3 text-sm leading-6 text-muted">
          The first public launch is limited to learner, expert, and admin operations with evidence-complete billing and support paths. Sponsor finance, learner management, and reporting remain hidden until their legal, privacy, and data-quality gates are complete.
        </p>
        <div className="mt-6 flex flex-wrap gap-3">
          <Link className="inline-flex items-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-medium text-white hover:bg-primary/90" href="/support">
            <Mail className="h-4 w-4" /> Contact support
          </Link>
          <Link className="rounded-lg border border-border px-4 py-2 text-sm font-medium text-navy hover:bg-background-light" href="/dashboard">
            Return to dashboard
          </Link>
        </div>
      </div>
    </main>
  );
}

export default function SponsorLayout(_props: { children: React.ReactNode }) {
  return <SponsorLayoutContent />;
}
