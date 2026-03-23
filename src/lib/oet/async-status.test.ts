import { describe, expect, it } from "vitest";
import { getAsyncWorkflowMeta } from "@/lib/oet/async-status";

describe("async workflow metadata", () => {
  it("returns the correct presentation for queued jobs", () => {
    expect(getAsyncWorkflowMeta("queued")).toEqual({
      badgeClass: "secondary",
      description: "Queued for processing",
      label: "Queued",
      retryable: false,
    });
  });

  it("marks failed jobs as retryable", () => {
    expect(getAsyncWorkflowMeta("failed")).toEqual({
      badgeClass: "danger",
      description: "Action needed to retry or recover",
      label: "Failed",
      retryable: true,
    });
  });
});
