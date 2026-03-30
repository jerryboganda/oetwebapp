"use client";

import React, {
  forwardRef,
  useId,
  useState,
  type InputHTMLAttributes,
} from "react";
import { IconEye, IconEyeOff } from "@tabler/icons-react";

interface ThemedPasswordInputProps extends Omit<
  InputHTMLAttributes<HTMLInputElement>,
  "type"
> {
  leftIcon?: React.ReactNode | undefined;
  wrapperClassName?: string | undefined;
  toggleClassName?: string | undefined;
}

const ThemedPasswordInput = forwardRef<
  HTMLInputElement,
  ThemedPasswordInputProps
>(
  (
    {
      className = "",
      leftIcon,
      wrapperClassName = "",
      toggleClassName = "",
      style,
      ...props
    },
    ref
  ) => {
    const generatedId = useId();
    const [isVisible, setIsVisible] = useState(false);

    return (
      <div
        className={wrapperClassName}
        style={{ position: "relative", width: "100%" }}
      >
        {leftIcon ? (
          <span
            style={{
              position: "absolute",
              top: "50%",
              left: "14px",
              transform: "translateY(-50%)",
              zIndex: 2,
              display: "inline-flex",
              alignItems: "center",
              justifyContent: "center",
              color: "#1e245c",
            }}
          >
            {leftIcon}
          </span>
        ) : null}

        <input
          {...props}
          id={props.id ?? generatedId}
          ref={ref}
          type={isVisible ? "text" : "password"}
          className={className}
          style={{
            ...style,
            paddingLeft: leftIcon ? "2.8rem" : style?.paddingLeft,
          }}
        />

        <button
          type="button"
          aria-label={isVisible ? "Hide password" : "Show password"}
          aria-pressed={isVisible}
          onClick={() => setIsVisible((current) => !current)}
          className={toggleClassName}
          style={{
            position: "absolute",
            top: "50%",
            right: "8px",
            transform: "translateY(-50%)",
            zIndex: 2,
            width: "42px",
            height: "42px",
            display: "inline-flex",
            alignItems: "center",
            justifyContent: "center",
            border: 0,
            padding: 0,
            background: "transparent",
            color: "#7b61ff",
            boxShadow: "none",
          }}
        >
          {isVisible ? <IconEyeOff size={22} /> : <IconEye size={22} />}
        </button>
      </div>
    );
  }
);

ThemedPasswordInput.displayName = "ThemedPasswordInput";

export default ThemedPasswordInput;
