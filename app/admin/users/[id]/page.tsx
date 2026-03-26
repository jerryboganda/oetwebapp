'use client';

import { useState } from 'react';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { mockUsers } from '@/lib/mock-admin-data';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Toast } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import Link from 'next/link';
import { ArrowLeft, User as UserIcon, Mail, Calendar, Shield, Activity, FileText } from 'lucide-react';
import { notFound } from 'next/navigation';
import { use } from 'react';

export default function UserDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const resolvedParams = use(params);
  const { isAuthenticated, role } = useAdminAuth();
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  if (!isAuthenticated || role !== 'admin') return null;

  const user = mockUsers.find(u => u.id === resolvedParams?.id);

  if (!user) {
    notFound();
  }

  const handleResetPassword = () => {
    try {
      analytics.track('admin_user_role_changed', { userId: user.id, action: 'reset_password' });
      setToast({ variant: 'success', message: `Password reset email sent to ${user.email}.` });
    } catch {
      setToast({ variant: 'error', message: 'Failed to send password reset.' });
    }
  };

  const handleToggleStatus = () => {
    const action = user.status === 'active' ? 'suspend' : 'reactivate';
    try {
      analytics.track('admin_user_role_changed', { userId: user.id, action });
      setToast({ variant: 'success', message: `Account ${action === 'suspend' ? 'suspended' : 'reactivated'} successfully.` });
    } catch {
      setToast({ variant: 'error', message: `Failed to ${action} account.` });
    }
  };

  return (
    <div className="space-y-6 max-w-5xl mx-auto" role="main" aria-label={`User Detail: ${user.name}`}>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <div>
        <Link href="/admin/users" className="text-sm text-slate-500 hover:text-blue-600 flex items-center gap-2 w-fit mb-4">
          <ArrowLeft className="w-4 h-4" />
          Back to Users
        </Link>
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-2xl font-semibold text-slate-900">{user.name}</h1>
            <p className="text-sm text-slate-500 mt-1 font-mono">{user.id}</p>
          </div>
          <div className="flex gap-3">
            <Button variant="outline" onClick={handleResetPassword}>
              Reset Password
            </Button>
            <Button
              variant={user.status === 'active' ? 'destructive' : 'primary'}
              onClick={handleToggleStatus}
            >
              {user.status === 'active' ? 'Suspend Account' : 'Reactivate Account'}
            </Button>
          </div>
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {/* Profile Card */}
        <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6 md:col-span-1 h-fit">
          <div className="flex flex-col items-center text-center">
            <div className="w-24 h-24 bg-blue-50 text-blue-600 rounded-full flex items-center justify-center mb-4">
              <UserIcon className="w-10 h-10" />
            </div>
            <h2 className="text-lg font-medium text-slate-900">{user.name}</h2>
            <Badge variant={user.role === 'admin' ? 'danger' : user.role === 'expert' ? 'warning' : 'default'} className="mt-2 capitalize">
              {user.role}
            </Badge>
            <Badge variant={user.status === 'active' ? 'success' : 'muted'} className="mt-2">
              {user.status.charAt(0).toUpperCase() + user.status.slice(1)}
            </Badge>
          </div>

          <div className="mt-8 space-y-4">
            <div className="flex items-center gap-3 text-sm">
              <Mail className="w-4 h-4 text-slate-400" />
              <span className="text-slate-600">{user.email}</span>
            </div>
            <div className="flex items-center gap-3 text-sm">
              <Calendar className="w-4 h-4 text-slate-400" />
              <span className="text-slate-600">Joined Jan 15, 2026</span>
            </div>
            <div className="flex items-center gap-3 text-sm">
              <Activity className="w-4 h-4 text-slate-400" />
              <span className="text-slate-600">Last login: {new Date(user.lastLogin).toLocaleDateString()}</span>
            </div>
          </div>
        </div>

        {/* Details Area */}
        <div className="md:col-span-2 space-y-6">
          {/* Stats */}
          <div className="grid grid-cols-2 gap-4">
            <div className="bg-white p-5 rounded-xl border border-slate-200 shadow-sm flex items-center gap-4">
              <div className="w-12 h-12 rounded-lg bg-indigo-50 text-indigo-600 flex items-center justify-center">
                <FileText className="w-6 h-6" />
              </div>
              <div>
                <div className="text-2xl font-semibold text-slate-900">
                  {user.role === 'expert' ? '142' : '12'}
                </div>
                <div className="text-sm text-slate-500">
                  {user.role === 'expert' ? 'Tasks Graded' : 'Tasks Completed'}
                </div>
              </div>
            </div>
            <div className="bg-white p-5 rounded-xl border border-slate-200 shadow-sm flex items-center gap-4">
              <div className="w-12 h-12 rounded-lg bg-teal-50 text-teal-600 flex items-center justify-center">
                <Shield className="w-6 h-6" />
              </div>
              <div>
                <div className="text-2xl font-semibold text-slate-900">
                  {user.role === 'expert' ? '98.5%' : 'N/A'}
                </div>
                <div className="text-sm text-slate-500">
                  {user.role === 'expert' ? 'Agreement Rate' : 'Avg Score'}
                </div>
              </div>
            </div>
          </div>

          {/* Recent Activity */}
          <div className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden">
            <div className="px-6 py-4 border-b border-slate-200 bg-slate-50/50">
              <h3 className="font-medium text-slate-900">Recent Activity</h3>
            </div>
            <div className="divide-y divide-slate-100">
              {[1, 2, 3].map((i) => (
                <div key={i} className="px-6 py-4 flex items-center justify-between hover:bg-slate-50 transition-colors">
                  <div>
                    <div className="text-sm font-medium text-slate-900">
                      {user.role === 'expert' ? 'Graded Writing Task' : 'Completed Speaking Task'}
                    </div>
                    <div className="text-xs text-slate-500 mt-1">CNT-00{i} • Cardiology</div>
                  </div>
                  <div className="text-xs text-slate-500">{i} days ago</div>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
