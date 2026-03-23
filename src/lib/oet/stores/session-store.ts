"use client";

import { create } from "zustand";
import type { MockSessionUser } from "@/lib/auth/session";

interface SessionStoreState {
  user: MockSessionUser | null;
  setUser: (user: MockSessionUser | null) => void;
}

export const useSessionStore = create<SessionStoreState>((set) => ({
  setUser: (user) => set({ user }),
  user: null,
}));
