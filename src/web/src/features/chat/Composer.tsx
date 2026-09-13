import { forwardRef, useImperativeHandle, useRef, useState, type FormEvent } from "react";

import { SendArrowIcon } from "../../design-system/icons";

type ComposerProps = {
  context: "home" | "conversation";
  onSubmit: (text: string) => void;
  disabled?: boolean;
  placeholder?: string;
  autoFocus?: boolean;
};

export type ComposerHandle = {
  focus: () => void;
  setText: (text: string) => void;
};

/** Chat/Composer (Figma 181:41): Context=Home|Conversation, State=Default|Focus|Filled.
 * Exposes an imperative handle so a parent (E2's "Еще один вопрос к поддержке" chip, a future
 * handoff-prefill flow) can focus or prefill the input without owning its state. */
export const Composer = forwardRef<ComposerHandle, ComposerProps>(function Composer(
  { context, onSubmit, disabled, placeholder = "Введите свой вопрос", autoFocus = true },
  ref
) {
  const [value, setValue] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);

  useImperativeHandle(ref, () => ({
    focus: () => inputRef.current?.focus(),
    setText: (text: string) => setValue(text)
  }));

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    const text = value.trim();
    if (!text || disabled) return;
    onSubmit(text);
    setValue("");
  }

  const isHome = context === "home";
  const canSend = !disabled && value.trim().length > 0;

  return (
    <form
      onSubmit={handleSubmit}
      className={`flex h-14 items-center gap-2.5 rounded-[30px] border border-transparent bg-[var(--surface-default)] py-1 pl-6 pr-2.5 transition-[border-color,box-shadow,transform] duration-200 focus-within:border-[var(--focus-ring)] ${
        isHome
          ? "w-[600px] max-w-[90vw] drop-shadow-[0_0_25px_#e21d2d] focus-within:scale-[1.01] focus-within:drop-shadow-[0_0_35px_#e21d2d]"
          : "w-full max-w-[760px] shadow-sm focus-within:shadow-md"
      }`}
    >
      <input
        ref={inputRef}
        value={value}
        onChange={(event) => setValue(event.target.value)}
        placeholder={placeholder}
        disabled={disabled}
        autoFocus={autoFocus}
        className="min-w-0 flex-1 bg-transparent text-sm text-[var(--content-primary)] outline-none placeholder:text-[var(--content-tertiary)] disabled:opacity-60"
      />
      <button
        type="submit"
        disabled={!canSend}
        aria-label="Отправить"
        className={`flex size-11 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-[#e21d2d] to-[#ff2fb0] text-white transition-[transform,opacity,box-shadow] duration-200 ${
          canSend ? "shadow-[0_6px_16px_rgba(226,29,45,0.35)] hover:scale-110 active:scale-95" : "scale-95 opacity-40"
        }`}
      >
        <SendArrowIcon className="size-5" />
      </button>
    </form>
  );
});
