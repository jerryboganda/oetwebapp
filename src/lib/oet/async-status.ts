import type { AsyncWorkflowMeta, AsyncWorkflowStatus } from "@/types/oet";

const ASYNC_WORKFLOW_META: Record<AsyncWorkflowStatus, AsyncWorkflowMeta> = {
  queued: {
    badgeClass: "secondary",
    description: "Queued for processing",
    label: "Queued",
    retryable: false,
  },
  processing: {
    badgeClass: "warning",
    description: "Currently processing",
    label: "Processing",
    retryable: false,
  },
  completed: {
    badgeClass: "success",
    description: "Completed and ready to review",
    label: "Completed",
    retryable: false,
  },
  failed: {
    badgeClass: "danger",
    description: "Action needed to retry or recover",
    label: "Failed",
    retryable: true,
  },
};

export function getAsyncWorkflowMeta(
  status: AsyncWorkflowStatus
): AsyncWorkflowMeta {
  return ASYNC_WORKFLOW_META[status];
}
