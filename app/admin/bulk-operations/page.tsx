'use client';

import { useEffect, useState } from 'react';
import { Users, CreditCard, Bell, UserX } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input, Textarea, Select } from '@/components/ui/form-controls';
import { Toast, InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { analytics } from '@/lib/analytics';

type ToastState = { variant: 'success' | 'error'; message: string } | null;
type ActiveTab = 'credits' | 'notifications' | 'status';

async function adminRequest<T = unknown>(path: string, body: object): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

function parseUserIds(text: string): string[] {
  return text.split(/[\n,;]+/).map((s) => s.trim()).filter(Boolean);
}

export default function BulkOperationsPage() {
  useAdminAuth();
  const [tab, setTab] = useState<ActiveTab>('credits');
  const [toast, setToast] = useState<ToastState>(null);
  const [submitting, setSubmitting] = useState(false);

  // Credits form
  const [creditUserIds, setCreditUserIds] = useState('');
  const [creditAmount, setCreditAmount] = useState('');
  const [creditReason, setCreditReason] = useState('');

  // Notifications form
  const [notifUserIds, setNotifUserIds] = useState('');
  const [notifTitle, setNotifTitle] = useState('');
  const [notifMessage, setNotifMessage] = useState('');

  // Status form
  const [statusUserIds, setStatusUserIds] = useState('');
  const [newStatus, setNewStatus] = useState('active');
  const [statusReason, setStatusReason] = useState('');

  useEffect(() => { analytics.track('admin_view', { page: 'bulk-operations' }); }, []);

  async function handleCreditSubmit() {
    const ids = parseUserIds(creditUserIds);
    if (ids.length === 0 || !creditAmount || !creditReason) return;
    setSubmitting(true);
    try {
      const result = await adminRequest<{ processed: number; skipped: number }>('/v1/admin/bulk/credits', {
        userIds: ids, creditAmount: Number(creditAmount), reason: creditReason,
      });
      setToast({ variant: 'success', message: `Adjusted credits for ${result.processed} users (${result.skipped} skipped).` });
      setCreditUserIds(''); setCreditAmount(''); setCreditReason('');
    } catch {
      setToast({ variant: 'error', message: 'Bulk credit operation failed.' });
    } finally {
      setSubmitting(false);
    }
  }

  async function handleNotificationSubmit() {
    const ids = parseUserIds(notifUserIds);
    if (ids.length === 0 || !notifTitle || !notifMessage) return;
    setSubmitting(true);
    try {
      const result = await adminRequest<{ sent: number }>('/v1/admin/bulk/notifications', {
        userIds: ids, title: notifTitle, message: notifMessage,
      });
      setToast({ variant: 'success', message: `Sent notifications to ${result.sent} users.` });
      setNotifUserIds(''); setNotifTitle(''); setNotifMessage('');
    } catch {
      setToast({ variant: 'error', message: 'Bulk notification failed.' });
    } finally {
      setSubmitting(false);
    }
  }

  async function handleStatusSubmit() {
    const ids = parseUserIds(statusUserIds);
    if (ids.length === 0 || !statusReason) return;
    setSubmitting(true);
    try {
      const result = await adminRequest<{ processed: number; skipped: number }>('/v1/admin/bulk/status', {
        userIds: ids, newStatus, reason: statusReason,
      });
      setToast({ variant: 'success', message: `Changed status for ${result.processed} users to '${newStatus}'.` });
      setStatusUserIds(''); setStatusReason('');
    } catch {
      setToast({ variant: 'error', message: 'Bulk status change failed.' });
    } finally {
      setSubmitting(false);
    }
  }

  const tabs: { key: ActiveTab; label: string; icon: React.ReactNode }[] = [
    { key: 'credits', label: 'Credit Adjustments', icon: <CreditCard className="w-4 h-4" /> },
    { key: 'notifications', label: 'Notifications', icon: <Bell className="w-4 h-4" /> },
    { key: 'status', label: 'Status Changes', icon: <UserX className="w-4 h-4" /> },
  ];

  return (
    <AdminRouteWorkspace>
      {toast && <Toast variant={toast.variant} onDismiss={() => setToast(null)}>{toast.message}</Toast>}

      <AdminRouteSectionHeader title="Bulk Learner Operations" icon={<Users className="w-5 h-5" />} />

      <div className="flex gap-2 mb-6">
        {tabs.map((t) => (
          <Button key={t.key} variant={tab === t.key ? 'default' : 'outline'} size="sm" onClick={() => setTab(t.key)}>
            {t.icon} <span className="ml-1">{t.label}</span>
          </Button>
        ))}
      </div>

      <AdminRoutePanel>
        {tab === 'credits' && (
          <Card>
            <CardContent className="pt-5 space-y-4">
              <InlineAlert variant="info" title="Bulk Credit Adjustment">
                Add or deduct credits from multiple learner accounts at once. Use negative values to debit.
              </InlineAlert>
              <div>
                <label className="text-sm font-medium">User IDs (one per line or comma-separated)</label>
                <Textarea value={creditUserIds} onChange={(e) => setCreditUserIds(e.target.value)} rows={4} placeholder="user-id-1&#10;user-id-2" />
                <p className="text-xs text-gray-500 mt-1">{parseUserIds(creditUserIds).length} users</p>
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="text-sm font-medium">Credit Amount</label>
                  <Input type="number" value={creditAmount} onChange={(e) => setCreditAmount(e.target.value)} placeholder="50" />
                </div>
                <div>
                  <label className="text-sm font-medium">Reason</label>
                  <Input value={creditReason} onChange={(e) => setCreditReason(e.target.value)} placeholder="Promotional credits" />
                </div>
              </div>
              <Button onClick={handleCreditSubmit} disabled={submitting || parseUserIds(creditUserIds).length === 0}>
                {submitting ? 'Processing…' : 'Apply Credits'}
              </Button>
            </CardContent>
          </Card>
        )}

        {tab === 'notifications' && (
          <Card>
            <CardContent className="pt-5 space-y-4">
              <InlineAlert variant="info" title="Bulk Notification">
                Send a notification to multiple learners simultaneously.
              </InlineAlert>
              <div>
                <label className="text-sm font-medium">User IDs (one per line or comma-separated)</label>
                <Textarea value={notifUserIds} onChange={(e) => setNotifUserIds(e.target.value)} rows={4} placeholder="user-id-1&#10;user-id-2" />
                <p className="text-xs text-gray-500 mt-1">{parseUserIds(notifUserIds).length} users</p>
              </div>
              <div>
                <label className="text-sm font-medium">Title</label>
                <Input value={notifTitle} onChange={(e) => setNotifTitle(e.target.value)} placeholder="Important Update" />
              </div>
              <div>
                <label className="text-sm font-medium">Message</label>
                <Textarea value={notifMessage} onChange={(e) => setNotifMessage(e.target.value)} rows={3} placeholder="Your message here..." />
              </div>
              <Button onClick={handleNotificationSubmit} disabled={submitting || parseUserIds(notifUserIds).length === 0}>
                {submitting ? 'Sending…' : 'Send Notifications'}
              </Button>
            </CardContent>
          </Card>
        )}

        {tab === 'status' && (
          <Card>
            <CardContent className="pt-5 space-y-4">
              <InlineAlert variant="warning" title="Bulk Status Change">
                Change the account status of multiple learners. This action may restrict their access.
              </InlineAlert>
              <div>
                <label className="text-sm font-medium">User IDs (one per line or comma-separated)</label>
                <Textarea value={statusUserIds} onChange={(e) => setStatusUserIds(e.target.value)} rows={4} placeholder="user-id-1&#10;user-id-2" />
                <p className="text-xs text-gray-500 mt-1">{parseUserIds(statusUserIds).length} users</p>
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="text-sm font-medium">New Status</label>
                  <Select value={newStatus} onChange={(e) => setNewStatus(e.target.value)}>
                    <option value="active">Active</option>
                    <option value="suspended">Suspended</option>
                  </Select>
                </div>
                <div>
                  <label className="text-sm font-medium">Reason</label>
                  <Input value={statusReason} onChange={(e) => setStatusReason(e.target.value)} placeholder="Compliance review" />
                </div>
              </div>
              <Button variant="destructive" onClick={handleStatusSubmit} disabled={submitting || parseUserIds(statusUserIds).length === 0}>
                {submitting ? 'Processing…' : 'Change Status'}
              </Button>
            </CardContent>
          </Card>
        )}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
