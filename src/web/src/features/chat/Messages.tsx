export function UserMessage({ text }: { text: string }) {
  return (
    <div className="flex items-start rounded-[30px] bg-[var(--surface-default)] p-2.5 self-end">
      <p className="text-sm text-[var(--content-primary)]">{text}</p>
    </div>
  );
}

export function AssistantResponse({ markdown }: { markdown: string }) {
  return (
    <div className="w-full max-w-[512px] text-sm leading-5 text-[var(--content-primary)]">
      {markdown.split("\n").map((line, index) => (
        <p key={index} className={index > 0 ? "mt-0" : undefined}>
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
    <div className="w-full max-w-[512px] rounded-[18px] border border-[var(--border-default)] bg-white p-3.5 text-sm text-[var(--content-primary)]">
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
    <div className="w-full max-w-[512px] rounded-[18px] border border-[var(--border-default)] bg-white p-3.5 text-sm text-[var(--content-primary)]">
      В базе знаний не нашлось подтверждённого ответа на этот вопрос.
    </div>
  );
}

export function ModerationWarningNotice({ count }: { count: number }) {
  return (
    <div className="w-full max-w-[512px] rounded-[18px] border border-[#f5c7cc] bg-[#fff6f7] p-3.5 text-sm text-[var(--content-primary)]">
      Сообщение нарушает правила общения (предупреждение {count}). Повторное нарушение закроет обращение.
    </div>
  );
}

export function ConversationClosedNotice({ reason }: { reason: "moderation" | "user" | "support" }) {
  const text =
    reason === "moderation"
      ? "Обращение закрыто модерацией из-за повторного нарушения правил."
      : reason === "support"
        ? "Обращение завершено поддержкой."
        : "Вы завершили это обращение.";
  return <div className="w-full max-w-[512px] rounded-[18px] bg-[var(--surface-subtle)] p-3.5 text-sm text-[var(--content-secondary)]">{text}</div>;
}

export function TechnicalErrorNotice() {
  return (
    <div className="w-full max-w-[512px] rounded-[18px] border border-[#f5c7cc] bg-[#fff6f7] p-3.5 text-sm text-[var(--content-primary)]">
      Техническая проблема на нашей стороне — это не значит, что ответа нет в базе. Попробуйте ещё раз или обратитесь к специалисту.
    </div>
  );
}

export function TurnStageNotice({ stage }: { stage: string }) {
  return <p className="text-xs text-[var(--content-tertiary)]">{stage}…</p>;
}
