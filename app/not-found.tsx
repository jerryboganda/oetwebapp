import Link from 'next/link';

export default function NotFound() {
  return (
    <div className="min-h-screen flex items-center justify-center p-6">
      <div className="text-center max-w-md space-y-4">
        <div className="text-6xl font-bold text-primary/30">404</div>
        <h2 className="text-xl font-semibold text-navy">Page not found</h2>
        <p className="text-muted text-sm">
          The page you&apos;re looking for doesn&apos;t exist or has been moved.
        </p>
        <Link
          href="/"
          className="inline-flex items-center justify-center px-6 py-2.5 rounded-lg bg-primary text-white font-medium hover:bg-primary/90 transition-colors"
        >
          Go to Dashboard
        </Link>
      </div>
    </div>
  );
}
