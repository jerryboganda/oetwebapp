'use client';

import { useEffect, useState } from 'react';
import { motion, useReducedMotion } from 'motion/react';
import {
  Building2,
  Mail,
  ChevronRight,
  Plus,
  Search,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchAdminSponsors, type AdminSponsorDto } from '@/lib/api';

type Sponsor = AdminSponsorDto;

export default function AdminInstitutionsPage() {
  const reducedMotion = useReducedMotion();
  const [sponsors, setSponsors] = useState<Sponsor[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState('');

  useEffect(() => {
    fetchAdminSponsors()
      .then((data) => setSponsors(data.items ?? []))
      .catch(() => setError('Failed to load institutions'))
      .finally(() => setLoading(false));
  }, []);

  const filtered = sponsors.filter(
    (s) =>
      s.name.toLowerCase().includes(filter.toLowerCase()) ||
      s.organizationName?.toLowerCase().includes(filter.toLowerCase()) ||
      s.contactEmail.toLowerCase().includes(filter.toLowerCase())
  );

  return (
    <div className="p-6">
      <motion.div
        initial={reducedMotion ? false : { opacity: 0, y: 8 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.3 }}
      >
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h1 className="text-2xl font-black text-navy">Institutions</h1>
            <p className="mt-1 text-sm text-muted">
              Manage sponsor accounts, employers, and institutional partners.
            </p>
          </div>
          <Button variant="primary">
            <Plus className="mr-1.5 h-4 w-4" />
            Add institution
          </Button>
        </div>

        <div className="mt-6 flex items-center gap-3">
          <div className="relative flex-1">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
            <Input
              placeholder="Search by name, email, or organization..."
              value={filter}
              onChange={(e) => setFilter(e.target.value)}
              className="pl-9"
            />
          </div>
        </div>

        <div className="mt-6 rounded-2xl border border-border bg-surface shadow-sm">
          {loading ? (
            <div className="divide-y divide-border">
              {[0, 1, 2].map((i) => (
                <div key={i} className="flex items-center gap-4 p-4">
                  <Skeleton className="h-10 w-10 rounded-lg" />
                  <div className="flex-1 space-y-2">
                    <Skeleton className="h-4 w-1/3" />
                    <Skeleton className="h-3 w-1/4" />
                  </div>
                  <Skeleton className="h-8 w-20" />
                </div>
              ))}
            </div>
          ) : error ? (
            <div className="p-8 text-center text-muted">{error}</div>
          ) : filtered.length === 0 ? (
            <div className="p-8 text-center text-muted">
              No institutions found.
            </div>
          ) : (
            <div className="divide-y divide-border">
              {filtered.map((sponsor, index) => (
                <motion.div
                  key={sponsor.id}
                  initial={reducedMotion ? false : { opacity: 0, y: 4 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.2, delay: index * 0.03 }}
                  className="group flex items-center gap-4 p-4 hover:bg-background-light"
                >
                  <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-primary/10 text-primary">
                    <Building2 className="h-5 w-5" />
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-semibold text-navy">
                      {sponsor.name}
                    </p>
                    <div className="mt-0.5 flex flex-wrap items-center gap-2 text-xs text-muted">
                      <span className="inline-flex items-center gap-1">
                        <Mail className="h-3 w-3" />
                        {sponsor.contactEmail}
                      </span>
                      {sponsor.organizationName && (
                        <span className="rounded-full bg-border px-2 py-0.5">
                          {sponsor.organizationName}
                        </span>
                      )}
                      <span
                        className={`rounded-full px-2 py-0.5 text-[10px] font-black uppercase tracking-wider ${
                          sponsor.status === 'active'
                            ? 'bg-emerald-100 text-emerald-700'
                            : 'bg-amber-100 text-amber-700'
                        }`}
                      >
                        {sponsor.status}
                      </span>
                    </div>
                  </div>
                  <div className="flex items-center gap-4">
                    <div className="hidden text-right sm:block">
                      <p className="text-xs text-muted">Type</p>
                      <p className="text-sm font-medium capitalize text-navy">
                        {sponsor.type}
                      </p>
                    </div>
                    <div className="hidden text-right sm:block">
                      <p className="text-xs text-muted">Learners</p>
                      <p className="text-sm font-medium text-navy">
                        {sponsor.learnerCount ?? 0}
                      </p>
                    </div>
                    <ChevronRight className="h-4 w-4 text-muted group-hover:text-navy" />
                  </div>
                </motion.div>
              ))}
            </div>
          )}
        </div>
      </motion.div>
    </div>
  );
}
