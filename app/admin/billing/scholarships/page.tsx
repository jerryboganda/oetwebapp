'use client';

import { useCallback, useEffect, useState } from 'react';
import { Gift, XCircle, Plus } from 'lucide-react';
import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Badge } from '@/components/ui/badge';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import {
  listScholarships,
  grantScholarship,
  revokeScholarship,
  type ScholarshipDto,
} from '@/lib/api';

const REASON_OPTIONS = ['need_based', 'partner_institute', 'testimonial', 'goodwill', 'other'].map((r) => ({ value: r, label: r }));
const TIER_OPTIONS = ['basic', 'premium', 'intensive'].map((t) => ({ value: t, label: t }));

export default function AdminScholarshipsPage() {
  const [rows, setRows] = useState<ScholarshipDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

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
      setToast({ variant: 'success', message: 'Scholarship granted.' });
      setOpen(false);
      setUserId(''); setReason('need_based'); setTier('premium'); setExpiresAt(''); setNotes('');
      await load();
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Grant failed.');
    }
  }

  async function handleRevoke(id: string) {
    if (!confirm('Revoke this scholarship?')) return;
    try {
      await revokeScholarship(id);
      setToast({ variant: 'success', message: 'Revoked.' });
      await load();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? err?.message ?? 'Revoke failed.' });
    }
  }

  const columns: Column<ScholarshipDto>[] = [
    { key: 'granted', header: 'Granted', render: (r) => new Date(r.grantedAt).toLocaleDateString() },
    { key: 'user', header: 'User', render: (r) => r.userId },
    { key: 'reason', header: 'Reason', render: (r) => r.reason },
    { key: 'tier', header: 'Tier', render: (r) => r.accessTier },
    { key: 'expires', header: 'Expires', render: (r) => r.expiresAt ? new Date(r.expiresAt).toLocaleDateString() : '—' },
    { key: 'status', header: 'Status', render: (r) => <Badge variant={r.status === 'active' ? 'success' : 'default'}>{r.status}</Badge> },
    {
      key: 'actions',
      header: '',
      render: (r) => r.status === 'active' ? (
        <Button variant="ghost" size="sm" onClick={() => handleRevoke(r.id)} aria-label="Revoke">
          <XCircle className="h-4 w-4 text-rose-600" />
        </Button>
      ) : null,
    },
  ];

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader title="Scholarships" description="Admin-granted financial-aid access. Separate from coupons; bypasses payment." />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AdminRoutePanel>
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <div className="mb-4 flex justify-end">
          <Button onClick={() => setOpen(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Grant scholarship
          </Button>
        </div>
        {loading ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : (
          <DataTable
            data={rows}
            columns={columns}
            keyExtractor={(r) => r.id}
            emptyMessage="No scholarships granted."
          />
        )}
      </AdminRoutePanel>

      {open && (
        <Modal open onClose={() => setOpen(false)} title="Grant scholarship">
          <div className="space-y-3 p-4">
            <Input label="User id" value={userId} onChange={(e) => setUserId(e.target.value)} placeholder="user_..." />
            <div className="grid grid-cols-2 gap-3">
              <Select label="Reason" value={reason} options={REASON_OPTIONS} onChange={(e) => setReason(e.target.value)} />
              <Select label="Access tier" value={tier} options={TIER_OPTIONS} onChange={(e) => setTier(e.target.value)} />
            </div>
            <Input label="Expires (optional)" type="date" value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} />
            <Textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="Reason for grant, partner institute, etc."
            />
            <div className="flex justify-end gap-2">
              <Button variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
              <Button onClick={handleGrant}>
                <Gift className="mr-2 h-4 w-4" />
                Grant
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </AdminRouteWorkspace>
  );
}
