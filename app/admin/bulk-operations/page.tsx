'use client';

import { useEffect, useState } from 'react';
import { Users, CreditCard, Bell, UserX } from 'lucide-react';

import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Input } from '@/components/admin/ui/input';
import { Textarea } from '@/components/admin/ui/textarea';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/admin/ui/select';
import { Toast, InlineAlert } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;
type ActiveTab = 'credits' | 'notifications' | 'status';

function adminRequest<T = unknown>(path: string, body: object): Promise<T> {
  return apiClient.post<T>(path, body);
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
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AdminOperationsLayout
        title="Bulk learner operations"
        description="Apply credit adjustments, notifications, or status changes to multiple learners at once."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Bulk operations' },
        ]}
        eyebrow="Operations"
        primaryGrid={
          <div className="space-y-6">
            <div
              role="tablist"
              aria-label="Bulk operation tabs"
              className="flex flex-wrap gap-2"
            >
              {tabs.map((t) => (
                <Button
                  key={t.key}
                  role="tab"
                  aria-selected={tab === t.key}
                  variant={tab === t.key ? 'primary' : 'outline'}
                  size="sm"
                  onClick={() => setTab(t.key)}
                >
                  {t.icon}
                  <span className="ml-1">{t.label}</span>
                </Button>
              ))}
            </div>

            {tab === 'credits' && (
              <Card>
                <CardHeader>
                  <div className="min-w-0">
                    <CardTitle>Credit adjustments</CardTitle>
                    <CardDescription>
                      Add or deduct credits across many learner accounts in one operation.
                    </CardDescription>
                  </div>
                </CardHeader>
                <CardContent className="space-y-4">
                  <InlineAlert variant="info" title="Bulk credit adjustment">
                    Add or deduct credits from multiple learner accounts at once. Use negative values to debit.
                  </InlineAlert>
                  <div className="space-y-2">
                    <Textarea
                      label="User IDs (one per line or comma-separated)"
                      value={creditUserIds}
                      onChange={(e) => setCreditUserIds(e.target.value)}
                      rows={4}
                      placeholder={'user-id-1\nuser-id-2'}
                    />
                    <p className="text-xs text-admin-fg-muted">{parseUserIds(creditUserIds).length} users</p>
                  </div>
                  <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                    <Input
                      label="Credit amount"
                      type="number"
                      value={creditAmount}
                      onChange={(e) => setCreditAmount(e.target.value)}
                      placeholder="50"
                    />
                    <Input
                      label="Reason"
                      value={creditReason}
                      onChange={(e) => setCreditReason(e.target.value)}
                      placeholder="Promotional credits"
                    />
                  </div>
                  <Button
                    onClick={handleCreditSubmit}
                    disabled={submitting || parseUserIds(creditUserIds).length === 0}
                    loading={submitting}
                  >
                    {submitting ? 'Processing…' : 'Apply credits'}
                  </Button>
                </CardContent>
              </Card>
            )}

            {tab === 'notifications' && (
              <Card>
                <CardHeader>
                  <div className="min-w-0">
                    <CardTitle>Notifications</CardTitle>
                    <CardDescription>
                      Broadcast an in-app notification to many learners at once.
                    </CardDescription>
                  </div>
                </CardHeader>
                <CardContent className="space-y-4">
                  <InlineAlert variant="info" title="Bulk notification">
                    Send a notification to multiple learners simultaneously.
                  </InlineAlert>
                  <div className="space-y-2">
                    <Textarea
                      label="User IDs (one per line or comma-separated)"
                      value={notifUserIds}
                      onChange={(e) => setNotifUserIds(e.target.value)}
                      rows={4}
                      placeholder={'user-id-1\nuser-id-2'}
                    />
                    <p className="text-xs text-admin-fg-muted">{parseUserIds(notifUserIds).length} users</p>
                  </div>
                  <Input
                    label="Title"
                    value={notifTitle}
                    onChange={(e) => setNotifTitle(e.target.value)}
                    placeholder="Important update"
                  />
                  <Textarea
                    label="Message"
                    value={notifMessage}
                    onChange={(e) => setNotifMessage(e.target.value)}
                    rows={3}
                    placeholder="Your message here..."
                  />
                  <Button
                    onClick={handleNotificationSubmit}
                    disabled={submitting || parseUserIds(notifUserIds).length === 0}
                    loading={submitting}
                  >
                    {submitting ? 'Sending…' : 'Send notifications'}
                  </Button>
                </CardContent>
              </Card>
            )}

            {tab === 'status' && (
              <Card>
                <CardHeader>
                  <div className="min-w-0">
                    <CardTitle>Status changes</CardTitle>
                    <CardDescription>
                      Move several accounts between active and suspended states with one audit trail.
                    </CardDescription>
                  </div>
                </CardHeader>
                <CardContent className="space-y-4">
                  <InlineAlert variant="warning" title="Bulk status change">
                    Change the account status of multiple learners. This action may restrict their access.
                  </InlineAlert>
                  <div className="space-y-2">
                    <Textarea
                      label="User IDs (one per line or comma-separated)"
                      value={statusUserIds}
                      onChange={(e) => setStatusUserIds(e.target.value)}
                      rows={4}
                      placeholder={'user-id-1\nuser-id-2'}
                    />
                    <p className="text-xs text-admin-fg-muted">{parseUserIds(statusUserIds).length} users</p>
                  </div>
                  <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                    <div className="flex flex-col gap-1.5">
                      <label className="text-sm font-medium text-admin-fg-strong">New status</label>
                      <Select value={newStatus} onValueChange={setNewStatus}>
                        <SelectTrigger>
                          <SelectValue placeholder="Select status" />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="active">Active</SelectItem>
                          <SelectItem value="suspended">Suspended</SelectItem>
                        </SelectContent>
                      </Select>
                    </div>
                    <Input
                      label="Reason"
                      value={statusReason}
                      onChange={(e) => setStatusReason(e.target.value)}
                      placeholder="Compliance review"
                    />
                  </div>
                  <Button
                    variant="destructive"
                    onClick={handleStatusSubmit}
                    disabled={submitting || parseUserIds(statusUserIds).length === 0}
                    loading={submitting}
                  >
                    {submitting ? 'Processing…' : 'Change status'}
                  </Button>
                </CardContent>
              </Card>
            )}
          </div>
        }
      />
    </AdminRouteWorkspace>
  );
}
