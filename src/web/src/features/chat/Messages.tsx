import moderationAlert from "../../assets/moderation-alert.svg";
import moderationLock from "../../assets/moderation-lock.svg";
import type { ReactNode } from "react";

const THINK_BLOCK_RE = /<think\b[^>]*>[\s\S]*?<\/think>/gi;
const OPEN_THINK_RE = /<think\b[\s\S]*$/i;
const FRAGMENT_REFERENCE_RE = /\b(?:frag(?:ment)?)[_-][a-z0-9-]+\b/gi;
const FRAGMENT_FIELD_RE = /["']?\b(?:source[_ ]?)?fragment[_ ]ids?["']?\s*[:=]\s*["']?[^\s,;}\]"']+/gi;
const INTERNAL_FIELD_RE = /["']?\b(?:claim_id|fragment_id|fragment_ids|evidence_fragment_ids|draft_markdown|response_format)\b["']?\s*[:=]\s*["']?[^\s,;}\]"']+/gi;
const INTERNAL_HEADER_RE = /^\s*(?:json\s+schema|черновик\s+ответа|что\s+установлено\s+в\s+источниках)\s*:?.*$/gim;

function cleanVisibleText(value: string): string {
  return value
    .replace(THINK_BLOCK_RE, "")
    .replace(OPEN_THINK_RE, "")
    .replace(FRAGMENT_FIELD_RE, "")
    .replace(INTERNAL_FIELD_RE, "")
    .replace(INTERNAL_HEADER_RE, "")
    .replace(FRAGMENT_REFERENCE_RE, "")
    .replace(/[ \t]{2,}/g, " ")
    .trim();
}

function renderInline(value: string, keyPrefix: string): ReactNode[] {
  const nodes: ReactNode[] = [];
  const inlinePattern = /(\*\*([^*]+)\*\*|__([^_]+)__|`([^`]+)`|\[([^\]]+)\]\(([^)\s]+)\))/g;
  let cursor = 0;
  let match: RegExpExecArray | null;
  let index = 0;
  while ((match = inlinePattern.exec(value)) !== null) {
    const plain = value.slice(cursor, match.index).replace(/\*\*|__/g, "");
    if (plain) nodes.push(plain);
    if (match[2] ?? match[3]) {
      nodes.push(<strong key={`${keyPrefix}-strong-${index}`}>{cleanVisibleText(match[2] ?? match[3] ?? "")}</strong>);
    } else if (match[4]) {
      nodes.push(<code key={`${keyPrefix}-code-${index}`} className="rounded bg-[var(--surface-subtle)] px-1 py-0.5 text-[0.9em]">{cleanVisibleText(match[4])}</code>);
    } else if (match[5]) {
      const label = cleanVisibleText(match[5]);
      if (/^https?:\/\//i.test(match[6])) {
        nodes.push(<a key={`${keyPrefix}-link-${index}`} href={match[6]} target="_blank" rel="noreferrer" className="underline">{label}</a>);
      } else {
        nodes.push(label);
      }
    }
    cursor = match.index + match[0].length;
    index += 1;
  }
  const tail = value.slice(cursor).replace(/\*\*|__/g, "");
  if (tail) nodes.push(cleanVisibleText(tail));
  return nodes;
}

function renderMarkdown(markdown: string): ReactNode[] {
  const lines = cleanVisibleText(markdown).split(/\r?\n/);
  const blocks: ReactNode[] = [];
  let index = 0;
  while (index < lines.length) {
    const line = lines[index];
    if (!line.trim()) {
      index += 1;
      continue;
    }
    if (/^\s*```/.test(line)) {
      const codeLines: string[] = [];
      index += 1;
      while (index < lines.length && !/^\s*```/.test(lines[index])) {
        codeLines.push(lines[index]);
        index += 1;
      }
      if (index < lines.length) index += 1;
      blocks.push(<pre key={`code-block-${index}`} className="overflow-x-auto rounded-xl bg-[var(--surface-subtle)] p-3 text-xs"><code>{codeLines.join("\n")}</code></pre>);
      continue;
    }
    const heading = line.match(/^\s{0,3}(#{1,3})\s+(.+?)\s*#*\s*$/);
    if (heading) {
      const Tag = heading[1].length === 1 ? "h2" : "h3";
      blocks.push(<Tag key={`heading-${index}`} className="font-semibold leading-snug">{renderInline(heading[2], `heading-${index}`)}</Tag>);
      index += 1;
      continue;
    }
    const unordered = line.match(/^\s*[-*]\s+(.+)$/);
    const ordered = line.match(/^\s*\d+[.)]\s+(.+)$/);
    if (unordered || ordered) {
      const orderedList = Boolean(ordered);
      const items: string[] = [];
      while (index < lines.length) {
        const current = lines[index].match(orderedList ? /^\s*\d+[.)]\s+(.+)$/ : /^\s*[-*]\s+(.+)$/);
        if (!current) break;
        items.push(current[1]);
        index += 1;
      }
      const List = orderedList ? "ol" : "ul";
      blocks.push(<List key={`list-${index}`} className={`${orderedList ? "list-decimal" : "list-disc"} ml-5 space-y-1`}>{items.map((item, itemIndex) => <li key={`list-${index}-${itemIndex}`}>{renderInline(item, `list-${index}-${itemIndex}`)}</li>)}</List>);
      continue;
    }
    const paragraph: string[] = [line];
    index += 1;
    while (index < lines.length && lines[index].trim() && !/^\s*```/.test(lines[index]) && !/^\s{0,3}#{1,3}\s+/.test(lines[index]) && !/^\s*(?:[-*]\s+|\d+[.)]\s+)/.test(lines[index])) {
      paragraph.push(lines[index]);
      index += 1;
    }
    blocks.push(<p key={`paragraph-${index}`}>{paragraph.map((part, partIndex) => <span key={`paragraph-${index}-${partIndex}`}>{partIndex > 0 && <br />}{renderInline(part, `paragraph-${index}-${partIndex}`)}</span>)}</p>);
  }
  return blocks;
}

export function UserMessage({ text }: { text: string }) {
  return (
    <div className="flex max-w-[80%] animate-fade-up items-start self-end rounded-[30px] bg-[var(--surface-default)] px-4 py-3 shadow-sm">
      <p className="text-sm text-[var(--content-primary)]">{text}</p>
    </div>
  );
}

export function AssistantResponse({ markdown }: { markdown: string }) {
  return (
    <div className="flex w-full max-w-[560px] animate-fade-up flex-col gap-2 text-sm leading-relaxed text-[var(--content-primary)]">
      {renderMarkdown(markdown)}
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

export function ClarificationNotice({ missingConditions, questions }: { missingConditions: string[]; questions?: string[] }) {
  // `questions` is additive next to `missing_conditions`. Older responses may contain only slot
  // names, so map both fields at the presentation boundary and never expose an internal slot name.
  const questionCopy: Record<string, string> = {
    role: "Вы работаете на Портале как поставщик или как заказчик?",
    provider: "Какой оператор ЭДО вы используете?",
    process: "Какой процесс или раздел Портала вы сейчас проходите?",
    document_type: "Какой тип документа вы оформляете?",
    status: "Какой статус сейчас отображается?",
    duration: "Как долго статус остаётся без изменений?",
    already_tried: "Что именно вы уже попробовали сделать?",
    error_code: "Какой код ошибки или полный текст сообщения вы видите?",
    category_id: "Какой идентификатор категории указан в файле?",
    model: "Какую модель или формат вы используете?",
    source_state: "Какое состояние документа указано в Портале?"
  };
  const items = (questions && questions.length > 0 ? questions : missingConditions)
    .map((item) => {
      const value = item.trim();
      if (questionCopy[value]) return questionCopy[value];
      if (/^[A-Za-z][A-Za-z0-9_]*$/.test(value)) return "Уточните детали ситуации, чтобы подобрать точную инструкцию.";
      return value;
    })
    .filter((item, index, all) => item.length > 0 && all.indexOf(item) === index);
  return (
    <div className="w-full max-w-[560px] animate-fade-up rounded-[18px] border border-[var(--border-default)] bg-white p-4 text-sm text-[var(--content-primary)]">
      <p className="font-medium">Нужны уточнения, чтобы ответить точно:</p>
      <ul className="mt-1.5 list-inside list-disc text-[var(--content-secondary)]">
        {items.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  );
}

export function NoConfirmedAnswerNotice({ reason }: { reason?: string }) {
  // product-spec.md §21: a direct request for a human is not "no answer found in the knowledge
  // base" — it never went through the FAQ loop in the first place.
  const text =
    reason === "EXPLICIT_HUMAN_REQUEST"
      ? "Соединяю вас со специалистом поддержки."
      : "В базе знаний не нашлось подтверждённого ответа на этот вопрос.";
  return (
    <div className="w-full max-w-[560px] animate-fade-up rounded-[18px] border border-[var(--border-default)] bg-white p-4 text-sm text-[var(--content-primary)]">
      {text}
    </div>
  );
}

export function OutOfScopeNotice({ message }: { message: string }) {
  return (
    <div className="w-full max-w-[560px] animate-fade-up rounded-[18px] border border-[var(--border-default)] bg-white p-4 text-sm text-[var(--content-primary)]">
      {message}
    </div>
  );
}

/** Chat/Moderation Warning (Figma 321:1486): warning-first policy, product-spec.md §14. The
 * message/count come from the server event (quality.md §12 "warning text and count come from the
 * server event"), never hardcoded copy — a rule change on the backend must not require a UI release. */
export function ModerationWarningNotice({ message }: { message: string }) {
  return (
    <div className="flex w-full max-w-[560px] animate-pop-in items-center gap-3 rounded-[18px] border border-[#f0c9cc] bg-[#fff7f7] p-4">
      <img src={moderationAlert} alt="" className="size-7 shrink-0" />
      <div className="flex min-w-0 flex-col gap-1">
        <p className="text-sm font-semibold leading-snug text-[var(--content-primary)]">Пожалуйста, без нецензурной лексики</p>
        <p className="text-xs leading-snug text-[var(--content-secondary)]">{message}</p>
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
