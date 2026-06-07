'use client';

import { useCallback, useMemo, useState, type ReactNode } from 'react';

import { BulkActionBar, type BulkAction } from '@/components/ui/bulk-action-bar';
import { BulkActionConfirmModal } from '@/components/ui/bulk-action-confirm-modal';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Pagination } from '@/components/ui/pagination';

/**
 * Result of a bulk operation. Numeric fields are summed across chunks by the
 * wrapper; `errors` arrays are concatenated. All fields except the first two
 * are optional so callers only report what they have.
 */
export interface BulkResult {
  totalRequested: number;
  succeeded: number;
  skipped?: number;
  failed?: number;
  errors?: string[];
  message?: string;
}

export interface ManagedBulkAction<T> {
  key: string;
  label: string;
  icon?: ReactNode;
  variant?: 'danger' | 'primary' | 'secondary';
  /** Status/permission gating. An action is disabled if any selected row fails this. */
  isEligible?: (row: T) => boolean;
  /** When present, clicking the action opens a confirmation modal first. */
  confirm?: {
    title: (n: number) => string;
    description: (n: number) => string;
    confirmLabel?: string;
    destructive?: boolean;
    requireReason?: boolean;
    reasonLabel?: string;
    reasonPlaceholder?: string;
  };
  /** Performs the operation for one chunk of ids. */
  run: (ids: string[], reason?: string) => Promise<BulkResult>;
}

export interface AdminManagedTableProps<T> {
  columns: Column<T>[];
  data: T[];
  keyExtractor: (row: T) => string;
  total: number;
  loading?: boolean;
  page: number;
  pageSize: number;
  onPageChange: (p: number) => void;
  onPageSizeChange: (s: number) => void;
  pageSizeOptions?: number[];
  itemLabel: string;
  itemLabelPlural: string;
  bulkActions: ManagedBulkAction<T>[];
  /** Max ids per `run` call. Defaults to 1000. */
  chunkSize?: number;
  onResult: (action: ManagedBulkAction<T>, result: BulkResult) => void;
  onError: (action: ManagedBulkAction<T>, error: unknown) => void;
  emptyState?: ReactNode;
}

const DEFAULT_CHUNK_SIZE = 1000;

/** Sums numeric BulkResult fields and concatenates errors across chunk results. */
function mergeResults(results: BulkResult[]): BulkResult {
  return results.reduce<BulkResult>(
    (acc, r) => ({
      totalRequested: acc.totalRequested + r.totalRequested,
      succeeded: acc.succeeded + r.succeeded,
      skipped: (acc.skipped ?? 0) + (r.skipped ?? 0),
      failed: (acc.failed ?? 0) + (r.failed ?? 0),
      errors: [...(acc.errors ?? []), ...(r.errors ?? [])],
      message: acc.message ?? r.message,
    }),
    { totalRequested: 0, succeeded: 0, skipped: 0, failed: 0, errors: [] },
  );
}

function chunk<T>(items: T[], size: number): T[][] {
  const out: T[][] = [];
  for (let i = 0; i < items.length; i += size) {
    out.push(items.slice(i, i + size));
  }
  return out;
}

export function AdminManagedTable<T>({
  columns,
  data,
  keyExtractor,
  total,
  loading,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
  pageSizeOptions,
  itemLabel,
  itemLabelPlural,
  bulkActions,
  chunkSize = DEFAULT_CHUNK_SIZE,
  onResult,
  onError,
  emptyState,
}: AdminManagedTableProps<T>) {
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());
  const [runningKey, setRunningKey] = useState<string | null>(null);
  const [pendingAction, setPendingAction] = useState<ManagedBulkAction<T> | null>(null);

  const selectedRows = useMemo(
    () => data.filter((row) => selectedKeys.has(keyExtractor(row))),
    [data, selectedKeys, keyExtractor],
  );

  const selectedCount = selectedKeys.size;
  const isBusy = runningKey !== null;

  const execute = useCallback(
    async (action: ManagedBulkAction<T>, reason?: string) => {
      const ids = Array.from(selectedKeys);
      if (ids.length === 0) return;

      setRunningKey(action.key);
      try {
        const chunks = chunk(ids, Math.max(1, chunkSize));
        const results: BulkResult[] = [];
        for (const part of chunks) {
          // Sequential by design: backends rate-limit / cap per request.
          results.push(await action.run(part, reason));
        }
        onResult(action, mergeResults(results));
        setSelectedKeys(new Set());
      } catch (error) {
        onError(action, error);
      } finally {
        setRunningKey(null);
      }
    },
    [selectedKeys, chunkSize, onResult, onError],
  );

  const handleActionClick = useCallback(
    (action: ManagedBulkAction<T>) => {
      if (action.confirm) {
        setPendingAction(action);
      } else {
        void execute(action);
      }
    },
    [execute],
  );

  const barActions: BulkAction[] = useMemo(
    () =>
      bulkActions.map((action) => {
        const ineligible =
          action.isEligible !== undefined &&
          selectedRows.some((row) => !action.isEligible!(row));
        return {
          key: action.key,
          label: action.label,
          icon: action.icon,
          variant: action.variant,
          disabled: selectedCount === 0 || ineligible || isBusy,
          loading: runningKey === action.key,
          onClick: () => handleActionClick(action),
        };
      }),
    [bulkActions, selectedRows, selectedCount, isBusy, runningKey, handleActionClick],
  );

  const confirm = pendingAction?.confirm;

  const showCustomEmpty = data.length === 0 && emptyState != null;

  return (
    <div className="flex flex-col gap-4">
      {showCustomEmpty ? (
        emptyState
      ) : (
        <DataTable
          columns={columns}
          data={data}
          keyExtractor={(row) => keyExtractor(row)}
          selectable
          selectedKeys={selectedKeys}
          onSelectionChange={setSelectedKeys}
        />
      )}

      <BulkActionBar
        selectedCount={selectedCount}
        totalCount={total}
        actions={barActions}
        onClearSelection={() => setSelectedKeys(new Set())}
        onSelectAll={() => setSelectedKeys(new Set(data.map((row) => keyExtractor(row))))}
      />

      <Pagination
        page={page}
        pageSize={pageSize}
        total={total}
        onPageChange={onPageChange}
        onPageSizeChange={onPageSizeChange}
        pageSizeOptions={pageSizeOptions}
        itemLabel={itemLabel}
        itemLabelPlural={itemLabelPlural}
      />

      {pendingAction && confirm ? (
        <BulkActionConfirmModal
          open
          title={confirm.title(selectedCount)}
          description={confirm.description(selectedCount)}
          confirmLabel={confirm.confirmLabel}
          destructive={confirm.destructive}
          requireReason={confirm.requireReason}
          reasonLabel={confirm.reasonLabel}
          reasonPlaceholder={confirm.reasonPlaceholder}
          loading={runningKey === pendingAction.key}
          onConfirm={(reason) => {
            const action = pendingAction;
            setPendingAction(null);
            void execute(action, reason);
          }}
          onClose={() => setPendingAction(null)}
        />
      ) : null}
    </div>
  );
}
