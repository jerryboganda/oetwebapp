import { z } from "zod";
import { isTargetCountry } from "./target-countries";

export const signupPayloadSchema = z
  .object({
    agreeToPrivacy: z.boolean(),
    agreeToTerms: z.boolean(),
    confirmPassword: z.string().min(8, "Confirm your password"),
    countryTarget: z
      .string()
      .min(2, "Select your target country")
      .refine(isTargetCountry, "Select a valid target country"),
    email: z.email("Enter a valid email address"),
    examDate: z
      .string()
      .min(1, "Select your target exam date")
      .refine((value) => {
        const parsed = new Date(`${value}T00:00:00`);
        if (Number.isNaN(parsed.getTime())) return false;
        const today = new Date();
        today.setHours(0, 0, 0, 0);
        return parsed >= today;
      }, "Target exam date must be today or later"),
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

export type SignupPayloadFormValues = z.input<typeof signupPayloadSchema>;
