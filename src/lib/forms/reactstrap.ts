import type { UseFormRegisterReturn } from "react-hook-form";

export function bindReactstrapInput(registration: UseFormRegisterReturn): Omit<
  UseFormRegisterReturn,
  "ref"
> & {
  innerRef: UseFormRegisterReturn["ref"];
} {
  const { ref, ...rest } = registration;
  return {
    ...rest,
    innerRef: ref,
  };
}
