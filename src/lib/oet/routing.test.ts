import { describe, expect, it } from "vitest";
import {
  canAccessRolePath,
  getRoleFromPath,
  resolveRoleLandingPath,
} from "@/lib/oet/routing";

describe("oet routing helpers", () => {
  it("maps each role to its primary landing route", () => {
    expect(resolveRoleLandingPath("learner")).toBe("/learner/dashboard");
    expect(resolveRoleLandingPath("expert")).toBe("/expert/queue");
    expect(resolveRoleLandingPath("admin")).toBe("/admin/content");
  });

  it("infers the protected role surface from the pathname", () => {
    expect(getRoleFromPath("/learner/writing/tasks")).toBe("learner");
    expect(getRoleFromPath("/expert/review/writing/rev_1")).toBe("expert");
    expect(getRoleFromPath("/admin/analytics/quality")).toBe("admin");
    expect(getRoleFromPath("/auth-pages/sign-in")).toBe(null);
  });

  it("only grants access to the matching protected surface", () => {
    expect(canAccessRolePath("learner", "/learner/dashboard")).toBe(true);
    expect(canAccessRolePath("learner", "/expert/queue")).toBe(false);
    expect(canAccessRolePath("expert", "/expert/queue")).toBe(true);
    expect(canAccessRolePath("expert", "/admin/content")).toBe(false);
    expect(canAccessRolePath("admin", "/admin/content")).toBe(true);
    expect(canAccessRolePath("admin", "/learner/dashboard")).toBe(false);
  });
});
