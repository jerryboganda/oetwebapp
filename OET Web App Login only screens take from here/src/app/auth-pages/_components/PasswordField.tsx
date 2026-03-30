"use client";

import React, { forwardRef, InputHTMLAttributes, useId } from "react";
import ThemedPasswordInput from "@/app/auth-pages/_components/ThemedPasswordInput";
import styles from "./AuthBackgroundShell.module.scss";

interface PasswordFieldProps extends Omit<
  InputHTMLAttributes<HTMLInputElement>,
  "type"
> {
  label: string;
  hint?: string;
}

const PasswordField = forwardRef<HTMLInputElement, PasswordFieldProps>(
  ({ label, hint, id, className, ...props }, ref) => {
    const generatedId = useId();
    const inputId = id ?? generatedId;

    return (
      <div className={styles.field}>
        <label htmlFor={inputId}>{label}</label>
        <ThemedPasswordInput
          {...props}
          id={inputId}
          ref={ref}
          className={[styles.input, styles.passwordInput, className]
            .filter(Boolean)
            .join(" ")}
          wrapperClassName={styles.passwordWrap}
          toggleClassName={styles.passwordToggle}
        />
        {hint ? <p className={styles.fieldHint}>{hint}</p> : null}
      </div>
    );
  }
);

PasswordField.displayName = "PasswordField";

export default PasswordField;
