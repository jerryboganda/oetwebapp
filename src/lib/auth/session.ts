export interface MockSessionUser {
  username: string;
  fullName: string;
  role: "learner" | "expert" | "admin";
  avatarUrl: string;
}

export const MOCK_SESSION_USERS: MockSessionUser[] = [
  {
    username: "learner@oet.app",
    fullName: "Aisha Khan",
    role: "learner",
    avatarUrl: "/images/ai_avatar/3.jpg",
  },
  {
    username: "expert@oet.app",
    fullName: "Daniel Carter",
    role: "expert",
    avatarUrl: "/images/ai_avatar/5.jpg",
  },
  {
    username: "admin@oet.app",
    fullName: "Sarah Malik",
    role: "admin",
    avatarUrl: "/images/ai_avatar/2.jpg",
  },
];

export function findMockUserByUsername(
  username: string | null | undefined
): MockSessionUser | undefined {
  if (!username) {
    return undefined;
  }

  const normalized = username.toLowerCase().trim();

  if (normalized === "learner@edu80.app") {
    return MOCK_SESSION_USERS.find((user) => user.role === "learner");
  }

  return MOCK_SESSION_USERS.find(
    (user) => user.username.toLowerCase() === normalized
  );
}

export function resolveLoginRedirectPath(
  role: MockSessionUser["role"]
): string {
  switch (role) {
    case "expert":
      return "/reviewer/queue";
    case "admin":
      return "/cms/content";
    case "learner":
    default:
      return "/learner/dashboard";
  }
}
