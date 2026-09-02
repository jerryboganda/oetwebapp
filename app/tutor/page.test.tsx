import { describe, expect, it, vi } from 'vitest';
import { redirect } from 'next/navigation';
import TutorPage from './page';

vi.mock('next/navigation', () => ({
  redirect: vi.fn(),
}));

describe('Tutor root page', () => {
  it('redirects to /tutor/dashboard', () => {
    TutorPage();
    expect(redirect).toHaveBeenCalledWith('/tutor/dashboard');
  });
});
