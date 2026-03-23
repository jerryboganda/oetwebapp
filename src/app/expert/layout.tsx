import type { ReactNode } from "react";
import RoleSessionHydrator from "@/Component/OET/Providers/RoleSessionHydrator";
import { requireRoleAccess } from "@/lib/auth/session.server";

export default async function ExpertLayout({
  children,
}: {
  children: ReactNode;
}) {
  const user = await requireRoleAccess(["expert"]);

  return (
    <>
      <RoleSessionHydrator user={user} />
      {children}
    </>
  );
}
