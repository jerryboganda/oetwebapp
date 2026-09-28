import Link from 'next/link';
import { Button } from '@/components/ui/button';

export default function NotFound() {
  return (
    <div className="min-h-screen flex items-center justify-center p-6">
      <div className="text-center max-w-md space-y-4">
        <div className="text-6xl font-bold text-primary/30 dark:text-primary/20" aria-hidden="true">404</div>
        <h1 className="text-xl font-semibold text-navy">Page not found</h1>
        <p className="text-muted text-sm">
          The page you&apos;re looking for doesn&apos;t exist or has been moved.
        </p>
        <Button asChild>
          <Link href="/">Go to Dashboard</Link>
        </Button>
      </div>
    </div>
  );
}
