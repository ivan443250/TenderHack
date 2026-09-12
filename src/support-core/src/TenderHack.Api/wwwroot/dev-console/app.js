'use strict';

/* =========================================================================
 * TenderHack — dev chat console. Same-origin static page served by TenderHack.Api.
 * Product flow (web-api-v0): cases → messages → timeline events (snapshot + SSE)
 * → handoff widget → completion → feedback → notifications. Everything the
 * server says is shown as-is; nothing here invents lifecycle state.
 * ========================================================================= */

const $ = (id) => document.getElementById(id);

const state = {
  baseUrl: localStorage.getItem('th_base_url') || '',
  devDetails: localStorage.getItem('th_dev_details') !== '0',
  autoSse: localStorage.getItem('th_auto_sse') !== '0',
  devCollapsed: localStorage.getItem('th_dev_collapsed') === '1',
  view: 'active',
  cases: [],
  activeCaseId: localStorage.getItem('th_case_id') || '',
  snapshot: null,
  timeline: new Map(), // event_id -> event (dedupe for snapshot + SSE)
  pendingUser: null,
  caseStream: null,
  notifStream: null,
  notifications: new Map(),
  log: [],
  sending: false,
  refreshTimer: null,
};

/* ---------- utils ---------- */

function escapeHtml(s) {
  return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function fmtTime(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  return isNaN(d) ? String(iso) : d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

function fmtDate(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  return isNaN(d) ? String(iso) : d.toLocaleString();
}

function shortId(id) { return id ? String(id).slice(0, 8) : ''; }

function genUuid() {
  if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
  });
}

/** Minimal, escaping-first markdown: paragraphs, **bold**, `code`, -/1. lists, line breaks. */
function renderMarkdown(md) {
  const blocks = String(md ?? '').replace(/\r\n/g, '\n').split(/\n{2,}/);
  return blocks.map((block) => {
    const lines = block.split('\n');
    const isUl = lines.every((l) => /^\s*[-*•]\s+/.test(l));
    const isOl = lines.every((l) => /^\s*\d+[.)]\s+/.test(l));
    const inline = (t) => escapeHtml(t)
      .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
      .replace(/`([^`]+)`/g, '<code>$1</code>');
    if (isUl) return '<ul>' + lines.map((l) => `<li>${inline(l.replace(/^\s*[-*•]\s+/, ''))}</li>`).join('') + '</ul>';
    if (isOl) return '<ol>' + lines.map((l) => `<li>${inline(l.replace(/^\s*\d+[.)]\s+/, ''))}</li>`).join('') + '</ol>';
    return `<p>${lines.map(inline).join('<br>')}</p>`;
  }).join('');
}

const LABELS = {
  conversation: { ACTIVE: ['Открыто', 'ok'], CLOSED_USER: ['Завершено вами', ''], CLOSED_SUPPORT: ['Завершено поддержкой', ''], CLOSED_MODERATION: ['Закрыто модерацией', 'err'] },
  resolution: { UNKNOWN: ['Результат неизвестен', ''], RESOLVED: ['Решено', 'ok'], UNRESOLVED: ['Не решено', 'warn'] },
  decision: { ANSWER: 'ответ', CLARIFY: 'уточнение', HANDOFF_OFFER: 'предложена передача', ANSWER_AND_HANDOFF: 'ответ + передача', MODERATION_WARNING: 'предупреждение', MODERATION_CLOSE: 'закрыто модерацией', TECHNICAL_ERROR: 'техническая ошибка' },
  handoff: { NOT_REQUESTED: ['Подготовлено, не отправлено', ''], PENDING: ['Отправляем в поддержку…', 'accent'], ACCEPTED: ['Принято поддержкой', 'ok'], SIMULATED_ACCEPTED: ['Принято (демо)', 'warn'], FAILED: ['Не удалось передать', 'err'] },
  terminal: { RESOLVED: 'решено', CLOSED_UNRESOLVED: 'закрыто без решения', CANCELLED: 'отменено', Resolved: 'решено', ClosedUnresolved: 'закрыто без решения', Cancelled: 'отменено' },
  failure: { TIMEOUT: 'сервис знаний не ответил вовремя', UNAVAILABLE: 'сервис знаний недоступен', INVALID_RESPONSE: 'сервис знаний вернул некорректный ответ', MODEL_ERROR: 'ошибка модели', Timeout: 'сервис знаний не ответил вовремя', Unavailable: 'сервис знаний недоступен', InvalidResponse: 'сервис знаний вернул некорректный ответ', ModelError: 'ошибка модели', STALE_TURN: 'обработка зависла и была прервана', INTERNAL: 'внутренняя ошибка сервера' },
};

/* ---------- toasts ---------- */

function toast(kind, title, detail, { ttl } = {}) {
  const el = document.createElement('div');
  el.className = `toast ${kind}`;
  el.innerHTML = `<div class="tc"><div class="tt">${escapeHtml(title)}</div>${detail ? `<div class="td">${escapeHtml(detail)}</div>` : ''}</div><button class="tx" title="Закрыть">✕</button>`;
  el.querySelector('.tx').addEventListener('click', () => el.remove());
  $('toasts').appendChild(el);
  const life = ttl ?? (kind === 'err' ? 12000 : 3500);
  if (life > 0) setTimeout(() => el.remove(), life);
}

/* ---------- HTTP layer + request log ---------- */

function describeError(entry) {
  if (entry.networkError) {
    const other = state.baseUrl && !state.baseUrl.startsWith(window.location.origin);
    return `Сетевая ошибка: ${entry.networkError}. API недоступен по ${entry.url}.` +
      (other ? ' Base URL — другой origin; без CORS браузер блокирует запрос.' : '');
  }
  if (entry.parseError) return `Ответ не JSON (${entry.parseError}).`;
  const d = entry.data;
  if (d && typeof d === 'object' && !Array.isArray(d)) {
    const parts = [];
    if (d.code) parts.push(d.code);
    if (d.title || d.message || d.detail) parts.push(d.title || d.message || d.detail);
    if (d.errors && typeof d.errors === 'object') {
      parts.push(Object.entries(d.errors).map(([f, m]) => `${f}: ${(Array.isArray(m) ? m : [m]).join(', ')}`).join('; '));
    }
    if (parts.length) return parts.join(' — ');
  }
  return entry.ok ? '' : `HTTP ${entry.status} без описания.`;
}

async function api(method, path, { query, body, idempotencyKey, silent } = {}) {
  let url = (state.baseUrl || '') + path;
  if (query) {
    const p = new URLSearchParams();
    for (const [k, v] of Object.entries(query)) if (v !== undefined && v !== null && v !== '') p.set(k, v);
    const qs = p.toString();
    if (qs) url += (url.includes('?') ? '&' : '?') + qs;
  }
  const headers = {};
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (idempotencyKey) headers['Idempotency-Key'] = idempotencyKey;

  const entry = { ts: new Date(), method, path: url.replace(state.baseUrl || '', ''), url, reqBody: body, reqHeaders: headers };
  const t0 = performance.now();
  try {
    const res = await fetch(url, { method, headers, body: body !== undefined ? JSON.stringify(body) : undefined, credentials: 'same-origin' });
    entry.status = res.status; entry.ok = res.ok;
    const text = await res.text();
    if (text) { try { entry.data = JSON.parse(text); } catch (e) { entry.data = text; entry.parseError = e.message; } } else entry.data = null;
  } catch (err) {
    entry.status = 0; entry.ok = false; entry.networkError = err.message;
  }
  entry.durationMs = Math.round(performance.now() - t0);
  entry.human = entry.ok ? '' : describeError(entry);
  pushLog(entry);
  if (!entry.ok && !silent) toast('err', `${method} ${entry.path} → ${entry.status || 'сеть'}`, entry.human);
  return entry;
}

function pushLog(entry) {
  state.log.unshift(entry);
  if (state.log.length > 300) state.log.pop();
  renderLog();
}

function renderLog() {
  const list = $('log-list');
  const errorsOnly = $('log-errors-only').checked;
  $('log-count').textContent = state.log.length;
  list.innerHTML = '';
  for (const e of state.log) {
    if (errorsOnly && e.ok) continue;
    const det = document.createElement('details');
    det.className = `log-entry ${e.ok ? '' : 'err'}`;
    const stCls = e.ok ? 'ok' : e.status >= 500 || e.status === 0 ? 'err' : 'warn';
    det.innerHTML =
      `<summary><span class="st ${stCls}">${e.status || 'ERR'}</span><span class="m">${e.method}</span><span class="p" title="${escapeHtml(e.path)}">${escapeHtml(e.path)}</span><span class="d">${e.durationMs}ms · ${fmtTime(e.ts)}</span></summary>` +
      (e.human ? `<div class="human">${escapeHtml(e.human)}</div>` : '') +
      (e.reqBody !== undefined ? `<pre>→ ${escapeHtml(JSON.stringify(e.reqBody, null, 2))}${e.reqHeaders['Idempotency-Key'] ? `\n→ Idempotency-Key: ${escapeHtml(e.reqHeaders['Idempotency-Key'])}` : ''}</pre>` : '') +
      `<pre>← ${escapeHtml(typeof e.data === 'string' ? e.data : JSON.stringify(e.data, null, 2))}</pre>`;
    list.appendChild(det);
  }
}

/* ---------- health / session ---------- */

async function checkHealth() {
  const e = await api('GET', '/health/ready', { silent: true });
  const dot = $('health-dot');
  dot.className = 'brand-dot ' + (e.ok ? 'ok' : 'err');
  dot.title = e.ok ? `health/ready: ${e.data?.dependencies ?? 'ok'}` : `health/ready: ${e.human}`;
}

async function ensureSession() {
  const e = await api('POST', '/api/v0/session');
  $('session-label').textContent = e.ok ? 'сессия: ok (cookie owner_id)' : 'сессия: ошибка';
  return e.ok;
}

/* ---------- cases list ---------- */

async function loadCases() {
  const e = await api('GET', '/api/v0/cases', { query: { status: state.view === 'archived' ? 'archived' : undefined } });
  if (!e.ok) { $('cases-list').innerHTML = `<div class="empty muted small">Не удалось загрузить: ${escapeHtml(e.human)}</div>`; return; }
  state.cases = Array.isArray(e.data) ? e.data : [];
  renderCases();
}

function renderCases() {
  const list = $('cases-list');
  list.innerHTML = '';
  if (!state.cases.length) {
    list.innerHTML = `<div class="empty muted small">${state.view === 'archived' ? 'Архив пуст' : 'Нет открытых обращений — создайте новое'}</div>`;
    return;
  }
  for (const c of state.cases) {
    const el = document.createElement('div');
    el.className = 'case-item' + (c.case_id === state.activeCaseId ? ' active' : '');
    const [convLabel, convCls] = LABELS.conversation[c.conversation_status] || [c.conversation_status, ''];
    const [resLabel, resCls] = LABELS.resolution[c.resolution_status] || [c.resolution_status, ''];
    el.innerHTML =
      `<div class="top"><span class="title">Обращение ${shortId(c.case_id)}</span><span class="muted small">${fmtTime(c.last_activity_at)}</span></div>` +
      `<div class="bottom"><span class="pill ${convCls}">${convLabel}</span>${c.conversation_status !== 'ACTIVE' ? `<span class="pill ${resCls}">${resLabel}</span>` : ''}</div>`;
    el.addEventListener('click', () => openCase(c.case_id));
    list.appendChild(el);
  }
}

async function createCase() {
  const e = await api('POST', '/api/v0/cases', { idempotencyKey: genUuid() });
  if (!e.ok) return;
  toast('ok', 'Обращение создано', shortId(e.data.case_id));
  state.view = 'active';
  document.querySelectorAll('.seg-btn').forEach((b) => b.classList.toggle('active', b.dataset.view === 'active'));
  await loadCases();
  await openCase(e.data.case_id);
  $('composer-text').focus();
}

/* ---------- active case: snapshot ---------- */

async function openCase(caseId) {
  if (state.activeCaseId !== caseId) closeCaseStream();
  state.activeCaseId = caseId;
  localStorage.setItem('th_case_id', caseId);
  state.timeline.clear();
  state.pendingUser = null;
  renderCases();
  $('chat-empty').hidden = true;
  $('chat').hidden = false;
  $('composer-error').hidden = true;
  const ok = await refreshSnapshot();
  if (ok && state.autoSse) openCaseStream();
}

async function refreshSnapshot() {
  if (!state.activeCaseId) return false;
  const e = await api('GET', `/api/v0/cases/${encodeURIComponent(state.activeCaseId)}`);
  if (!e.ok) {
    if (e.status === 404) { toast('warn', 'Обращение не найдено', 'Возможно, оно принадлежит другой сессии (другой cookie).'); state.activeCaseId = ''; localStorage.removeItem('th_case_id'); $('chat').hidden = true; $('chat-empty').hidden = false; }
    return false;
  }
  state.snapshot = e.data;
  for (const item of e.data.timeline || []) state.timeline.set(String(item.item_id), { event_id: String(item.item_id), type: item.type, occurred_at: item.occurred_at, turn_id: item.turn_id, payload: item.payload });
  $('snapshot-json').textContent = JSON.stringify(e.data, null, 2);
  renderHeader();
  renderHandoffWidget();
  renderFeedbackWidget();
  renderTimeline();
  renderComposerState();
  return true;
}

function scheduleRefresh() {
  clearTimeout(state.refreshTimer);
  state.refreshTimer = setTimeout(() => { refreshSnapshot(); loadCases(); }, 250);
}

function renderHeader() {
  const s = state.snapshot;
  $('case-title').textContent = `Обращение ${shortId(s.case_id)}`;
  $('case-id-label').textContent = s.case_id;
  const pills = [];
  const [convLabel, convCls] = LABELS.conversation[s.conversation_status] || [s.conversation_status, ''];
  pills.push(`<span class="pill ${convCls}">${convLabel}</span>`);
  const [resLabel, resCls] = LABELS.resolution[s.resolution_status] || [s.resolution_status, ''];
  pills.push(`<span class="pill ${resCls}">${resLabel}</span>`);
  if (s.moderation_warning_count > 0) pills.push(`<span class="pill warn">предупреждений: ${s.moderation_warning_count}</span>`);
  if (s.last_decision) pills.push(`<span class="pill dev-only mono" title="last_decision">${s.last_decision}</span>`);
  if (s.active_turn) pills.push(`<span class="pill dev-only mono" title="active_turn">rev ${s.active_turn.revision} · ${s.active_turn.status}</span>`);
  $('case-pills').innerHTML = pills.join('');
  $('btn-complete').hidden = s.conversation_status !== 'ACTIVE';
}

/* ---------- timeline ---------- */

function renderTimeline() {
  const box = $('timeline');
  const atBottom = box.scrollHeight - box.scrollTop - box.clientHeight < 80;
  box.innerHTML = '';
  const events = [...state.timeline.values()].sort((a, b) => Number(a.event_id) - Number(b.event_id));
  if (!events.length && !state.pendingUser) box.innerHTML = '<div class="sys">Пока нет сообщений. Опишите вопрос ниже.</div>';
  let lastTurn = null;
  for (const ev of events) {
    const el = renderEvent(ev, lastTurn);
    if (el) box.appendChild(el);
    if (ev.turn_id) lastTurn = ev.turn_id;
  }
  if (state.pendingUser) {
    const el = document.createElement('div');
    el.className = 'msg user pending';
    el.innerHTML = `<div class="body">${escapeHtml(state.pendingUser)}</div>`;
    box.appendChild(el);
    const st = document.createElement('div');
    st.className = 'stage';
    st.textContent = 'Отправляем…';
    box.appendChild(st);
  }
  if (atBottom) box.scrollTop = box.scrollHeight;
}

function devMeta(ev, extra = '') {
  return `<div class="meta dev-only"><span class="type">${escapeHtml(ev.type)}</span><span>#${ev.event_id}</span>${ev.turn_id ? `<span title="turn_id">turn ${shortId(ev.turn_id)}</span>` : ''}${extra}</div>`;
}

function renderEvent(ev, lastTurn) {
  const p = ev.payload || {};
  const el = document.createElement('div');
  const isLatestTurnEvent = state.snapshot?.active_turn?.turn_id === ev.turn_id;
  switch (ev.type) {
    case 'USER_MESSAGE':
      el.className = 'msg user';
      el.innerHTML = `${devMeta(ev)}<div class="body">${escapeHtml(p.text)}</div><div class="meta"><span>${fmtTime(ev.occurred_at)}</span></div>`;
      return el;
    case 'TURN_STAGE': {
      // Stage lines matter only while the latest turn is still running; for finished turns they are noise.
      const turnDone = [...state.timeline.values()].some((x) => x.turn_id === ev.turn_id && ['AI_ANSWER', 'CLARIFICATION', 'NO_CONFIRMED_ANSWER', 'TECHNICAL_ERROR', 'MODERATION_WARNING', 'CONVERSATION_CLOSED'].includes(x.type));
      if (turnDone && !state.devDetails) return null;
      el.className = 'stage';
      el.innerHTML = `${escapeHtml(p.stage)}<span class="mono muted dev-only">#${ev.event_id}</span>`;
      return el;
    }
    case 'AI_ANSWER': {
      el.className = 'msg ai answer';
      const sources = Array.isArray(p.sources) ? p.sources : [];
      el.innerHTML = `${devMeta(ev)}<div class="body">${renderMarkdown(p.markdown)}</div>` +
        (sources.length ? `<div class="sources">${sources.map((id) => `<button class="chip" data-source="${escapeHtml(id)}" title="Открыть фрагмент источника">📄 источник ${escapeHtml(shortId(id))}</button>`).join('')}</div>` : '<div class="muted small" style="margin-top:6px">источники не переданы в событии</div>') +
        `<div class="meta"><span>${fmtTime(ev.occurred_at)}</span><span>ответ проверен по базе знаний</span></div>`;
      return el;
    }
    case 'CLARIFICATION': {
      el.className = 'msg ai clarify';
      const conds = Array.isArray(p.missing_conditions) ? p.missing_conditions : [];
      el.innerHTML = `${devMeta(ev)}<div class="body"><p><strong>Нужно уточнить.</strong> Ответ зависит от условий, которых нет в вопросе:</p><ul class="conds">${conds.map((c) => `<li>${escapeHtml(c)}</li>`).join('') || '<li class="muted">(условия не указаны)</li>'}</ul><p class="muted small">Ответьте одним сообщением с уточнением.</p></div>`;
      return el;
    }
    case 'NO_CONFIRMED_ANSWER':
      el.className = 'msg ai noanswer';
      el.innerHTML = `${devMeta(ev)}<div class="body"><p><strong>Подтверждённого ответа в базе знаний нет.</strong></p><p class="muted small">Мы не придумываем ответ, если инструкции его не подтверждают. Ниже — передача специалисту.</p></div>`;
      return el;
    case 'HANDOFF_OFFER': {
      el.className = 'msg ai';
      const reasons = Array.isArray(p.reason_codes) ? p.reason_codes : [];
      const h = state.snapshot?.handoff;
      const canOffer = state.snapshot?.conversation_status === 'ACTIVE' && !h && isLatestTurnEvent;
      el.innerHTML = `${devMeta(ev)}<div class="body"><p><strong>Передать обращение специалисту?</strong></p>` +
        `<p class="muted small">Очередь: <code>${escapeHtml(p.dispatch_queue)}</code>, линия ${escapeHtml(p.recommended_line)}${p.engineering_review_suggested ? ', рекомендована техническая диагностика' : ''}.</p>` +
        (reasons.length ? `<p class="dev-only small">reason codes: ${reasons.map((r) => `<code>${escapeHtml(r)}</code>`).join(' ')}</p>` : '') +
        `</div><div class="actions">${canOffer ? '<button class="btn btn-small btn-primary" data-action="handoff-prepare">Передать специалисту</button>' : h ? '<span class="muted small">передача уже создана — см. виджет выше</span>' : ''}</div>`;
      return el;
    }
    case 'MODERATION_WARNING':
      el.className = 'sys warn';
      el.innerHTML = `⚠ Предупреждение: в сообщении обнаружена ненормативная лексика. Повторное нарушение закроет обращение (предупреждений: ${p.moderation_warning_count ?? '?'}).<span class="mono dev-only">#${ev.event_id}</span>`;
      return el;
    case 'CONVERSATION_CLOSED':
      el.className = 'sys err';
      el.innerHTML = `⛔ Обращение закрыто модерацией после повторного нарушения. История сохранена, новые сообщения не принимаются.<span class="mono dev-only">#${ev.event_id}</span>`;
      return el;
    case 'TECHNICAL_ERROR':
      el.className = 'msg ai error';
      el.innerHTML = `${devMeta(ev)}<div class="body"><p><strong>Техническая ошибка.</strong> ${escapeHtml(LABELS.failure[p.category] || 'не удалось обработать вопрос')}.</p><p class="muted small">Это не значит, что ответа нет в базе. Повторите вопрос позже или передайте специалисту.</p><p class="dev-only small">category: <code>${escapeHtml(p.category)}</code></p></div>`;
      return el;
    case 'HANDOFF_STATUS': {
      el.className = 'sys';
      const bits = [];
      if (p.stage) bits.push(`этап: <strong>${escapeHtml(p.stage.display_name || p.stage.code)}</strong>`);
      if (p.assigned_specialist) bits.push(`специалист: <strong>${escapeHtml(p.assigned_specialist.display_name || p.assigned_specialist.ref)}</strong>`);
      if (p.terminal) bits.push(`итог: <strong>${escapeHtml(LABELS.terminal[p.terminal] || p.terminal)}</strong>`);
      const demo = String(p.integration_mode || '').toUpperCase() === 'SIMULATED' ? ' <span class="pill warn">демо</span>' : '';
      el.innerHTML = `Статус передачи · ${bits.join(' · ') || 'обновлён'}${demo}<span class="mono dev-only">#${ev.event_id} ${fmtTime(ev.occurred_at)}</span>`;
      return el;
    }
    case 'CASE_RESOLUTION_CHANGED': {
      el.className = 'sys';
      const [label] = LABELS.resolution[String(p.resolution_status || '').toUpperCase()] || [p.resolution_status];
      el.innerHTML = `Результат обращения: <strong>${escapeHtml(label)}</strong><span class="mono dev-only">#${ev.event_id}</span>`;
      return el;
    }
    case 'CASE_COMPLETED': {
      el.className = 'sys ok';
      const who = { USER: 'вами', User: 'вами', SUPPORT: 'поддержкой', Support: 'поддержкой', MODERATION: 'модерацией', Moderation: 'модерацией' }[p.completion_reason] || '';
      el.innerHTML = `✓ Обращение завершено ${who} · ${fmtDate(ev.occurred_at)}<span class="mono dev-only">#${ev.event_id}</span>`;
      return el;
    }
    case 'FEEDBACK_REQUESTED':
      el.className = 'sys';
      el.innerHTML = `Оцените обращение — форма выше.<span class="mono dev-only">#${ev.event_id}</span>`;
      return el;
    case 'FEEDBACK_SUBMITTED':
      el.className = 'sys ok';
      el.innerHTML = `Спасибо за оценку.<span class="mono dev-only">#${ev.event_id}</span>`;
      return el;
    default:
      el.className = 'sys';
      el.innerHTML = `<span class="mono">${escapeHtml(ev.type)}</span> ${escapeHtml(JSON.stringify(p))}`;
      return el;
  }
}

/* ---------- composer ---------- */

function renderComposerState() {
  const s = state.snapshot;
  const blocked = $('composer-blocked');
  const form = $('composer-form');
  if (!s) return;
  if (s.conversation_status !== 'ACTIVE') {
    const [label] = LABELS.conversation[s.conversation_status] || [s.conversation_status];
    blocked.textContent = `${label}: новые сообщения не принимаются (сервер вернёт 409 CASE_CLOSED).`;
    blocked.hidden = false;
    form.hidden = true;
  } else {
    blocked.hidden = true;
    form.hidden = false;
  }
  $('btn-send').disabled = state.sending;
}

async function sendMessage() {
  const ta = $('composer-text');
  const text = ta.value.trim();
  if (!text || state.sending || !state.activeCaseId) return;
  state.sending = true;
  state.pendingUser = text;
  $('composer-error').hidden = true;
  renderComposerState();
  renderTimeline();
  const e = await api('POST', `/api/v0/cases/${encodeURIComponent(state.activeCaseId)}/messages`, { body: { text, client_message_id: genUuid() }, silent: true });
  state.sending = false;
  state.pendingUser = null;
  if (e.ok) {
    ta.value = '';
    $('composer-count').textContent = '0 / 4000';
    const d = e.data;
    if (d.decision === 'TECHNICAL_ERROR') toast('warn', 'Техническая ошибка при обработке', LABELS.failure[d.failure_category] || d.failure_category);
  } else {
    const err = $('composer-error');
    err.textContent = `Не отправлено — ${e.human}`;
    err.hidden = false;
    if (e.status === 409 && e.data?.code === 'CASE_CLOSED') toast('warn', 'Обращение закрыто', 'Создайте новое обращение.');
    else if (e.status === 409 && e.data?.code === 'CONCURRENCY_CONFLICT') toast('warn', 'Параллельная отправка', 'Другой запрос по этому обращению ещё выполняется. Повторите.');
    else toast('err', `Ошибка отправки (${e.status || 'сеть'})`, e.human);
  }
  await refreshSnapshot();
  loadCases();
  renderComposerState();
  ta.focus();
}

/* ---------- handoff widget ---------- */

function renderHandoffWidget() {
  const w = $('handoff-widget');
  const h = state.snapshot?.handoff;
  if (!h) { w.hidden = true; return; }
  const [label, cls] = LABELS.handoff[h.status] || [h.status, ''];
  const simulated = String(h.integration_mode || '').toUpperCase() === 'SIMULATED';
  w.className = 'widget ' + (h.status === 'FAILED' ? 'failed' : simulated ? 'simulated' : h.status === 'ACCEPTED' ? 'ok' : '');
  const lastUser = [...state.timeline.values()].filter((x) => x.type === 'USER_MESSAGE').pop()?.payload?.text || '';
  const rows = [];
  rows.push(['Статус', `<span class="pill ${cls}">${label}</span>${simulated ? ' <span class="pill warn" title="integration_mode=SIMULATED">демо-адаптер</span>' : ''}`]);
  if (h.external_case_id) rows.push(['Номер в поддержке', `<code>${escapeHtml(h.external_case_id)}</code>`]);
  if (h.stage) rows.push(['Этап', escapeHtml(h.stage.display_name || h.stage.code)]);
  if (h.assigned_specialist) rows.push(['Специалист', `${escapeHtml(h.assigned_specialist.display_name || h.assigned_specialist.ref)}${simulated ? ' <span class="muted small">(демо)</span>' : ''}`]);
  if (h.terminal) rows.push(['Итог', escapeHtml(LABELS.terminal[h.terminal] || h.terminal)]);
  if (h.updated_at) rows.push(['Принято', fmtDate(h.updated_at)]);
  let actions = '';
  if (h.status === 'NOT_REQUESTED' && state.snapshot.conversation_status === 'ACTIVE') {
    actions = `<div class="field" style="margin-top:8px"><span>Кратко опишите проблему для специалиста</span><textarea id="handoff-summary" rows="2">${escapeHtml(lastUser)}</textarea></div>` +
      `<div class="row" style="margin-top:8px"><button class="btn btn-small btn-primary" data-action="handoff-confirm">Отправить в поддержку</button><span class="muted small">маршрут и коды причин сервер берёт из последнего HANDOFF_OFFER, клиент их не меняет</span></div>`;
  } else if (h.status === 'FAILED' && state.snapshot.conversation_status === 'ACTIVE') {
    actions = `<div class="field" style="margin-top:8px"><span>Повторить отправку — описание для специалиста</span><textarea id="handoff-summary" rows="2">${escapeHtml(lastUser)}</textarea></div>` +
      `<div class="row" style="margin-top:8px"><button class="btn btn-small btn-primary" data-action="handoff-retry">Повторить</button></div>`;
  } else if (h.status === 'PENDING') {
    actions = '<p class="muted small" style="margin-top:6px">Ждём подтверждения от адаптера поддержки (api-worker доставляет из outbox, обычно ≤ 3 с).</p>';
  }
  w.innerHTML = `<h3>Передача специалисту</h3><dl class="kv">${rows.map(([k, v]) => `<dt>${k}</dt><dd>${v}</dd>`).join('')}</dl>` +
    (h.stale ? '<div class="stale">⚠ Статус не обновляется: опрос поддержки достиг лимита времени без итогового факта.</div>' : '') +
    (!h.stage && !h.assigned_specialist && !h.terminal && (h.status === 'ACCEPTED' || h.status === 'SIMULATED_ACCEPTED') ? '<p class="muted small" style="margin-top:6px">Этап и специалист появятся только когда их сообщит адаптер — заглушек нет.</p>' : '') +
    actions;
  w.hidden = false;
}

async function handoffPrepare() {
  const e = await api('POST', `/api/v0/cases/${encodeURIComponent(state.activeCaseId)}/handoff/prepare`);
  if (!e.ok) return;
  toast('ok', 'Пакет передачи подготовлен', 'Отредактируйте описание и отправьте.');
  await refreshSnapshot();
  $('handoff-summary')?.focus();
}

async function handoffSubmit(kind) {
  const summary = ($('handoff-summary')?.value || '').trim();
  if (!summary) { toast('warn', 'Нужно описание', 'Поле summary обязательно (сервер вернёт 400).'); $('handoff-summary')?.focus(); return; }
  const e = await api('POST', `/api/v0/cases/${encodeURIComponent(state.activeCaseId)}/handoff/${kind}`, { body: { summary }, idempotencyKey: genUuid() });
  if (e.ok) toast('ok', kind === 'confirm' ? 'Отправлено в поддержку' : 'Повторная отправка запущена', 'Статус обновится по SSE.');
  await refreshSnapshot();
}

/* ---------- completion / feedback ---------- */

async function completeCase(solved) {
  const body = solved === '' ? { solved: null } : { solved: solved === 'true' };
  const e = await api('POST', `/api/v0/cases/${encodeURIComponent(state.activeCaseId)}/complete`, { body });
  $('complete-pop').hidden = true;
  if (e.ok) toast('ok', 'Обращение завершено', 'Оцените его в форме выше.');
  await refreshSnapshot();
  loadCases();
}

function renderFeedbackWidget() {
  const w = $('feedback-widget');
  const s = state.snapshot;
  if (!s || !s.completed_at || s.completion_reason === 'MODERATION') { w.hidden = true; return; }
  w.className = 'widget';
  if (s.feedback) {
    const f = s.feedback;
    const r = (v) => v == null ? '—' : v === 'POSITIVE' ? '👍' : '👎';
    w.innerHTML = `<h3>Ваша оценка</h3><dl class="kv"><dt>Специалист</dt><dd>${r(f.specialist_rating)}</dd><dt>Качество информации</dt><dd>${r(f.information_quality_rating)}</dd><dt>Вопрос решён</dt><dd>${f.solved == null ? '—' : f.solved ? 'да' : 'нет'}</dd>${f.comment_text ? `<dt>Комментарий</dt><dd>${escapeHtml(f.comment_text)}</dd>` : ''}<dt>Отправлено</dt><dd>${fmtDate(f.submitted_at)}</dd></dl>`;
    w.hidden = false;
    return;
  }
  const h = s.handoff;
  const showSpecialist = h && (h.status === 'ACCEPTED' || h.status === 'SIMULATED_ACCEPTED');
  const showSolved = s.resolution_status === 'UNKNOWN';
  const rating = (name, title, note) => `<div class="row"><span style="min-width:190px">${title}${note ? ` <span class="muted small">${note}</span>` : ''}</span>` +
    `<label class="check"><input type="radio" name="${name}" value="POSITIVE"> 👍</label><label class="check"><input type="radio" name="${name}" value="NEGATIVE"> 👎</label><label class="check"><input type="radio" name="${name}" value="" checked> без оценки</label></div>`;
  w.innerHTML = `<h3>Оцените обращение</h3>` +
    (showSpecialist ? rating('fb-specialist', 'Работа специалиста', h.status === 'SIMULATED_ACCEPTED' ? '(демо)' : '') : '') +
    rating('fb-quality', 'Качество информации') +
    (showSolved ? `<div class="row"><span style="min-width:190px">Вопрос решён?</span><label class="check"><input type="radio" name="fb-solved" value="true"> да</label><label class="check"><input type="radio" name="fb-solved" value="false"> нет</label><label class="check"><input type="radio" name="fb-solved" value="" checked> не знаю</label></div>` : '') +
    `<div class="field" style="margin-top:6px"><span>Комментарий (необязательно)</span><input id="fb-comment" type="text" maxlength="1000"></div>` +
    `<div class="row" style="margin-top:8px"><button class="btn btn-small btn-primary" data-action="feedback-submit">Отправить оценку</button><span class="muted small">одна оценка на обращение; повторная — 409</span></div>`;
  w.hidden = false;
}

async function submitFeedback() {
  const radio = (name) => document.querySelector(`input[name="${name}"]:checked`)?.value ?? '';
  const body = {
    specialist_rating: radio('fb-specialist') || null,
    information_quality_rating: radio('fb-quality') || null,
    solved: radio('fb-solved') === '' ? null : radio('fb-solved') === 'true',
    comment_text: ($('fb-comment')?.value || '').trim() || null,
  };
  const e = await api('POST', `/api/v0/cases/${encodeURIComponent(state.activeCaseId)}/feedback`, { body, idempotencyKey: genUuid() });
  if (e.ok) toast('ok', 'Спасибо за оценку');
  await refreshSnapshot();
}

/* ---------- sources ---------- */

async function openSource(fragmentId) {
  const e = await api('GET', `/api/v0/sources/${encodeURIComponent(fragmentId)}`);
  const body = $('drawer-body');
  if (!e.ok) {
    body.innerHTML = `<p class="inline-error">${escapeHtml(e.human)}</p><p class="muted small">Фрагмент: <code>${escapeHtml(fragmentId)}</code>. Источник читается через knowledge; если он недоступен, это ошибка инфраструктуры, а не «нет такого документа».</p>`;
  } else {
    const s = e.data;
    $('drawer-title').textContent = s.title || 'Источник';
    body.innerHTML = `<dl class="src-meta"><dt>Документ</dt><dd>${escapeHtml(s.document_id)}</dd><dt>Версия</dt><dd>${escapeHtml(s.version)}</dd>${s.page != null ? `<dt>Страница</dt><dd>${escapeHtml(s.page)}</dd>` : ''}${s.anchor ? `<dt>Якорь</dt><dd>${escapeHtml(s.anchor)}</dd>` : ''}<dt>Snapshot</dt><dd>${escapeHtml(s.snapshot_id)}</dd><dt>Фрагмент</dt><dd>${escapeHtml(fragmentId)}</dd></dl><div class="src-text">${escapeHtml(s.text)}</div>`;
  }
  $('drawer').hidden = false;
}

/* ---------- SSE: case ---------- */

function sseLog(cls, text) {
  const list = $('sse-list');
  const el = document.createElement('div');
  el.className = `sse-entry ${cls}`;
  el.textContent = `${fmtTime(new Date())} ${text}`;
  list.prepend(el);
  while (list.children.length > 200) list.lastChild.remove();
}

function closeCaseStream() {
  if (state.caseStream) { state.caseStream.close(); state.caseStream = null; sseLog('info', 'case stream closed'); }
  $('sse-case-label').textContent = 'case stream: —';
}

function openCaseStream() {
  closeCaseStream();
  const id = state.activeCaseId;
  if (!id) return;
  const url = `${state.baseUrl || ''}/api/v0/cases/${encodeURIComponent(id)}/events/stream`;
  const es = new EventSource(url, { withCredentials: true });
  state.caseStream = es;
  $('sse-case-label').textContent = `case stream: ${shortId(id)} · подключение…`;
  es.addEventListener('open', () => { $('sse-case-label').textContent = `case stream: ${shortId(id)} · открыт`; sseLog('info', `open ${url}`); });
  es.addEventListener('case_event', (ev) => {
    let data;
    try { data = JSON.parse(ev.data); } catch { sseLog('err', `bad JSON: ${ev.data}`); return; }
    sseLog('', `#${ev.lastEventId} ${data.type} ${JSON.stringify(data.payload)}`);
    if (data.case_id !== state.activeCaseId) return;
    const isNew = !state.timeline.has(String(data.event_id));
    state.timeline.set(String(data.event_id), data);
    if (!isNew) return;
    renderTimeline();
    if (!['USER_MESSAGE', 'TURN_STAGE'].includes(data.type)) scheduleRefresh();
  });
  es.addEventListener('error', () => {
    $('sse-case-label').textContent = `case stream: ${shortId(id)} · ошибка, реконнект (Last-Event-ID)`;
    sseLog('err', 'error — браузер переподключится сам');
  });
}

/* ---------- notifications ---------- */

function renderNotifications() {
  const items = [...state.notifications.values()].sort((a, b) => Number(b.notification_id) - Number(a.notification_id));
  const unread = items.filter((n) => !n.read_at).length;
  const badge = $('notif-badge');
  badge.textContent = unread;
  badge.hidden = unread === 0;
  const list = $('notif-list');
  list.innerHTML = items.length ? '' : '<div class="empty muted small">Пока пусто</div>';
  for (const n of items) {
    const el = document.createElement('div');
    el.className = 'notif-item' + (n.read_at ? '' : ' unread');
    const demo = String(n.payload?.integration_mode || '').toUpperCase() === 'SIMULATED' ? ' <span class="pill warn">демо</span>' : '';
    el.innerHTML = `<div class="t">${escapeHtml(n.payload?.title || n.type)}${demo}</div><div class="b">${escapeHtml(n.payload?.body || '')}</div><div class="muted small">${fmtTime(n.occurred_at)} · обращение ${shortId(n.case_id)}<span class="dev-only mono"> · #${escapeHtml(n.notification_id)} ${escapeHtml(n.type)}</span></div>`;
    el.addEventListener('click', async () => {
      if (!n.read_at) await ackNotifications([n.notification_id]);
      $('notif-panel').hidden = true;
      if (state.activeCaseId !== n.case_id) {
        if (!state.cases.some((c) => c.case_id === n.case_id)) { state.view = state.view === 'active' ? 'archived' : 'active'; document.querySelectorAll('.seg-btn').forEach((b) => b.classList.toggle('active', b.dataset.view === state.view)); await loadCases(); }
        openCase(n.case_id);
      }
    });
    list.appendChild(el);
  }
}

async function loadNotifications() {
  const e = await api('GET', '/api/v0/notifications', { silent: true });
  if (!e.ok) return;
  for (const n of e.data) state.notifications.set(String(n.notification_id), n);
  renderNotifications();
}

async function ackNotifications(ids) {
  if (!ids.length) return;
  const e = await api('POST', '/api/v0/notifications/ack', { body: { ids: ids.map(String) } });
  if (!e.ok) return;
  const now = new Date().toISOString();
  for (const id of ids) { const n = state.notifications.get(String(id)); if (n) n.read_at = now; }
  renderNotifications();
}

function openNotificationStream() {
  if (state.notifStream) state.notifStream.close();
  const es = new EventSource(`${state.baseUrl || ''}/api/v0/notifications/stream`, { withCredentials: true });
  state.notifStream = es;
  const label = $('sse-notif-label');
  es.addEventListener('open', () => { label.className = 'on'; label.title = 'SSE /notifications/stream: открыт'; });
  es.addEventListener('notification', (ev) => {
    let n;
    try { n = JSON.parse(ev.data); } catch { sseLog('err', `notification bad JSON: ${ev.data}`); return; }
    sseLog('', `notification #${ev.lastEventId} ${n.type} → ${shortId(n.case_id)}`);
    const isNew = !state.notifications.has(String(n.notification_id));
    state.notifications.set(String(n.notification_id), n);
    renderNotifications();
    if (isNew && !n.read_at) {
      toast('ok', n.payload?.title || n.type, `${n.payload?.body || ''} · обращение ${shortId(n.case_id)}`);
      if (n.case_id === state.activeCaseId) scheduleRefresh(); else loadCases();
    }
  });
  es.addEventListener('error', () => { label.className = 'off'; label.title = 'SSE /notifications/stream: ошибка, реконнект'; });
}

/* ---------- analytics ---------- */

async function loadEvaluations() {
  const out = $('analytics-out');
  if (!state.activeCaseId) { out.innerHTML = '<p class="muted small">Сначала откройте обращение.</p>'; return; }
  const e = await api('GET', '/api/v0/analytics/evaluations', { query: { case_id: state.activeCaseId } });
  if (!e.ok) { out.innerHTML = `<div class="inline-error">${escapeHtml(e.human)}</div>`; return; }
  const evs = e.data.evaluations || [];
  out.innerHTML = evs.length ? '' : '<p class="muted small">Оценок по этому обращению пока нет (quality-worker считает их асинхронно).</p>';
  for (const ev of evs) {
    const dim = (name, d) => `<tr><td>${name}</td><td><strong>${escapeHtml(d.value)}</strong></td><td>${escapeHtml(d.reason || d.limitation || '')}</td></tr>`;
    out.innerHTML += `<div class="card"><h4>turn ${shortId(ev.turn_id)} · ${fmtDate(ev.evaluated_at)}${ev.critical_error ? ' <span class="pill err">критическая ошибка</span>' : ''}</h4><table>${dim('Опора на факты', ev.factual_support)}${dim('Полнота', ev.completeness)}${dim('Ясность', ev.clarity)}${dim('Следующий шаг', ev.next_step)}</table>${ev.critical_error_reason ? `<p class="small" style="color:var(--err)">${escapeHtml(ev.critical_error_reason)}</p>` : ''}</div>`;
  }
}

async function loadIssueGroups() {
  const out = $('analytics-out');
  const e = await api('GET', '/api/v0/analytics/issue-groups');
  if (!e.ok) { out.innerHTML = `<div class="inline-error">${escapeHtml(e.human)}</div>`; return; }
  const groups = e.data.groups || [];
  out.innerHTML = groups.length ? '' : '<p class="muted small">Групп проблем пока нет.</p>';
  for (const g of groups) {
    out.innerHTML += `<div class="card"><h4>${escapeHtml(g.label)} <span class="pill">n=${g.n}</span>${g.negative_signal_count != null ? ` <span class="pill warn">негатив: ${g.negative_signal_count}</span>` : ''}${g.unresolved_count != null ? ` <span class="pill">не решено: ${g.unresolved_count}</span>` : ''}</h4>` +
      (g.hypotheses?.length ? `<ul>${g.hypotheses.map((h) => `<li><code>${escapeHtml(h.type)}</code> ${escapeHtml(h.text)}${h.confidence ? ` <span class="muted small">(${escapeHtml(h.confidence)})</span>` : ''}</li>`).join('')}</ul>` : '') +
      (g.limitations?.length ? `<p class="muted small">Ограничения: ${g.limitations.map(escapeHtml).join('; ')}</p>` : '') +
      (g.representative_case_ids?.length ? `<p class="small">Кейсы: ${g.representative_case_ids.map((id) => `<button class="chip" data-open-case="${escapeHtml(id)}">${escapeHtml(shortId(id))}</button>`).join(' ')}</p>` : '') +
      `</div>`;
  }
}

/* ---------- wiring ---------- */

function applyDevSettings() {
  document.body.classList.toggle('dev-details', state.devDetails);
  document.body.classList.toggle('dev-collapsed', state.devCollapsed);
  $('set-dev-details').checked = state.devDetails;
  $('set-auto-sse').checked = state.autoSse;
  $('set-base-url').value = state.baseUrl;
  $('btn-dev-toggle').textContent = state.devCollapsed ? '⇤' : '⇥';
}

function wire() {
  $('btn-new-case').addEventListener('click', createCase);
  document.querySelectorAll('.seg-btn').forEach((b) => b.addEventListener('click', () => {
    state.view = b.dataset.view;
    document.querySelectorAll('.seg-btn').forEach((x) => x.classList.toggle('active', x === b));
    loadCases();
  }));
  $('btn-refresh').addEventListener('click', () => { refreshSnapshot(); loadCases(); });
  $('btn-complete').addEventListener('click', () => { $('complete-pop').hidden = !$('complete-pop').hidden; });
  $('complete-pop').addEventListener('click', (ev) => {
    const v = ev.target.closest('[data-complete]')?.dataset.complete;
    if (v === undefined) return;
    if (v === 'cancel') { $('complete-pop').hidden = true; return; }
    completeCase(v);
  });

  const ta = $('composer-text');
  ta.addEventListener('input', () => { $('composer-count').textContent = `${ta.value.length} / 4000`; });
  ta.addEventListener('keydown', (ev) => { if (ev.key === 'Enter' && !ev.shiftKey) { ev.preventDefault(); sendMessage(); } });
  $('composer-form').addEventListener('submit', (ev) => { ev.preventDefault(); sendMessage(); });

  document.addEventListener('click', (ev) => {
    const src = ev.target.closest('[data-source]');
    if (src) { openSource(src.dataset.source); return; }
    const oc = ev.target.closest('[data-open-case]');
    if (oc) { openCase(oc.dataset.openCase); return; }
    const action = ev.target.closest('[data-action]')?.dataset.action;
    if (!action) return;
    if (action === 'handoff-prepare') handoffPrepare();
    if (action === 'handoff-confirm') handoffSubmit('confirm');
    if (action === 'handoff-retry') handoffSubmit('retry');
    if (action === 'feedback-submit') submitFeedback();
  });

  $('btn-drawer-close').addEventListener('click', () => { $('drawer').hidden = true; });
  $('drawer').addEventListener('click', (ev) => { if (ev.target === $('drawer')) $('drawer').hidden = true; });

  $('btn-notif').addEventListener('click', () => { $('notif-panel').hidden = !$('notif-panel').hidden; });
  $('btn-notif-close').addEventListener('click', () => { $('notif-panel').hidden = true; });
  $('btn-notif-ack-all').addEventListener('click', () => ackNotifications([...state.notifications.values()].filter((n) => !n.read_at).map((n) => n.notification_id)));

  document.querySelectorAll('.tab').forEach((t) => t.addEventListener('click', () => {
    document.querySelectorAll('.tab').forEach((x) => x.classList.toggle('active', x === t));
    document.querySelectorAll('.tab-body').forEach((b) => { b.hidden = b.dataset.tab !== t.dataset.tab; });
  }));
  $('btn-dev-toggle').addEventListener('click', () => { state.devCollapsed = !state.devCollapsed; localStorage.setItem('th_dev_collapsed', state.devCollapsed ? '1' : '0'); applyDevSettings(); });
  $('log-errors-only').addEventListener('change', renderLog);
  $('btn-log-clear').addEventListener('click', () => { state.log = []; renderLog(); });
  $('btn-sse-clear').addEventListener('click', () => { $('sse-list').innerHTML = ''; });
  $('btn-snapshot-copy').addEventListener('click', () => navigator.clipboard?.writeText($('snapshot-json').textContent).then(() => toast('ok', 'Snapshot скопирован')));
  $('btn-evals').addEventListener('click', loadEvaluations);
  $('btn-issue-groups').addEventListener('click', loadIssueGroups);

  $('set-dev-details').addEventListener('change', (ev) => { state.devDetails = ev.target.checked; localStorage.setItem('th_dev_details', state.devDetails ? '1' : '0'); applyDevSettings(); renderTimeline(); });
  $('set-auto-sse').addEventListener('change', (ev) => { state.autoSse = ev.target.checked; localStorage.setItem('th_auto_sse', state.autoSse ? '1' : '0'); if (state.autoSse && state.activeCaseId) openCaseStream(); else closeCaseStream(); });
  $('set-base-url').addEventListener('change', (ev) => { state.baseUrl = ev.target.value.trim().replace(/\/+$/, ''); localStorage.setItem('th_base_url', state.baseUrl); toast('warn', 'Base URL изменён', 'Перезагрузите страницу, чтобы переподключить SSE.'); });
  $('btn-health-live').addEventListener('click', () => api('GET', '/health/live').then((e) => e.ok && toast('ok', 'health/live', JSON.stringify(e.data))));
  $('btn-health-ready').addEventListener('click', () => api('GET', '/health/ready').then((e) => e.ok && toast('ok', 'health/ready', JSON.stringify(e.data))));
  $('btn-session').addEventListener('click', ensureSession);
}

async function boot() {
  applyDevSettings();
  wire();
  await checkHealth();
  setInterval(checkHealth, 30000);
  const ok = await ensureSession();
  if (!ok) { toast('err', 'Нет сессии', 'POST /api/v0/session не удался — смотрите лог запросов справа.'); }
  await loadCases();
  await loadNotifications();
  openNotificationStream();
  if (state.activeCaseId) await openCase(state.activeCaseId);
}

boot();
