"use client";

import Link from "next/link";
import { IconUser, IconUserPlus } from "@tabler/icons-react";
import { AUTH_ROUTES } from "@/lib/auth/routes";
import styles from "./AuthBackgroundShell.module.scss";

interface AuthModeSwitchProps {
  mode: "signIn" | "signUp";
}

const switchItems = [
  {
    key: "signIn",
    label: "Login",
    href: AUTH_ROUTES.signIn,
    icon: IconUser,
  },
  {
    key: "signUp",
    label: "Sign Up",
    href: AUTH_ROUTES.signUp,
    icon: IconUserPlus,
  },
] as const;

export default function AuthModeSwitch({ mode }: AuthModeSwitchProps) {
  return (
    <div className={styles.switcher} aria-label="Authentication mode switch">
      {switchItems.map((item) => {
        const Icon = item.icon;
        const isActive = item.key === mode;

        return (
          <Link
            key={item.key}
            href={item.href}
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
