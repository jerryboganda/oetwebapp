"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { User, UserPlus } from "lucide-react";
import { appendAuthNextParam, AUTH_ROUTES } from "@/lib/auth/routes";
import styles from "./auth-screen-shell.module.scss";

interface AuthModeSwitchProps {
  mode: "signIn" | "signUp";
}

const switchItems = [
  {
    key: "signIn",
    label: "Login",
    href: AUTH_ROUTES.signIn,
    icon: User,
  },
  {
    key: "signUp",
    label: "Sign Up",
    href: AUTH_ROUTES.signUp,
    icon: UserPlus,
  },
] as const;

export function AuthModeSwitch({ mode }: AuthModeSwitchProps) {
  const searchParams = useSearchParams();
  const nextPath = searchParams?.get("next") ?? null;

  return (
    <div className={styles.switcher} aria-label="Authentication mode switch">
      {switchItems.map((item) => {
        const Icon = item.icon;
        const isActive = item.key === mode;

        return (
          <Link
            key={item.key}
            href={appendAuthNextParam(item.href, nextPath)}
            className={`${styles.switcherLink} ${
              isActive ? styles.switcherActive : ""
            }`}
            aria-current={isActive ? "page" : undefined}
          >
            <Icon size={18} />
            <span>{item.label}</span>
          </Link>
        );
      })}
    </div>
  );
}

export default AuthModeSwitch;
