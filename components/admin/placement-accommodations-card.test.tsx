import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { PlacementAccommodation } from '@/lib/api/admin-placement';
import { ApiError } from '@/lib/api/client';

const FULL_ACCESS = ['learner:read', 'learner:write'];

const { mockFetch, mockGrant, mockRevoke, authState } = vi.hoisted(() => ({
  mockFetch: vi.fn(),
  mockGrant: vi.fn(),
  mockRevoke: vi.fn(),
  authState: { adminPermissions: [] as string[] },
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({ user: { adminPermissions: authState.adminPermissions } }),
}));

vi.mock('@/lib/api/admin-placement', () => ({
  fetchPlacementAccommodations: (...args: unknown[]) => mockFetch(...args),
  grantPlacementAccommodation: (...args: unknown[]) => mockGrant(...args),
  revokePlacementAccommodation: (...args: unknown[]) => mockRevoke(...args),
}));

import { PlacementAccommodationsCard } from './placement-accommodations-card';

function grant(overrides: Partial<PlacementAccommodation> = {}): PlacementAccommodation {
  return {
    id: 'acc-1',
    learnerUserId: 'user-1',
    learnerEmail: 'ali@example.com',
    learnerName: 'Ali Hassan',
    extraTimePercent: 50,
    reference: 'Case ref A-17',
    status: 'active',
    approvedByUserId: 'admin-1',
    approvedByName: 'Dr Admin',
    approvedAt: '2026-06-15T10:30:00.000Z',
    revokedByUserId: null,
    revokedByName: null,
    revokedAt: null,
    revokedReason: null,
    uses: [],
    ...overrides,
  };
}

const revokedGrant = () =>
  grant({
    id: 'acc-2',
    learnerUserId: 'user-2',
    learnerEmail: 'sara@example.com',
    learnerName: 'Sara Khan',
    status: 'revoked',
    revokedByUserId: 'admin-2',
    revokedByName: 'Dr Other',
    revokedAt: '2026-07-01T09:00:00.000Z',
    revokedReason: 'Entered in error',
  });

const grantButton = () => screen.getByRole('button', { name: /grant extra time/i });
const learnerInput = () => screen.getByLabelText(/learner email or user id/i);
const percentSelect = () => screen.getByLabelText(/extra time percentage/i);
const referenceInput = () => screen.getByLabelText(/administrative reference/i);

describe('PlacementAccommodationsCard', () => {
  beforeEach(() => {
    mockFetch.mockReset();
    mockGrant.mockReset();
    mockRevoke.mockReset();
    mockFetch.mockResolvedValue([]);
    authState.adminPermissions = FULL_ACCESS;
  });

  describe('listing', () => {
    it('shows a loading state, then each grant with its approver, date and status', async () => {
      let resolveFetch!: (rows: PlacementAccommodation[]) => void;
      mockFetch.mockReturnValue(
        new Promise<PlacementAccommodation[]>((resolve) => {
          resolveFetch = resolve;
        }),
      );

      render(<PlacementAccommodationsCard />);
      expect(screen.getByText(/loading accommodations/i)).toBeInTheDocument();

      resolveFetch([grant()]);
      const table = await screen.findByRole('table');
      const view = within(table);
      expect(view.getByText('Ali Hassan')).toBeInTheDocument();
      expect(view.getByText('ali@example.com')).toBeInTheDocument();
      expect(view.getByText('+50%')).toBeInTheDocument();
      expect(view.getByText('Active')).toBeInTheDocument();
      expect(view.getByText('Dr Admin')).toBeInTheDocument();
      expect(view.getByText(/2026/)).toBeInTheDocument();
      expect(view.getByText('Case ref A-17')).toBeInTheDocument();
      expect(view.getByText('None yet')).toBeInTheDocument();
      expect(mockFetch).toHaveBeenCalledWith({ includeRevoked: false });
    });

    it('shows an honest empty state when there are no grants', async () => {
      render(<PlacementAccommodationsCard />);

      expect(await screen.findByText(/no active accommodations/i)).toBeInTheDocument();
      expect(screen.queryByRole('table')).not.toBeInTheDocument();
    });

    it('shows the load error and recovers on retry', async () => {
      const user = userEvent.setup();
      mockFetch.mockRejectedValueOnce(new Error('Boom')).mockResolvedValueOnce([grant()]);

      render(<PlacementAccommodationsCard />);
      expect(await screen.findByText('Boom')).toBeInTheDocument();
      expect(screen.queryByText(/loading accommodations/i)).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: /retry/i }));

      expect(await screen.findByRole('table')).toBeInTheDocument();
      expect(screen.queryByText('Boom')).not.toBeInTheDocument();
    });

    it('loads active grants only until Show revoked is on, then shows who revoked and why', async () => {
      const user = userEvent.setup();
      mockFetch.mockImplementation(async (opts?: { includeRevoked?: boolean }) =>
        opts?.includeRevoked ? [grant(), revokedGrant()] : [grant()],
      );

      render(<PlacementAccommodationsCard />);
      await screen.findByRole('table');
      expect(mockFetch).toHaveBeenLastCalledWith({ includeRevoked: false });
      expect(screen.queryByText('Sara Khan')).not.toBeInTheDocument();
      expect(screen.getByText('1 active')).toBeInTheDocument();

      await user.click(screen.getByRole('checkbox', { name: /show revoked/i }));

      expect(await screen.findByText('Sara Khan')).toBeInTheDocument();
      expect(mockFetch).toHaveBeenLastCalledWith({ includeRevoked: true });
      expect(screen.getByText('1 active · 1 revoked')).toBeInTheDocument();
      expect(screen.getByText('Revoked')).toBeInTheDocument();
      expect(screen.getByText(/revoked by dr other/i)).toBeInTheDocument();
      expect(screen.getByText(/reason: entered in error/i)).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /revoke extra time for sara@example.com/i })).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: /revoke extra time for ali@example.com/i })).toBeInTheDocument();
    });

    it('filters the table by learner name, email or user id', async () => {
      const user = userEvent.setup();
      mockFetch.mockResolvedValue([grant(), { ...revokedGrant(), status: 'active' }]);

      render(<PlacementAccommodationsCard />);
      await screen.findByRole('table');

      const filter = screen.getByLabelText(/filter by learner/i);
      await user.type(filter, 'sara');
      expect(screen.queryByText('Ali Hassan')).not.toBeInTheDocument();
      expect(screen.getByText('Sara Khan')).toBeInTheDocument();

      await user.clear(filter);
      await user.type(filter, 'user-1');
      expect(screen.getByText('Ali Hassan')).toBeInTheDocument();
      expect(screen.queryByText('Sara Khan')).not.toBeInTheDocument();

      await user.clear(filter);
      await user.type(filter, 'nobody');
      expect(screen.getByText(/no accommodations match the filter/i)).toBeInTheDocument();
    });
  });

  describe('attempts used', () => {
    it('expands and collapses the list of attempts that used a grant', async () => {
      const user = userEvent.setup();
      mockFetch.mockResolvedValue([
        grant({
          uses: [
            { sessionId: 'sess-aaa', appliedAt: '2026-06-20T08:00:00.000Z', extraTimePercent: 50 },
            { sessionId: 'sess-bbb', appliedAt: '2026-06-21T08:00:00.000Z', extraTimePercent: 50 },
          ],
        }),
      ]);

      render(<PlacementAccommodationsCard />);
      const toggle = await screen.findByRole('button', { name: /2 attempts/i });
      expect(toggle).toHaveAttribute('aria-expanded', 'false');
      expect(screen.queryByText('sess-aaa')).not.toBeInTheDocument();

      await user.click(toggle);
      expect(toggle).toHaveAttribute('aria-expanded', 'true');
      expect(screen.getByText('sess-aaa')).toBeInTheDocument();
      expect(screen.getByText('sess-bbb')).toBeInTheDocument();
      expect(screen.getAllByText(/applied .*2026/)).toHaveLength(2);

      await user.click(toggle);
      expect(toggle).toHaveAttribute('aria-expanded', 'false');
      expect(screen.queryByText('sess-aaa')).not.toBeInTheDocument();
    });
  });

  describe('granting', () => {
    it('warns admins not to record medical details in the reference', async () => {
      render(<PlacementAccommodationsCard />);
      await screen.findByText(/no active accommodations/i);

      expect(
        screen.getByText('Administrative reference only - do not enter medical or personal health details'),
      ).toBeInTheDocument();
      expect(referenceInput()).toHaveAccessibleDescription(/do not enter medical or personal health details/i);
      expect(referenceInput()).toHaveAttribute('maxLength', '200');
    });

    it('validates the form before calling the API', async () => {
      const user = userEvent.setup();
      render(<PlacementAccommodationsCard />);
      await screen.findByText(/no active accommodations/i);

      await user.click(grantButton());
      expect(screen.getByText("Enter the learner's email address or user ID.")).toBeInTheDocument();
      expect(screen.getByText('Choose an extra-time percentage.')).toBeInTheDocument();

      await user.type(learnerInput(), 'not-an-email@');
      await user.selectOptions(percentSelect(), '50');
      await user.click(grantButton());
      expect(screen.getByText(/enter a valid email address/i)).toBeInTheDocument();
      expect(screen.queryByText('Choose an extra-time percentage.')).not.toBeInTheDocument();

      await user.clear(learnerInput());
      await user.type(learnerInput(), 'ali@example.com');
      fireEvent.change(referenceInput(), { target: { value: 'x'.repeat(201) } });
      await user.click(grantButton());
      expect(screen.getByText(/keep the reference to 200 characters or fewer/i)).toBeInTheDocument();

      expect(mockGrant).not.toHaveBeenCalled();
    });

    it('offers only the 25, 50, 75 and 100 percent presets', async () => {
      render(<PlacementAccommodationsCard />);
      await screen.findByText(/no active accommodations/i);

      const options = within(percentSelect()).getAllByRole('option').map((option) => option.textContent);
      expect(options).toEqual(['Select percentage', '+25%', '+50%', '+75%', '+100%']);
    });

    it('grants by email, sends a trimmed reference, refreshes the list and resets the form', async () => {
      const user = userEvent.setup();
      mockGrant.mockResolvedValue(grant({ id: 'acc-new', learnerEmail: 'new@example.com', extraTimePercent: 75 }));
      mockFetch
        .mockResolvedValueOnce([])
        .mockResolvedValueOnce([
          grant({ id: 'acc-new', learnerEmail: 'new@example.com', learnerName: null, extraTimePercent: 75 }),
        ]);

      render(<PlacementAccommodationsCard />);
      await screen.findByText(/no active accommodations/i);

      await user.type(learnerInput(), '  new@example.com ');
      await user.selectOptions(percentSelect(), '75');
      await user.type(referenceInput(), ' Case ref A-17 ');
      await user.click(grantButton());

      await waitFor(() =>
        expect(mockGrant).toHaveBeenCalledWith({
          learnerEmail: 'new@example.com',
          extraTimePercent: 75,
          reference: 'Case ref A-17',
        }),
      );
      expect(await screen.findByText(/extra time granted: \+75% for new@example\.com/i)).toBeInTheDocument();
      expect(await screen.findByRole('table')).toBeInTheDocument();
      expect(mockFetch).toHaveBeenCalledTimes(2);
      expect(learnerInput()).toHaveValue('');
      expect(percentSelect()).toHaveValue('');
      expect(referenceInput()).toHaveValue('');
    });

    it('treats an entry without @ as a user id and omits a blank reference', async () => {
      const user = userEvent.setup();
      mockGrant.mockResolvedValue(grant());

      render(<PlacementAccommodationsCard />);
      await screen.findByText(/no active accommodations/i);

      await user.type(learnerInput(), 'user-42');
      await user.selectOptions(percentSelect(), '25');
      await user.click(grantButton());

      await waitFor(() => expect(mockGrant).toHaveBeenCalledWith({ learnerUserId: 'user-42', extraTimePercent: 25 }));
    });

    it('shows the API error and keeps what the admin typed', async () => {
      const user = userEvent.setup();
      mockGrant.mockRejectedValue(new Error('Learner not found'));

      render(<PlacementAccommodationsCard />);
      await screen.findByText(/no active accommodations/i);

      await user.type(learnerInput(), 'ghost@example.com');
      await user.selectOptions(percentSelect(), '100');
      await user.click(grantButton());

      expect(await screen.findByText('Learner not found')).toBeInTheDocument();
      expect(learnerInput()).toHaveValue('ghost@example.com');
      expect(percentSelect()).toHaveValue('100');
      expect(mockFetch).toHaveBeenCalledTimes(1);
    });
  });

  describe('revoking', () => {
    const openRevoke = async (user: ReturnType<typeof userEvent.setup>) =>
      user.click(await screen.findByRole('button', { name: /revoke extra time for ali@example.com/i }));

    it('asks for confirmation first, and cancelling changes nothing', async () => {
      const user = userEvent.setup();
      mockFetch.mockResolvedValue([grant()]);

      render(<PlacementAccommodationsCard />);
      await openRevoke(user);

      const confirmForm = screen.getByRole('form', { name: /confirm revoking extra time for ali@example.com/i });
      expect(mockRevoke).not.toHaveBeenCalled();
      expect(within(confirmForm).getByLabelText(/reason \(optional\)/i)).toBeInTheDocument();

      await user.click(within(confirmForm).getByRole('button', { name: /cancel/i }));

      expect(screen.queryByRole('form', { name: /confirm revoking/i })).not.toBeInTheDocument();
      expect(mockRevoke).not.toHaveBeenCalled();
      expect(screen.getByRole('button', { name: /revoke extra time for ali@example.com/i })).toHaveFocus();
    });

    it('revokes with the optional reason and refreshes the list', async () => {
      const user = userEvent.setup();
      mockRevoke.mockResolvedValue(grant({ status: 'revoked' }));
      // The list is active-only, so the revoked grant is gone after the refresh.
      mockFetch.mockResolvedValueOnce([grant()]).mockResolvedValueOnce([]);

      render(<PlacementAccommodationsCard />);
      await openRevoke(user);
      await user.type(screen.getByLabelText(/reason \(optional\)/i), 'Entered in error');
      await user.click(screen.getByRole('button', { name: /confirm revoke/i }));

      await waitFor(() => expect(mockRevoke).toHaveBeenCalledWith('acc-1', 'Entered in error'));
      expect(await screen.findByText(/extra time revoked for ali@example\.com/i)).toBeInTheDocument();
      expect(await screen.findByText(/no active accommodations/i)).toBeInTheDocument();
      expect(screen.queryByRole('table')).not.toBeInTheDocument();
      expect(mockFetch).toHaveBeenCalledTimes(2);
      expect(screen.queryByRole('form', { name: /confirm revoking/i })).not.toBeInTheDocument();
    });

    it('keeps the confirm step open and shows the error when the revoke fails', async () => {
      const user = userEvent.setup();
      mockRevoke.mockRejectedValue(new Error('Already revoked'));
      mockFetch.mockResolvedValue([grant()]);

      render(<PlacementAccommodationsCard />);
      await openRevoke(user);
      await user.click(screen.getByRole('button', { name: /confirm revoke/i }));

      expect(await screen.findByText('Already revoked')).toBeInTheDocument();
      expect(mockRevoke).toHaveBeenCalledWith('acc-1', undefined);
      expect(screen.getByRole('form', { name: /confirm revoking/i })).toBeInTheDocument();
      expect(mockFetch).toHaveBeenCalledTimes(1);
    });
  });

  describe('permissions', () => {
    const grantForm = () => screen.queryByRole('form', { name: /grant extra time/i });

    it('without learner:read explains the missing permission instead of loading or showing controls', () => {
      // The page is gated by review_ops alone, which does not carry learner:*.
      authState.adminPermissions = ['review_ops'];

      render(<PlacementAccommodationsCard />);

      expect(screen.getByText(/need the learner:read permission/i)).toBeInTheDocument();
      expect(mockFetch).not.toHaveBeenCalled();
      expect(grantForm()).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /retry|refresh|revoke/i })).not.toBeInTheDocument();
      expect(screen.queryByRole('table')).not.toBeInTheDocument();
    });

    it('with learner:read only shows a read-only audit view with no grant form and no revoke action', async () => {
      authState.adminPermissions = ['learner:read'];
      mockFetch.mockResolvedValue([
        grant({ uses: [{ sessionId: 'sess-aaa', appliedAt: '2026-06-20T08:00:00.000Z', extraTimePercent: 50 }] }),
      ]);

      render(<PlacementAccommodationsCard />);

      const table = await screen.findByRole('table');
      expect(within(table).getByText('Ali Hassan')).toBeInTheDocument();
      expect(screen.getByText(/needs the learner:write permission/i)).toBeInTheDocument();
      expect(grantForm()).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /revoke extra time/i })).not.toBeInTheDocument();
      expect(within(table).queryByRole('columnheader', { name: /action/i })).not.toBeInTheDocument();
      expect(within(table).getAllByRole('columnheader')).toHaveLength(6);
    });

    it('with learner:read and learner:write shows the grant form and revoke action', async () => {
      authState.adminPermissions = ['learner:read', 'learner:write'];
      mockFetch.mockResolvedValue([grant()]);

      render(<PlacementAccommodationsCard />);

      await screen.findByRole('table');
      expect(grantForm()).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /revoke extra time for ali@example.com/i })).toBeInTheDocument();
      expect(screen.queryByText(/needs the learner:write permission/i)).not.toBeInTheDocument();
    });

    it('treats system_admin as satisfying both permissions', async () => {
      authState.adminPermissions = ['system_admin'];
      mockFetch.mockResolvedValue([grant()]);

      render(<PlacementAccommodationsCard />);

      await screen.findByRole('table');
      expect(grantForm()).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /revoke extra time for ali@example.com/i })).toBeInTheDocument();
    });

    it('does not offer Retry when the list load is refused with a 403', async () => {
      mockFetch.mockRejectedValue(new ApiError(403, 'forbidden', 'Forbidden', false));

      render(<PlacementAccommodationsCard />);

      expect(await screen.findByText(/do not have permission/i)).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /retry/i })).not.toBeInTheDocument();
    });
  });
});
