import { z } from "zod";

export const learnerGoalSchema = z.object({
  examDate: z.string().optional(),
  previousAttempts: z.string().optional(),
  professionId: z.string().min(1, "Profession is required"),
  targetCountry: z.string().optional(),
  targetListening: z.string().optional(),
  targetReading: z.string().optional(),
  targetSpeaking: z.string().optional(),
  targetWriting: z.string().optional(),
  weakSubtests: z.array(z.string()),
  weeklyStudyHours: z.coerce
    .number()
    .min(1, "Enter at least 1 hour")
    .max(40, "Enter 40 hours or fewer"),
});

export const contentBuilderSchema = z.object({
  criteriaFocus: z.array(z.string()).min(1, "Select at least one criterion"),
  difficulty: z.enum(["foundation", "target", "stretch"]),
  durationMinutes: z.coerce
    .number()
    .min(5, "Duration must be at least 5 minutes")
    .max(240, "Duration must be 240 minutes or fewer"),
  metadataNotes: z.string().min(10, "Add short rubric or model-answer notes"),
  professionId: z.string().min(1, "Select a profession"),
  subtest: z.enum(["writing", "speaking", "reading", "listening"]),
  title: z.string().min(3, "Title is required"),
});

export const signupPayloadSchema = z
  .object({
    agreeToPrivacy: z.boolean(),
    agreeToTerms: z.boolean(),
    confirmPassword: z.string().min(8, "Confirm your password"),
    countryTarget: z.string().min(2, "Select your target country"),
    email: z.email("Enter a valid email address"),
    examTypeId: z.string().min(1, "Select an exam"),
    firstName: z.string().min(2, "First name is required"),
    lastName: z.string().min(2, "Last name is required"),
    marketingOptIn: z.boolean(),
    mobileNumber: z
      .string()
      .min(7, "Mobile number is required")
      .max(20, "Enter a valid mobile number"),
    password: z.string().min(8, "Password must be at least 8 characters"),
    professionId: z.string().min(1, "Select a profession"),
    sessionId: z.string().min(1, "Select a session"),
  })
  .refine((value) => value.password === value.confirmPassword, {
    message: "Passwords must match",
    path: ["confirmPassword"],
  })
  .refine((value) => value.agreeToTerms, {
    message: "Accept the terms to continue",
    path: ["agreeToTerms"],
  })
  .refine((value) => value.agreeToPrivacy, {
    message: "Accept the privacy policy to continue",
    path: ["agreeToPrivacy"],
  });

export type LearnerGoalFormValues = z.input<typeof learnerGoalSchema>;
export type ContentBuilderFormValues = z.input<typeof contentBuilderSchema>;
export type SignupPayloadFormValues = z.input<typeof signupPayloadSchema>;
