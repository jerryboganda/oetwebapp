'use client';

import { useState, useEffect, useMemo } from 'react';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { FilterBar, FilterGroup } from '@/components/ui/filter-bar';
import { DataTable, Column } from '@/components/ui/data-table';
import { mockUsers, AdminUser } from '@/lib/mock-admin-data';
import { Badge } from '@/components/ui/badge';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Users, Search } from 'lucide-react';
import { analytics } from '@/lib/analytics';
import Link from 'next/link';

export default function UsersPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [activeFilters, setActiveFilters] = useState<Record<string, string[]>>({ role: [], status: [] });
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [pageStatus, setPageStatus] = useState<'loading' | 'success' | 'empty'>('loading');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => {
    const load = async () => {
      await new Promise(resolve => setTimeout(resolve, 300));
      setPageStatus(mockUsers.length > 0 ? 'success' : 'empty');
    };
    load();
  }, []);

  const columns: Column<AdminUser>[] = [
    {
      key: 'name',
      header: 'Name',
      render: (item) => (
        <div>
          <Link href={`/admin/users/${item.id}`} className="font-medium text-blue-600 hover:underline">
            {item.name}
          </Link>
          <div className="text-sm text-slate-500">{item.email}</div>
        </div>
      ),
    },
    {
      key: 'role',
      header: 'Role',
      render: (item) => (
        <Badge variant={item.role === 'admin' ? 'danger' : item.role === 'expert' ? 'warning' : 'default'} className="capitalize">
          {item.role}
        </Badge>
      ),
    },
    {
      key: 'lastLogin',
      header: 'Last Login',
      render: (item) => <div className="text-slate-600">{new Date(item.lastLogin).toLocaleDateString()}</div>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (item) => (
        <Badge variant={item.status === 'active' ? 'success' : 'muted'}>
          {item.status.charAt(0).toUpperCase() + item.status.slice(1)}
        </Badge>
      ),
    },
  ];

  const filterGroups: FilterGroup[] = [
    {
      id: 'role',
      label: 'Role',
      options: [
        { id: 'learner', label: 'Learner' },
        { id: 'expert', label: 'Expert' },
        { id: 'admin', label: 'Admin' },
      ],
    },
    {
      id: 'status',
      label: 'Status',
      options: [
        { id: 'active', label: 'Active' },
        { id: 'suspended', label: 'Suspended' },
      ],
    },
  ];

  const handleFilterChange = (groupId: string, optionId: string) => {
    setActiveFilters((prev) => {
      const current = prev[groupId] || [];
      const updated = current.includes(optionId)
        ? current.filter((id) => id !== optionId)
        : [...current, optionId];
      return { ...prev, [groupId]: updated };
    });
  };

  const filteredData = useMemo(() => {
    const q = searchQuery.toLowerCase();
    return mockUsers.filter((item) => {
      const matchesSearch = !q || item.name.toLowerCase().includes(q) || item.email.toLowerCase().includes(q) || item.id.toLowerCase().includes(q);
      const matchesRole = activeFilters.role.length === 0 || activeFilters.role.includes(item.role);
      const matchesStatus = activeFilters.status.length === 0 || activeFilters.status.includes(item.status);
      return matchesSearch && matchesRole && matchesStatus;
    });
  }, [searchQuery, activeFilters]);

  const handleInvite = () => {
    try {
      analytics.track('admin_user_role_changed', { action: 'invite' });
      setIsModalOpen(false);
      setToast({ variant: 'success', message: 'Invitation sent successfully.' });
    } catch {
      setToast({ variant: 'error', message: 'Failed to send invitation.' });
    }
  };

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <div className="space-y-6 max-w-7xl mx-auto" role="main" aria-label="User Management">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900">User Management</h1>
          <p className="text-sm text-slate-500 mt-1">Manage learners, experts, and administrative accounts.</p>
        </div>
        <Button onClick={() => setIsModalOpen(true)}>Invite User</Button>
      </div>

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<Users className="w-12 h-12 text-muted" />}
            title="No Users Yet"
            description="Invite your first user to get started."
            action={{ label: 'Invite User', onClick: () => setIsModalOpen(true) }}
          />
        }
      >
        <div className="bg-white rounded-xl shadow-sm border border-slate-200">
          <div className="p-4 border-b border-slate-200 flex flex-col sm:flex-row gap-4">
            <div className="relative flex-1">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400 z-10" />
              <Input
                placeholder="Search by name, email, or ID..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="pl-10"
              />
            </div>
          </div>
          <FilterBar
            groups={filterGroups}
            selected={activeFilters}
            onChange={handleFilterChange}
            className="border-b border-slate-200 p-4"
          />
          {filteredData.length === 0 ? (
            <EmptyState
              icon={<Users className="w-12 h-12 text-muted" />}
              title="No Matching Users"
              description="Adjust your search or filters."
              action={{ label: 'Clear Filters', onClick: () => { setSearchQuery(''); setActiveFilters({ role: [], status: [] }); } }}
            />
          ) : (
            <DataTable columns={columns} data={filteredData} keyExtractor={(item) => item.id} />
          )}
        </div>
      </AsyncStateWrapper>

      <Modal open={isModalOpen} onClose={() => setIsModalOpen(false)} title="Invite New User">
        <div className="space-y-4 py-4">
          <Input label="Full Name" placeholder="e.g., Dr. John Doe" />
          <Input label="Email Address" type="email" placeholder="john.doe@example.com" />
          <Select
            label="Role"
            options={[
              { value: 'learner', label: 'Learner' },
              { value: 'expert', label: 'Expert' },
              { value: 'admin', label: 'Admin' },
            ]}
          />
          <p className="text-sm text-slate-500 mt-2">An invitation email will be sent with instructions to set up their password.</p>
          <div className="flex justify-end gap-3 pt-4 border-t border-slate-100 mt-6">
            <Button variant="outline" onClick={() => setIsModalOpen(false)}>Cancel</Button>
            <Button onClick={handleInvite}>Send Invite</Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
