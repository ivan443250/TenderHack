import type { ButtonHTMLAttributes, ReactNode } from "react";

type IconButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  icon: ReactNode;
  style?: "neutral" | "brand";
  label: string;
};

export function IconButton({ icon, style = "neutral", label, className = "", disabled, ...rest }: IconButtonProps) {
  const base = "flex size-9 shrink-0 items-center justify-center rounded-full transition-colors";
  const styles =
    style === "brand"
      ? "bg-[var(--action-primary)] text-white hover:opacity-92 active:opacity-82 disabled:opacity-42"
      : "border border-[var(--border-default)] bg-[var(--surface-default)] text-[var(--content-primary)] hover:bg-[var(--surface-subtle)] disabled:opacity-42";

  return (
    <button type="button" aria-label={label} title={label} disabled={disabled} className={`${base} ${styles} ${className}`} {...rest}>
      {icon}
    </button>
  );
}
