"use client";

import { useEffect } from "react";
import { useSessionStore } from "@/lib/oet/stores/session-store";
import type { MockSessionUser } from "@/lib/auth/session";

interface RoleSessionHydratorProps {
  user: MockSessionUser | null;
}

const RoleSessionHydrator = ({ user }: RoleSessionHydratorProps) => {
  const setUser = useSessionStore((state) => state.setUser);

  useEffect(() => {
    setUser(user);
  }, [setUser, user]);

  return null;
};

export default RoleSessionHydrator;
