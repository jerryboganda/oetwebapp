import type { ReactNode } from "react";
import RoleSessionHydrator from "@/Component/OET/Providers/RoleSessionHydrator";
import { requireRoleAccess } from "@/lib/auth/session.server";

export default async function AdminLayout({
  children,
}: {
  children: ReactNode;
}) {
  const user = await requireRoleAccess(["admin"]);

  return (
    <>
      <RoleSessionHydrator user={user} />
      {children}
    </>
  );
}
