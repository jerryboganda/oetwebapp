import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

const { mockSignIn } = vi.hoisted(() => ({
  mockSignIn: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({
    replace: vi.fn(),
  }),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({
    signIn: mockSignIn,
  }),
}));

vi.mock('@/lib/auth-client', () => ({
  buildExternalAuthStartHref: vi.fn((_provider: string) => '#'),
}));

import { SignInForm } from '../sign-in-form';

describe('SignInForm', () => {
  it('uses explicit auth copy and ergonomic field attributes', () => {
    render(<SignInForm nextHref="/study-plan" />);

    expect(screen.getByRole('button', { name: 'Sign In' })).toBeInTheDocument();

    const emailInput = screen.getByLabelText(/email address/i);
    expect(emailInput).toHaveAttribute('name', 'email');
    expect(emailInput).toHaveAttribute('autocomplete', 'email');
    expect(emailInput).toHaveAttribute('inputmode', 'email');
    expect(emailInput).toHaveAttribute('spellcheck', 'false');

    const passwordInput = screen.getByLabelText(/^password$/i);
    expect(passwordInput).toHaveAttribute('name', 'password');
    expect(passwordInput).toHaveAttribute('autocomplete', 'current-password');
  });
});
