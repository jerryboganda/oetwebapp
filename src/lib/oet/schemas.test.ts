import { describe, expect, it } from "vitest";
import {
  contentBuilderSchema,
  learnerGoalSchema,
  signupPayloadSchema,
} from "@/lib/oet/schemas";

describe("OET schemas", () => {
  it("accepts a valid signup payload and rejects mismatched passwords", () => {
    const valid = signupPayloadSchema.safeParse({
      agreeToPrivacy: true,
      agreeToTerms: true,
      confirmPassword: "password123",
      countryTarget: "Australia",
      email: "learner@oet.app",
      examTypeId: "oet",
      firstName: "Aisha",
      lastName: "Khan",
      marketingOptIn: true,
      mobileNumber: "+923001234567",
      password: "password123",
      professionId: "nursing",
      sessionId: "session-oet-nursing-apr",
    });

    const invalid = signupPayloadSchema.safeParse({
      agreeToPrivacy: true,
      agreeToTerms: true,
      confirmPassword: "password124",
      countryTarget: "Australia",
      email: "learner@oet.app",
      examTypeId: "oet",
      firstName: "Aisha",
      lastName: "Khan",
      marketingOptIn: false,
      mobileNumber: "+923001234567",
      password: "password123",
      professionId: "nursing",
      sessionId: "session-oet-nursing-apr",
    });

    expect(valid.success).toBe(true);
    expect(invalid.success).toBe(false);
  });

  it("coerces learner and content numeric inputs from string form values", () => {
    const learnerResult = learnerGoalSchema.parse({
      examDate: "",
      previousAttempts: "",
      professionId: "nursing",
      targetCountry: "Australia",
      targetListening: "",
      targetReading: "",
      targetSpeaking: "",
      targetWriting: "",
      weakSubtests: [],
      weeklyStudyHours: "12",
    });

    const contentResult = contentBuilderSchema.parse({
      criteriaFocus: ["purpose"],
      difficulty: "target",
      durationMinutes: "45",
      metadataNotes: "Rubric notes for a strong writing task.",
      professionId: "nursing",
      subtest: "writing",
      title: "Referral letter sprint",
    });

    expect(learnerResult.weeklyStudyHours).toBe(12);
    expect(contentResult.durationMinutes).toBe(45);
  });
});
