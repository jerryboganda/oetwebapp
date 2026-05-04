import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { NoBillingPermission } from '@/components/admin/billing/no-billing-permission';

describe('admin billing RBAC empty state', () => {
  it('renders a 403-friendly empty state with the required permission', () => {
    render(<NoBillingPermission requiredPermission="billing:read" />);
    expect(screen.getByTestId('no-billing-permission')).toBeInTheDocument();
    expect(screen.getByText(/billing access required/i)).toBeInTheDocument();
    expect(screen.getByText(/billing:read/)).toBeInTheDocument();
  });

  it('lets the caller override the title and permission name', () => {
    render(<NoBillingPermission title="Wallet tiers locked" requiredPermission="billing:write" />);
    expect(screen.getByText(/wallet tiers locked/i)).toBeInTheDocument();
    expect(screen.getByText(/billing:write/)).toBeInTheDocument();
  });
});
