import Link from 'next/link';
import { ArrowLeft, Headphones } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';

export default function PronunciationDiscriminationPage() {
  return (
    <LearnerDashboardShell>
      <Link href="/pronunciation" className="mb-4 inline-flex items-center gap-2 text-sm font-medium text-muted hover:text-navy">
        <ArrowLeft className="h-4 w-4" /> Back to pronunciation
      </Link>
      <Card className="p-6">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <h1 className="flex items-center gap-2.5 text-2xl font-bold text-navy">
              <Headphones className="h-6 w-6 shrink-0 text-primary" aria-hidden />
              Minimal-pair discrimination
            </h1>
            <p className="mt-2 max-w-2xl text-sm leading-6 text-muted">
              This first-class pronunciation route is ready for the learner module. The live listening rounds stay disabled until the published drill includes verified audio pairs and scoring evidence.
            </p>
          </div>
          <Button variant="outline" asChild>
<Link href="/pronunciation">Choose another drill</Link>
</Button>
        </div>
      </Card>
    </LearnerDashboardShell>
  );
}
