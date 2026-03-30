export interface DemoAuthAccount {
  email: string;
  label: string;
}

export const DEMO_AUTH_ACCOUNTS: DemoAuthAccount[] = [
  {
    email: "learner@edu80.app",
    label: "Learner demo",
  },
  {
    email: "expert@edu80.app",
    label: "Expert demo",
  },
  {
    email: "admin@edu80.app",
    label: "Admin demo",
  },
];

export function normalizeDemoEmail(email: string | null | undefined): string {
  return String(email ?? "")
    .trim()
    .toLowerCase();
}
