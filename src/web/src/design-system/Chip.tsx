import type { ButtonHTMLAttributes } from "react";

type PillChoiceProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  selected?: boolean;
};

/** Selectable pill used by resolution/feedback controls (Chat/Resolution & Feedback in Figma). */
export function PillChoice({ selected, className = "", ...rest }: PillChoiceProps) {
  return (
    <button
      type="button"
      aria-pressed={selected}
      className={`flex h-[34px] items-center whitespace-nowrap rounded-full border px-3 text-xs font-medium transition-colors ${
        selected
          ? "border-[var(--action-primary)] bg-[#fff0f1] text-[#c70d17]"
          : "border-[var(--border-default)] bg-[var(--surface-subtle)] text-[var(--content-primary)]"
      } ${className}`}
      {...rest}
    />
  );
}

type SuggestionChipProps = ButtonHTMLAttributes<HTMLButtonElement>;

/** Home-only glass suggestion chip (Chat/Suggestion Chip, Figma node 186:117 family). */
export function SuggestionChip({ className = "", ...rest }: SuggestionChipProps) {
  return (
    <button
      type="button"
      className={`flex h-[45px] items-center whitespace-nowrap rounded-full border border-white/55 bg-[var(--p-neutral-glass-base)]/70 px-[10px] text-sm font-bold text-white backdrop-blur-sm transition-opacity hover:opacity-90 ${className}`}
      {...rest}
    />
  );
}
