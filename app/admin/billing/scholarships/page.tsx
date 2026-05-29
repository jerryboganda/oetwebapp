'use client';

import { useCallback, useEffect, useState } from 'react';
import type { ColumnDef } from '@tanstack/react-table';
import { Gift, Plus, XCircle } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { DataTable } from '@/components/admin/ui/data-table';
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/admin/ui/dialog';
import { Input } from '@/components/admin/ui/input';
import { Label } from '@/components/admin/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/admin/ui/select';
import { Textarea } from '@/components/admin/ui/textarea';
import { toast } from '@/components/admin/ui/toaster';
import { InlineAlert } from '@/components/ui/alert';
import {
  listScholarships,
  grantScholarship,
  revokeScholarship,
  type ScholarshipDto,
} from '@/lib/api';

const REASON_OPTIONS = ['need_based', 'partner_institute', 'testimonial', 'goodwill', 'other'];
const TIER_OPTIONS = ['basic', 'premium', 'intensive'];

export default function AdminScholarshipsPage() {
  const [rows, setRows] = useState<ScholarshipDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [open, setOpen] = useState(false);
  const [userId, setUserId] = useState('');
  const [reason, setReason] = useState('need_based');
  const [tier, setTier] = useState('premium');
  const [expiresAt, setExpiresAt] = useState('');
  const [notes, setNotes] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await listScholarships());
      setError(null);
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function handleGrant() {
    if (!userId.trim()) {
      setError('User id is required.');
      return;
    }
    try {
      await grantScholarship({
        userId,
        reason,
        accessTier: tier,
        expiresAt: expiresAt ? new Date(expiresAt).toISOString() : null,
        adminNotes: notes || null,
      });
      toast.success('Scholarship granted.');
      setOpen(false);
      setUserId('');
      setReason('need_based');
      setTier('premium');
      setExpiresAt('');
      setNotes('');
      await load();
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Grant failed.');
    }
  }

  async function handleRevoke(id: string) {
    if (!confirm('Revoke this scholarship?')) return;
    try {
      await revokeScholarship(id);
      toast.success('Revoked.');
      await load();
    } catch (err: any) {
      toast.error(err?.userMessage ?? err?.message ?? 'Revoke failed.');
    }
  }

  const columns: ColumnDef<ScholarshipDto>[] = [
    {
      id: 'granted',
      header: 'Granted',
      cell: ({ row }) => new Date(row.original.grantedAt).toLocaleDateString(),
    },
    { id: 'user', accessorKey: 'userId', header: 'User' },
    { id: 'reason', accessorKey: 'reason', header: 'Reason' },
    { id: 'tier', accessorKey: 'accessTier', header: 'Tier' },
    {
      id: 'expires',
      header: 'Expires',
      cell: ({ row }) => (row.original.expiresAt ? new Date(row.original.expiresAt).toLocaleDateString() : '-'),
    },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) => (
        <Badge variant={row.original.status === 'active' ? 'success' : 'default'}>
          {row.original.status}
        </Badge>
      ),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      enableHiding: false,
      cell: ({ row }) =>
        row.original.status === 'active' ? (
          <Button variant="ghost" size="sm" onClick={() => handleRevoke(row.original.id)} aria-label="Revoke">
            <XCircle className="h-4 w-4 text-[var(--admin-danger)]" />
          </Button>
        ) : null,
    },
  ];

  return (
    <AdminTableLayout
      title="Scholarships"
      description="Admin-granted financial-aid access. Separate from coupons; bypasses payment."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Billing', href: '/admin/billing' },
        { label: 'Scholarships' },
      ]}
      actions={
        <Button onClick={() => setOpen(true)} startIcon={<Plus className="h-4 w-4" />}>
          Grant scholarship
        </Button>
      }
      banner={error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
    >
      <DataTable
        columns={columns}
        data={rows}
        loading={loading}
        emptyMessage="No scholarships granted."
        searchPlaceholder="Search scholarships…"
      />

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Grant scholarship</DialogTitle>
          </DialogHeader>
          <div className="space-y-3">
            <Input
              label="User id"
              value={userId}
              onChange={(e) => setUserId(e.target.value)}
              placeholder="user_..."
            />
            <div className="grid grid-cols-2 gap-3">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="sch-reason">Reason</Label>
                <Select value={reason} onValueChange={setReason}>
                  <SelectTrigger id="sch-reason">
                    <SelectValue placeholder="Reason" />
                  </SelectTrigger>
                  <SelectContent>
                    {REASON_OPTIONS.map((r) => (
                      <SelectItem key={r} value={r}>
                        {r}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="sch-tier">Access tier</Label>
                <Select value={tier} onValueChange={setTier}>
                  <SelectTrigger id="sch-tier">
                    <SelectValue placeholder="Tier" />
                  </SelectTrigger>
                  <SelectContent>
                    {TIER_OPTIONS.map((t) => (
                      <SelectItem key={t} value={t}>
                        {t}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
            <Input
              label="Expires (optional)"
              type="date"
              value={expiresAt}
              onChange={(e) => setExpiresAt(e.target.value)}
            />
            <Textarea
              label="Admin notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="Reason for grant, partner institute, etc."
              rows={3}
            />
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button onClick={handleGrant} startIcon={<Gift className="h-4 w-4" />}>
              Grant
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </AdminTableLayout>
  );
}
