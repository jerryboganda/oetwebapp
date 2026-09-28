'use client';

import Link from 'next/link';
import { CatalogStorefront } from '@/components/domain/catalog';
import { CartNavButton } from '@/components/cart';
import { Button } from '@/components/ui/button';

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
            <Button asChild variant="ghost" size="sm" className="text-sm font-semibold">
              <Link href="/sign-in">Log in</Link>
            </Button>
            <Button asChild size="sm" className="text-sm font-semibold">
              <Link href="/register">Create account</Link>
            </Button>
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-[1200px] px-4 py-8 sm:px-6 lg:py-10">
        <CatalogStorefront variant="public" />
      </main>
    </div>
  );
}
