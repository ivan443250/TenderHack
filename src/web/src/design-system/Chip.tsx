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
      className={`flex h-10 items-center whitespace-nowrap rounded-full border px-4 text-xs font-medium transition-[background-color,border-color,transform,box-shadow] duration-150 hover:-translate-y-px active:scale-95 ${
        selected
          ? "border-[var(--action-primary)] bg-[#fff0f1] text-[#c70d17]"
          : "border-[var(--border-default)] bg-[var(--surface-subtle)] text-[var(--content-primary)] hover:border-[#d5dce3] hover:bg-white"
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
      className={`flex h-12 items-center whitespace-nowrap rounded-full border border-white/55 bg-[var(--p-neutral-glass-base)]/70 px-4 text-sm font-bold text-white shadow-[0_4px_14px_rgba(0,0,0,0.08)] backdrop-blur-sm transition-[transform,background-color,box-shadow] duration-200 hover:-translate-y-1 hover:scale-[1.04] hover:bg-[var(--p-neutral-glass-base)]/85 hover:shadow-[0_10px_24px_rgba(0,0,0,0.14)] active:scale-[0.98] disabled:opacity-60 ${className}`}
      {...rest}
    />
  );
}
