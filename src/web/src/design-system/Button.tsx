import type { ButtonHTMLAttributes, ReactNode } from "react";

type ButtonPrimaryProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  icon?: ReactNode;
};

export function ButtonPrimary({ icon, children, className = "", disabled, ...rest }: ButtonPrimaryProps) {
  return (
    <button
      type="button"
      disabled={disabled}
      className={`inline-flex h-12 items-center justify-center gap-2 whitespace-nowrap rounded-[30px] px-5 text-xs font-medium text-[var(--action-primary-content)] transition-[opacity,transform,box-shadow] duration-150 ${
        disabled
          ? "bg-[#e0e0e0] text-[#737373] opacity-65"
          : "bg-[var(--action-primary)] shadow-[0_6px_16px_rgba(226,29,45,0.25)] hover:-translate-y-px hover:opacity-92 active:scale-[0.98] active:opacity-82"
      } ${className}`}
      {...rest}
    >
      {icon}
      {children}
    </button>
  );
}

/** Chat/Specialist CTA (Figma 292:1492): secondary post-answer action for explicit human handoff.
 * Brand-subtle (tinted fill + brand border) so it stays visible without competing with Send. */
export function SpecialistCta({ icon, children = "Позвать специалиста", className = "", disabled, ...rest }: ButtonPrimaryProps) {
  return (
    <button
      type="button"
      disabled={disabled}
      className={`inline-flex h-12 items-center justify-center gap-2 whitespace-nowrap rounded-[30px] border border-[var(--action-primary)] bg-[#fff0f1] px-5 text-xs font-medium text-[var(--action-primary)] transition-[background-color,transform,box-shadow] duration-150 hover:-translate-y-px hover:bg-[#ffe4e6] hover:shadow-[0_6px_16px_rgba(226,29,45,0.15)] active:scale-[0.98] disabled:opacity-42 ${className}`}
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
      className={`inline-flex h-12 items-center justify-center gap-2 whitespace-nowrap rounded-[30px] border border-[var(--border-default)] bg-[var(--surface-default)] px-5 text-xs font-medium text-[var(--content-primary)] transition-[background-color,transform,border-color] duration-150 hover:-translate-y-px hover:border-[#d5dce3] hover:bg-[var(--surface-subtle)] active:scale-[0.98] disabled:opacity-42 ${className}`}
      {...rest}
    >
      {children}
      {icon}
    </button>
  );
}
