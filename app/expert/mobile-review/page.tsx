import Link from 'next/link';
import { FileSearch, ShieldCheck } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';

export default function ExpertMobileReviewPage() {
  return (
    <main className="min-h-screen bg-background px-4 py-8 sm:px-6 lg:px-8">
      <Card className="mx-auto max-w-3xl p-6 sm:p-8">
        <div className="mb-4 flex h-14 w-14 items-center justify-center rounded-2xl bg-primary/10 text-primary">
          <ShieldCheck className="h-7 w-7" />
        </div>
        <p className="text-xs font-bold uppercase tracking-[0.25em] text-primary">Expert mobile review</p>
        <h1 className="mt-2 text-3xl font-black text-navy">Mobile review is not launch-ready yet</h1>
        <p className="mt-3 text-sm leading-6 text-muted">
          This route is intentionally closed until each mobile review has candidate evidence, audio or transcript context, rubric references, draft persistence, and rework-flow binding. Use the full expert review console for launch-critical reviews.
        </p>
        <div className="mt-6 flex flex-wrap gap-3">
          <Link href="/expert/queue">
            <Button><FileSearch className="h-4 w-4" /> Open review queue</Button>
          </Link>
          <Link href="/support">
            <Button variant="outline">Contact support</Button>
          </Link>
        </div>
      </Card>
    </main>
  );
}
