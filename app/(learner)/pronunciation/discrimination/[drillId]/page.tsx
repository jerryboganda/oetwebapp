'use client';

import Link from 'next/link';
import { Headphones } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';

export default function PronunciationDiscriminationPage() {
  return (
    <LearnerPageHero
      title="Minimal-pair discrimination"
      description="This first-class pronunciation route is ready for the learner module. The live listening rounds stay disabled until the published drill includes verified audio pairs and scoring evidence."
      icon={Headphones}
      aside={(
        <Button variant="outline" asChild>
          <Link href="/pronunciation">Choose another drill</Link>
        </Button>
      )}
    />
  );
}
