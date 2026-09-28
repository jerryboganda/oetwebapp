import Link from 'next/link';
import { LifeBuoy, Mail, ShieldCheck, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';

const supportEmail = 'support@oetwithdrhesham.co.uk';

export default function SupportPage() {
  return (
    <main className="min-h-screen bg-background-light px-4 py-10 sm:px-6 lg:px-8">
      <div className="mx-auto max-w-4xl space-y-6">
        <div className="rounded-surface bg-gradient-to-br from-primary/10 via-surface to-lavender/30 p-6 sm:p-8">
          <div className="mb-4 flex h-14 w-14 items-center justify-center rounded-2xl bg-primary text-white dark:bg-violet-700">
            <LifeBuoy className="h-7 w-7" aria-hidden="true" />
          </div>
          <p className="text-xs font-bold uppercase tracking-[0.25em] text-primary">Support</p>
          <h1 className="mt-2 text-3xl font-black text-navy sm:text-4xl">We can help with your OET prep account</h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted">
            Contact support for account access, billing questions, privacy requests, or help deleting your account. Do not email passwords, payment card details, or clinical documents.
          </p>
        </div>

        <div className="grid gap-4 md:grid-cols-3">
          <Card className="p-5">
            <Mail className="mb-3 h-5 w-5 text-primary" aria-hidden="true" />
            <h2 className="font-semibold text-navy">Contact</h2>
            <p className="mt-2 text-sm leading-6 text-muted">
              Email <a className="font-medium text-primary underline-offset-4 hover:underline" href={`mailto:${supportEmail}`}>{supportEmail}</a> with your account email and a short description.
            </p>
          </Card>
          <Card className="p-5">
            <ShieldCheck className="mb-3 h-5 w-5 text-primary" aria-hidden="true" />
            <h2 className="font-semibold text-navy">Privacy</h2>
            <p className="mt-2 text-sm leading-6 text-muted">
              For data access, correction, or export requests, include &quot;Privacy request&quot; in the subject. We will verify ownership before sharing account data.
            </p>
          </Card>
          <Card className="p-5">
            <Trash2 className="mb-3 h-5 w-5 text-primary" aria-hidden="true" />
            <h2 className="font-semibold text-navy">Delete account</h2>
            <p className="mt-2 text-sm leading-6 text-muted">
              Request deletion from the same email address used on the account. We will confirm irreversible deletion steps before acting.
            </p>
          </Card>
        </div>

        <Card className="p-5">
          <h2 className="font-semibold text-navy">Response expectations</h2>
          <p className="mt-2 text-sm leading-6 text-muted">
            Account and billing requests are normally reviewed within 2 business days. Privacy and deletion requests are acknowledged within 7 days after identity verification.
          </p>
          <div className="mt-4 flex flex-wrap gap-3">
            <Button asChild>
              <Link href={`mailto:${supportEmail}`}>Email support</Link>
            </Button>
            <Button asChild variant="outline">
              <Link href="/privacy">Read privacy policy</Link>
            </Button>
            <Button asChild variant="outline">
              <Link href="/account-deletion">Delete my account</Link>
            </Button>
            <Button asChild variant="outline">
              <Link href="/sign-in">Sign in</Link>
            </Button>
          </div>
        </Card>
      </div>
    </main>
  );
}
