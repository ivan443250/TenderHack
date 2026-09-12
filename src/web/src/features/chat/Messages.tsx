import moderationAlert from "../../assets/moderation-alert.svg";
import moderationLock from "../../assets/moderation-lock.svg";

export function UserMessage({ text }: { text: string }) {
  return (
    <div className="flex max-w-[80%] animate-fade-up items-start self-end rounded-[30px] bg-[var(--surface-default)] px-4 py-3 shadow-sm">
      <p className="text-sm text-[var(--content-primary)]">{text}</p>
    </div>
  );
}

export function AssistantResponse({ markdown }: { markdown: string }) {
  return (
    <div className="w-full max-w-[560px] animate-fade-up text-sm leading-relaxed text-[var(--content-primary)]">
      {markdown.split("\n").map((line, index) => (
        <p key={index} className={index > 0 ? "mt-1" : undefined}>
          {line}
        </p>
      ))}
    </div>
  );
}

export function HandoffCta({ label = "Не получили ответ? Передать запрос в специалисту", onClick }: { label?: string; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="flex items-start rounded-[30px] bg-[var(--surface-default)] p-2.5 text-left text-sm text-[var(--content-primary)] hover:bg-[var(--surface-subtle)]"
    >
      {label}
    </button>
  );
}

export function ClarificationNotice({ missingConditions }: { missingConditions: string[] }) {
  return (
    <div className="w-full max-w-[560px] animate-fade-up rounded-[18px] border border-[var(--border-default)] bg-white p-4 text-sm text-[var(--content-primary)]">
      <p className="font-medium">Нужны уточнения, чтобы ответить точно:</p>
      <ul className="mt-1.5 list-inside list-disc text-[var(--content-secondary)]">
        {missingConditions.map((condition) => (
          <li key={condition}>{condition}</li>
        ))}
      </ul>
    </div>
  );
}

export function NoConfirmedAnswerNotice() {
  return (
    <div className="w-full max-w-[560px] animate-fade-up rounded-[18px] border border-[var(--border-default)] bg-white p-4 text-sm text-[var(--content-primary)]">
      В базе знаний не нашлось подтверждённого ответа на этот вопрос.
    </div>
  );
}

/** Chat/Moderation Warning (Figma 321:1486): warning-first policy, product-spec.md §14. */
export function ModerationWarningNotice() {
  return (
    <div className="flex w-full max-w-[560px] animate-pop-in items-center gap-3 rounded-[18px] border border-[#f0c9cc] bg-[#fff7f7] p-4">
      <img src={moderationAlert} alt="" className="size-7 shrink-0" />
      <div className="flex min-w-0 flex-col gap-1">
        <p className="text-sm font-semibold leading-snug text-[var(--content-primary)]">Пожалуйста, без нецензурной лексики</p>
        <p className="text-xs leading-snug text-[var(--content-secondary)]">
          Я продолжу диалог, но при повторном нарушении чат будет заблокирован.
        </p>
      </div>
    </div>
  );
}

/** Chat/Moderation Blocked Notice (Figma 321:1494): sits where the composer was once the chat is
 * closed by moderation — the only next step is a new chat. */
export function ModerationBlockedNotice({ className = "" }: { className?: string }) {
  return (
    <div className={`flex min-h-[62px] w-full max-w-[705px] animate-pop-in items-center gap-2.5 rounded-[30px] border border-[#efc5c8] bg-[#fff6f6] px-[18px] py-2 ${className}`}>
      <img src={moderationLock} alt="" className="size-[22px] shrink-0" />
      <div className="flex min-w-0 flex-col gap-0.5">
        <p className="text-sm font-semibold leading-snug text-[var(--action-primary)]">Чат заблокирован</p>
        <p className="text-xs leading-snug text-[var(--content-secondary)]">
          Повторное нарушение правил общения. Создайте новый чат, чтобы продолжить.
        </p>
      </div>
    </div>
  );
}

export function ConversationClosedNotice({ reason }: { reason: "user" | "support" }) {
  const text = reason === "support" ? "Обращение завершено поддержкой." : "Вы завершили это обращение.";
  return <div className="w-full max-w-[560px] animate-fade-up rounded-[18px] bg-[var(--surface-subtle)] p-4 text-sm text-[var(--content-secondary)]">{text}</div>;
}

export function TechnicalErrorNotice() {
  return (
    <div className="w-full max-w-[560px] animate-pop-in rounded-[18px] border border-[#f5c7cc] bg-[#fff6f7] p-4 text-sm text-[var(--content-primary)]">
      Техническая проблема на нашей стороне — это не значит, что ответа нет в базе. Попробуйте ещё раз или обратитесь к специалисту.
    </div>
  );
}

export function TurnStageNotice({ stage }: { stage: string }) {
  return (
    <p className="loading-dots animate-fade-in text-xs text-[var(--content-tertiary)]">
      {stage}
    </p>
  );
}
