'use client';

import Link from 'next/link';
import { CatalogStorefront } from '@/components/domain/catalog';
import { CartNavButton } from '@/components/cart';

export default function CatalogPage() {
  return (
    <div className="min-h-screen bg-background-light text-navy">
      <header className="sticky top-0 z-10 border-b border-border bg-surface/80 backdrop-blur">
        <div className="mx-auto flex max-w-[1200px] items-center justify-between gap-3 px-4 py-3 sm:px-6">
          <Link href="/" className="text-sm font-bold tracking-tight text-navy">
            OET with Dr. Ahmed Hesham
          </Link>
          <div className="flex items-center gap-2">
            <CartNavButton />
            <Link
              href="/sign-in"
              className="rounded-lg px-3 py-1.5 text-sm font-semibold text-navy transition-colors hover:bg-background-light"
            >
              Log in
            </Link>
            <Link
              href="/register"
              className="rounded-lg bg-primary px-3.5 py-1.5 text-sm font-semibold text-white transition-colors hover:bg-primary/90"
            >
              Create account
            </Link>
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-[1200px] px-4 py-8 sm:px-6 lg:py-10">
        <CatalogStorefront variant="public" />
      </main>
    </div>
  );
}
