import type { ButtonHTMLAttributes, ReactNode } from "react";

type ButtonPrimaryProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  icon?: ReactNode;
};

export function ButtonPrimary({ icon, children, className = "", disabled, ...rest }: ButtonPrimaryProps) {
  return (
    <button
      type="button"
      disabled={disabled}
      className={`inline-flex h-[45px] items-center justify-center gap-2 whitespace-nowrap rounded-[30px] px-[18px] text-xs font-medium text-[var(--action-primary-content)] transition-opacity ${
        disabled
          ? "bg-[#e0e0e0] text-[#737373] opacity-65"
          : "bg-[var(--action-primary)] hover:opacity-92 active:opacity-82"
      } ${className}`}
      {...rest}
    >
      {icon}
      {children}
    </button>
  );
}

export function ButtonSecondary({ icon, children, className = "", disabled, ...rest }: ButtonPrimaryProps) {
  return (
    <button
      type="button"
      disabled={disabled}
      className={`inline-flex h-[45px] items-center justify-center gap-2 whitespace-nowrap rounded-[30px] border border-[var(--border-default)] bg-[var(--surface-default)] px-[18px] text-xs font-medium text-[var(--content-primary)] transition-colors hover:bg-[var(--surface-subtle)] disabled:opacity-42 ${className}`}
      {...rest}
    >
      {children}
      {icon}
    </button>
  );
}
