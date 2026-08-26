import { useState } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ManageAccessPanel } from './manage-access-panel';
import {
  createEmptyUserAccess,
  type UserAccess,
  type UserAccessSubscription,
} from '@/lib/user-access';

vi.mock('@/lib/materials-api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/materials-api')>();
  return {
    ...actual,
    adminListMaterialFolders: vi.fn().mockResolvedValue([]),
  };
});

vi.mock('@/lib/user-access', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/user-access')>();
  return {
    ...actual,
    fetchAdminBillingPlans: vi.fn().mockResolvedValue([]),
    fetchAdminAddons: vi.fn().mockResolvedValue([]),
    fetchAdminRecallSetTags: vi.fn().mockResolvedValue([]),
    fetchAllocatableVideos: vi.fn().mockResolvedValue([]),
  };
});

vi.mock('./folder-scope-picker', () => ({
  FolderScopePicker: ({ selectedIds }: { selectedIds: string[] }) => (
    <div data-testid="folder-scope-picker">Folders selected: {selectedIds.length}</div>
  ),
}));

vi.mock('./video-scope-picker', () => ({
  VideoScopePicker: ({ selectedIds }: { selectedIds: string[] }) => (
    <div data-testid="video-scope-picker">Videos selected: {selectedIds.length}</div>
  ),
}));

vi.mock('./recall-set-picker', () => ({
  RecallSetPicker: ({ selectedCodes }: { selectedCodes: string[] }) => (
    <div data-testid="recall-set-picker">Recall sets selected: {selectedCodes.length}</div>
  ),
}));

const eligiblePendingSubscription: UserAccessSubscription = {
  id: 'sub-pending-1',
  planCode: 'med-all-inclusive',
  planName: 'Medicine All-Inclusive',
  status: 'Pending',
  expiresAt: null,
  isPrimary: true,
  isPending: true,
};

function Harness({
  initialValue,
  onChange,
}: {
  initialValue: UserAccess;
  onChange?: (val: UserAccess) => void;
}) {
  const [value, setValue] = useState<UserAccess>(initialValue);

  return (
    <ManageAccessPanel
      value={value}
      onChange={(next) => {
        setValue(next);
        onChange?.(next);
      }}
      plans={[]}
      addons={[]}
      recallSets={[]}
      folderTree={[]}
      videos={[]}
    />
  );
}

describe('ManageAccessPanel — Automatic Package-Based Content Access', () => {
  it('renders module toggles for all four modules and no content sections when modules are disabled', () => {
    const emptyAccess = createEmptyUserAccess();
    const onChange = vi.fn();

    render(
      <ManageAccessPanel
        value={emptyAccess}
        onChange={onChange}
        plans={[]}
        addons={[]}
        recallSets={[]}
        folderTree={[]}
        videos={[]}
      />,
    );

    // Module toggles are present and unchecked
    const recallsCheckbox = screen.getByRole('checkbox', { name: /^recalls$/i });
    const materialsCheckbox = screen.getByRole('checkbox', { name: /^materials library$/i });
    const videosCheckbox = screen.getByRole('checkbox', { name: /^videos$/i });
    const mocksCheckbox = screen.getByRole('checkbox', { name: /^mocks$/i });

    expect(recallsCheckbox).toBeInTheDocument();
    expect(recallsCheckbox).not.toBeChecked();
    expect(materialsCheckbox).toBeInTheDocument();
    expect(materialsCheckbox).not.toBeChecked();
    expect(videosCheckbox).toBeInTheDocument();
    expect(videosCheckbox).not.toBeChecked();
    expect(mocksCheckbox).toBeInTheDocument();
    expect(mocksCheckbox).not.toBeChecked();

    // No content sections rendered when modules are disabled
    expect(screen.queryByText('Materials Library content')).not.toBeInTheDocument();
    expect(screen.queryByText('Video Library content')).not.toBeInTheDocument();
    expect(screen.queryByText('Recall content')).not.toBeInTheDocument();
    expect(screen.queryByText('Optional manual restriction')).not.toBeInTheDocument();
  });

  it('renders the "No active package" warning when a module is enabled without an eligible package', () => {
    const accessWithModuleWithoutPackage: UserAccess = {
      ...createEmptyUserAccess(),
      moduleOverrides: [
        { moduleKey: 'Recalls', enabled: true },
        { moduleKey: 'MaterialsLibrary', enabled: false },
        { moduleKey: 'VideoLibrary', enabled: false },
        { moduleKey: 'Mocks', enabled: false },
      ],
    };

    render(
      <ManageAccessPanel
        value={accessWithModuleWithoutPackage}
        onChange={vi.fn()}
        plans={[]}
        addons={[]}
        recallSets={[]}
        folderTree={[]}
        videos={[]}
      />,
    );

    expect(screen.getByText('No active package')).toBeInTheDocument();
    expect(
      screen.getByText(/the module toggles and content scope below will have no effect until a package is added/i),
    ).toBeInTheDocument();
  });

  it('renders content sections with collapsed optional manual restriction details when modules are enabled with an eligible package', () => {
    const accessWithEligiblePackage: UserAccess = {
      ...createEmptyUserAccess(),
      subscriptions: [eligiblePendingSubscription],
      moduleOverrides: [
        { moduleKey: 'Recalls', enabled: true },
        { moduleKey: 'MaterialsLibrary', enabled: true },
        { moduleKey: 'VideoLibrary', enabled: true },
        { moduleKey: 'Mocks', enabled: false },
      ],
    };

    const { container } = render(
      <ManageAccessPanel
        value={accessWithEligiblePackage}
        onChange={vi.fn()}
        plans={[]}
        addons={[]}
        recallSets={[]}
        folderTree={[]}
        videos={[]}
      />,
    );

    // Warning is absent because package is eligible
    expect(screen.queryByText('No active package')).not.toBeInTheDocument();

    // Three content sections are present
    expect(screen.getByText('Materials Library content')).toBeInTheDocument();
    expect(screen.getByText('Video Library content')).toBeInTheDocument();
    expect(screen.getByText('Recall content')).toBeInTheDocument();

    // Mocks content section is absent
    expect(screen.queryByText('Mocks content')).not.toBeInTheDocument();

    // Three details elements exist, all labeled "Optional manual restriction" and collapsed by default
    const detailsElements = container.querySelectorAll('details');
    expect(detailsElements).toHaveLength(3);

    const summaries = screen.getAllByText('Optional manual restriction');
    expect(summaries).toHaveLength(3);

    detailsElements.forEach((details) => {
      expect(details.open).toBe(false);
      expect(details.querySelector('summary')).toHaveTextContent('Optional manual restriction');
    });
  });

  it('preserves empty scope arrays (materialFolderIds, videoIds, recallSetCodes) when toggling modules', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    const initialAccess: UserAccess = {
      ...createEmptyUserAccess(),
      subscriptions: [eligiblePendingSubscription],
      moduleOverrides: [
        { moduleKey: 'Recalls', enabled: true },
        { moduleKey: 'MaterialsLibrary', enabled: true },
        { moduleKey: 'VideoLibrary', enabled: true },
        { moduleKey: 'Mocks', enabled: false },
      ],
      materialFolderIds: [],
      videoIds: [],
      recallSetCodes: [],
    };

    render(<Harness initialValue={initialAccess} onChange={onChange} />);

    // Toggle Mocks on
    const mocksCheckbox = screen.getByRole('checkbox', { name: /^mocks$/i });
    expect(mocksCheckbox).not.toBeChecked();

    await user.click(mocksCheckbox);
    expect(onChange).toHaveBeenCalled();

    let latest = onChange.mock.calls.at(-1)?.[0] as UserAccess;
    expect(latest.moduleOverrides.find((m) => m.moduleKey === 'Mocks')?.enabled).toBe(true);
    expect(latest.materialFolderIds).toEqual([]);
    expect(latest.videoIds).toEqual([]);
    expect(latest.recallSetCodes).toEqual([]);

    // Toggle Mocks back off
    await user.click(mocksCheckbox);
    latest = onChange.mock.calls.at(-1)?.[0] as UserAccess;
    expect(latest.moduleOverrides.find((m) => m.moduleKey === 'Mocks')?.enabled).toBe(false);
    expect(latest.materialFolderIds).toEqual([]);
    expect(latest.videoIds).toEqual([]);
    expect(latest.recallSetCodes).toEqual([]);

    // All emitted onChange calls must have empty scope arrays
    for (const [emitted] of onChange.mock.calls) {
      expect(emitted.materialFolderIds).toEqual([]);
      expect(emitted.videoIds).toEqual([]);
      expect(emitted.recallSetCodes).toEqual([]);
    }
  });

  it('displays "Automatic by default" helper copy in each enabled content section', () => {
    const access: UserAccess = {
      ...createEmptyUserAccess(),
      subscriptions: [eligiblePendingSubscription],
      moduleOverrides: [
        { moduleKey: 'Recalls', enabled: true },
        { moduleKey: 'MaterialsLibrary', enabled: true },
        { moduleKey: 'VideoLibrary', enabled: true },
        { moduleKey: 'Mocks', enabled: false },
      ],
    };

    render(
      <ManageAccessPanel
        value={access}
        onChange={vi.fn()}
        plans={[]}
        addons={[]}
        recallSets={[]}
        folderTree={[]}
        videos={[]}
      />,
    );

    const helperTexts = screen.getAllByText(/Automatic by default/i);
    expect(helperTexts).toHaveLength(3);

    expect(
      screen.getByText(/Automatic by default: leave the optional restriction below empty and the learner receives every Materials Library folder/i),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Automatic by default: leave the optional restriction below empty and the learner receives every video/i),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Automatic by default: leave the optional restriction below empty and the learner receives every recall set/i),
    ).toBeInTheDocument();
  });
});
