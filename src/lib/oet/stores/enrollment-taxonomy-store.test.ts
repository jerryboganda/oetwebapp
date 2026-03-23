import { beforeEach, describe, expect, it } from "vitest";
import { useEnrollmentTaxonomyStore } from "@/lib/oet/stores/enrollment-taxonomy-store";

describe("enrollment taxonomy store", () => {
  beforeEach(() => {
    localStorage.removeItem("oet-enrollment-taxonomy");
    useEnrollmentTaxonomyStore.getState().reset();
  });

  it("creates linked taxonomy entries that can be reused by signup", () => {
    const store = useEnrollmentTaxonomyStore.getState();

    store.createExamType({
      code: "PTE",
      description: "Pearson preparation track",
      label: "PTE",
      status: "active",
    });

    const createdExam = useEnrollmentTaxonomyStore
      .getState()
      .examTypes.find((item) => item.code === "PTE");

    expect(createdExam).toBeTruthy();

    store.createProfession({
      countryTargets: ["Australia"],
      description: "General PTE pathway",
      examTypeIds: [createdExam!.id],
      label: "PTE Candidate",
      status: "active",
    });

    const createdProfession = useEnrollmentTaxonomyStore
      .getState()
      .professions.find((item) => item.label === "PTE Candidate");

    expect(createdProfession?.examTypeIds).toContain(createdExam!.id);

    store.createSession({
      capacity: 24,
      currency: "USD",
      deliveryMode: "online",
      description: "Weekend coaching",
      endDate: "2026-08-12",
      enrollmentOpen: true,
      examTypeId: createdExam!.id,
      name: "PTE Weekend Cohort",
      priceLabel: "$129",
      professionIds: [createdProfession!.id],
      seatsRemaining: 24,
      startDate: "2026-07-19",
      status: "open",
      timezone: "Asia/Karachi",
    });

    expect(
      useEnrollmentTaxonomyStore
        .getState()
        .sessions.some((item) => item.name === "PTE Weekend Cohort")
    ).toBe(true);
  });

  it("cascades exam deletion into linked professions and sessions", () => {
    const store = useEnrollmentTaxonomyStore.getState();
    const examId = store.examTypes[0]!.id;

    store.createProfession({
      countryTargets: ["Canada"],
      description: "Temporary linked profession",
      examTypeIds: [examId],
      label: "Linked Profession",
      status: "active",
    });

    const professionId = useEnrollmentTaxonomyStore
      .getState()
      .professions.find((item) => item.label === "Linked Profession")!.id;

    store.createSession({
      capacity: 10,
      currency: "USD",
      deliveryMode: "online",
      description: "Temporary linked session",
      endDate: "2026-09-10",
      enrollmentOpen: true,
      examTypeId: examId,
      name: "Linked Session",
      priceLabel: "$99",
      professionIds: [professionId],
      seatsRemaining: 10,
      startDate: "2026-09-01",
      status: "open",
      timezone: "Asia/Karachi",
    });

    store.deleteExamType(examId);

    const nextState = useEnrollmentTaxonomyStore.getState();
    expect(nextState.examTypes.some((item) => item.id === examId)).toBe(false);
    expect(
      nextState.professions.find((item) => item.id === professionId)
        ?.examTypeIds
    ).not.toContain(examId);
    expect(
      nextState.sessions.some((item) => item.name === "Linked Session")
    ).toBe(false);
  });
});
