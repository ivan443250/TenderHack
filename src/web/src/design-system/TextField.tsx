import type { InputHTMLAttributes } from "react";

type TextFieldProps = InputHTMLAttributes<HTMLInputElement> & {
  error?: boolean;
};

export function TextField({ error, className = "", disabled, ...rest }: TextFieldProps) {
  return (
    <input
      disabled={disabled}
      className={`h-[50px] w-full rounded-[30px] border bg-[var(--surface-default)] px-[18px] text-xs text-[var(--content-primary)] placeholder:text-[var(--content-tertiary)] outline-none transition-colors ${
        error
          ? "border-[var(--focus-ring)]"
          : "border-[var(--border-default)] focus:border-[1.5px] focus:border-[var(--focus-ring)]"
      } ${disabled ? "bg-[var(--surface-subtle)] opacity-55" : ""} ${className}`}
      {...rest}
    />
  );
}
