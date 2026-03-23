"use client";

import React, { forwardRef, useId, useState } from "react";
import { Input, type InputProps } from "reactstrap";
import { IconEye, IconEyeOff } from "@tabler/icons-react";

interface ThemedPasswordInputProps extends Omit<InputProps, "type"> {
  leftIcon?: React.ReactNode;
  wrapperClassName?: string;
  toggleClassName?: string;
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
    const hasLeftIcon = Boolean(leftIcon);

    return (
      <div className={`position-relative ${wrapperClassName}`.trim()}>
        {leftIcon ? (
          <span
            className="position-absolute top-50 start-0 translate-middle-y d-inline-flex align-items-center justify-content-center text-primary-dark"
            style={{ left: "14px", zIndex: 2 }}
          >
            {leftIcon}
          </span>
        ) : null}

        <Input
          {...props}
          id={props.id ?? generatedId}
          innerRef={ref}
          type={isVisible ? "text" : "password"}
          className={`${className} ${hasLeftIcon ? "pa-s-34" : ""} pe-5`.trim()}
          style={style}
        />

        <button
          type="button"
          aria-label={isVisible ? "Hide password" : "Show password"}
          aria-pressed={isVisible}
          onClick={() => setIsVisible((current) => !current)}
          className={`p-0 border-0 position-absolute top-50 end-0 translate-middle-y d-inline-flex align-items-center justify-content-center ${toggleClassName}`.trim()}
          style={{
            color: "#7b61ff",
            background: "transparent",
            width: "42px",
            height: "42px",
            right: "8px",
            zIndex: 2,
            boxShadow: "none",
            outline: "none",
            appearance: "none",
          }}
          onMouseEnter={(event) => {
            event.currentTarget.style.backgroundColor = "transparent";
            event.currentTarget.style.boxShadow = "none";
          }}
          onMouseLeave={(event) => {
            event.currentTarget.style.backgroundColor = "transparent";
            event.currentTarget.style.boxShadow = "none";
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
