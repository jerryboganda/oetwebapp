import type { ReactNode } from "react";
import RoleSessionHydrator from "@/Component/OET/Providers/RoleSessionHydrator";
import { requireRoleAccess } from "@/lib/auth/session.server";

export default async function LearnerLayout({
  children,
}: {
  children: ReactNode;
}) {
  const user = await requireRoleAccess(["learner"]);

  return (
    <>
      <RoleSessionHydrator user={user} />
      {children}
    </>
  );
}
