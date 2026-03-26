'use client';

import { AppShell } from '@/components/layout/app-shell';
import { NavItem } from '@/components/layout/sidebar';
import { 
  Library, 
  FilePlus2, 
  ListTree, 
  Target, 
  Cpu, 
  Activity, 
  PieChart, 
  Users, 
  CreditCard, 
  Flag, 
  ShieldCheck, 
  Lock 
} from 'lucide-react';
import { usePathname } from 'next/navigation';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

const adminNavItems: NavItem[] = [
  { href: '/admin/content', label: 'Content Library', icon: <Library className="w-5 h-5" />, matchPrefix: '/admin/content' },
  { href: '/admin/taxonomy', label: 'Profession Taxonomy', icon: <ListTree className="w-5 h-5" />, matchPrefix: '/admin/taxonomy' },
  { href: '/admin/criteria', label: 'Rubrics & Criteria', icon: <Target className="w-5 h-5" />, matchPrefix: '/admin/criteria' },
  { href: '/admin/ai-config', label: 'AI Eval Config', icon: <Cpu className="w-5 h-5" />, matchPrefix: '/admin/ai-config' },
  { href: '/admin/review-ops', label: 'Review Ops', icon: <Activity className="w-5 h-5" />, matchPrefix: '/admin/review-ops' },
  { href: '/admin/analytics/quality', label: 'Quality Analytics', icon: <PieChart className="w-5 h-5" />, matchPrefix: '/admin/analytics' },
  { href: '/admin/users', label: 'User Ops', icon: <Users className="w-5 h-5" />, matchPrefix: '/admin/users' },
  { href: '/admin/billing', label: 'Billing Ops', icon: <CreditCard className="w-5 h-5" />, matchPrefix: '/admin/billing' },
  { href: '/admin/flags', label: 'Feature Flags', icon: <Flag className="w-5 h-5" />, matchPrefix: '/admin/flags' },
  { href: '/admin/audit-logs', label: 'Audit Logs', icon: <ShieldCheck className="w-5 h-5" />, matchPrefix: '/admin/audit-logs' },
];

export default function AdminLayout({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { role, isAuthenticated, isLoading } = useAdminAuth();

  if (isLoading) {
    return (
      <div className="flex h-screen w-full items-center justify-center bg-gray-50">
        <div className="flex flex-col items-center gap-4 text-gray-500 font-sans">
          <Lock className="w-8 h-8 animate-pulse text-primary" />
          <p>Verifying Admin Credentials...</p>
        </div>
        <div className="hidden">{children}</div>
      </div>
    );
  }

  if (!isAuthenticated || role !== 'admin') {
    return (
      <div className="flex h-screen w-full items-center justify-center bg-gray-50 text-red-600 font-sans">
        <p>Access Denied. You do not have permission to view this console.</p>
        <div className="hidden">{children}</div>
      </div>
    );
  }

  return (
    <AppShell 
      navItems={adminNavItems} 
      mobileNavItems={adminNavItems} 
    >
      {children}
    </AppShell>
  );
}
