import { useState, type FormEvent } from "react";

import { SendArrowIcon } from "../../design-system/icons";

type ComposerProps = {
  context: "home" | "conversation";
  onSubmit: (text: string) => void;
  disabled?: boolean;
  placeholder?: string;
};

/** Chat/Composer (Figma 181:41): Context=Home|Conversation, State=Default|Focus|Filled. */
export function Composer({ context, onSubmit, disabled, placeholder = "Введите свой вопрос" }: ComposerProps) {
  const [value, setValue] = useState("");

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    const text = value.trim();
    if (!text || disabled) return;
    onSubmit(text);
    setValue("");
  }

  const isHome = context === "home";

  return (
    <form
      onSubmit={handleSubmit}
      className={`flex h-[50px] items-center gap-2.5 rounded-[30px] bg-[var(--surface-default)] py-1 pl-[26px] pr-2.5 ${
        isHome ? "w-[550px] drop-shadow-[0_0_25px_#e21d2d] focus-within:border focus-within:border-[var(--focus-ring)]" : "w-full max-w-[705px] focus-within:border focus-within:border-[var(--focus-ring)]"
      }`}
    >
      <input
        value={value}
        onChange={(event) => setValue(event.target.value)}
        placeholder={placeholder}
        disabled={disabled}
        className="min-w-0 flex-1 bg-transparent text-xs text-[var(--content-primary)] placeholder:text-[var(--content-tertiary)] outline-none"
      />
      <button
        type="submit"
        disabled={disabled || !value.trim()}
        aria-label="Отправить"
        className="flex size-9 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-[#e21d2d] to-[#ff2fb0] disabled:opacity-40"
      >
        <SendArrowIcon className="size-4" />
      </button>
    </form>
  );
}
