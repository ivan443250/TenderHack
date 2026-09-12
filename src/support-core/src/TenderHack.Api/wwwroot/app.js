'use strict';

/* ---------- state ---------- */

const state = {
  baseUrl: localStorage.getItem('th_base_url') || '',
  activeCaseId: localStorage.getItem('th_case_id') || '',
  caseStream: null,
  notifStream: null,
};

const $ = (id) => document.getElementById(id);

function genUuid() {
  if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

/* ---------- HTTP layer ---------- */

async function apiRequest(method, path, { query, body, idempotencyKey } = {}) {
  let url = (state.baseUrl || '') + path;
  if (query) {
    const params = new URLSearchParams();
    for (const [k, v] of Object.entries(query)) {
      if (v !== undefined && v !== null && v !== '') params.set(k, v);
    }
    const qs = params.toString();
    if (qs) url += (url.includes('?') ? '&' : '?') + qs;
  }

  const headers = {};
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (idempotencyKey) headers['Idempotency-Key'] = idempotencyKey;

  const entry = {
    id: genUuid(),
    ts: new Date(),
    method,
    path: url.replace(state.baseUrl || '', '') || '/',
    url,
  };

  const started = performance.now();
  try {
    const res = await fetch(url, {
      method,
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
      credentials: 'same-origin',
    });
    entry.durationMs = Math.round(performance.now() - started);
    entry.status = res.status;
    entry.ok = res.ok;

    const text = await res.text();
    if (text) {
      try {
        entry.data = JSON.parse(text);
      } catch (e) {
        entry.data = text;
        entry.parseError = e.message;
      }
    } else {
      entry.data = null;
    }
  } catch (err) {
    entry.durationMs = Math.round(performance.now() - started);
    entry.status = 0;
    entry.ok = false;
    entry.networkError = err.message;
  }

  pushLog(entry);
  return entry;
}

/* ---------- rendering ---------- */

function humanMessage(entry) {
  if (entry.networkError) {
    const crossOrigin = state.baseUrl && !state.baseUrl.startsWith(window.location.origin);
    return (
      `Сетевая ошибка: ${entry.networkError}. ` +
      `Проверьте, что API запущен и доступен по адресу "${entry.url}". ` +
      (crossOrigin
        ? 'Base URL указывает на другой origin — без настроенного CORS на сервере браузер заблокирует запрос. Проще всего оставить Base URL пустым и открывать эту страницу с того же адреса, что и API.'
        : 'Если API поднят на другом порту/хосте, впишите его в поле Base URL вверху страницы.')
    );
  }

  if (entry.parseError) {
    return `Ответ пришёл не в формате JSON (${entry.parseError}). Смотрите тело ответа ниже как есть.`;
  }

  if (entry.data && typeof entry.data === 'object' && !Array.isArray(entry.data)) {
    const parts = [];
    if (entry.data.code) parts.push(`код: ${entry.data.code}`);
    const msg = entry.data.title || entry.data.message || entry.data.detail;
    if (msg) parts.push(msg);
    if (entry.data.errors && typeof entry.data.errors === 'object') {
      const details = Object.entries(entry.data.errors)
        .map(([field, msgs]) => `${field} — ${(Array.isArray(msgs) ? msgs : [msgs]).join(', ')}`)
        .join('; ');
      if (details) parts.push(details);
    }
    if (parts.length) return parts.join(' · ');
  }

  if (entry.ok) return '';
  return `HTTP ${entry.status}: сервер вернул ошибку без стандартного описания. Смотрите тело ответа ниже.`;
}

function renderResult(container, entry) {
  if (!container) return;
  container.innerHTML = '';
  container.classList.remove('result-ok', 'result-err');
  container.classList.add(entry.ok ? 'result-ok' : 'result-err');

  const summary = document.createElement('div');
  summary.className = 'result-summary';
  const statusLabel = entry.status === 0 ? 'NETWORK ERROR' : entry.status;
  summary.innerHTML =
    `<span class="badge ${entry.ok ? 'badge-ok' : 'badge-err'}">${statusLabel}</span>` +
    `<span class="method">${entry.method}</span>` +
    `<span class="path">${escapeHtml(entry.path)}</span>` +
    `<span class="duration">${entry.durationMs} ms</span>`;
  container.appendChild(summary);

  const human = humanMessage(entry);
  if (human) {
    const h = document.createElement('div');
    h.className = 'result-human';
    h.textContent = human;
    container.appendChild(h);
  }

  if (!entry.networkError) {
    const pre = document.createElement('pre');
    pre.className = 'result-json';
    pre.textContent =
      typeof entry.data === 'string' ? entry.data : JSON.stringify(entry.data, null, 2);
    container.appendChild(pre);
  }
}

function escapeHtml(str) {
  return String(str).replace(/[&<>"']/g, (c) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  }[c]));
}

/* ---------- global request log ---------- */

function pushLog(entry) {
  const log = $('request-log');
  if (log.firstElementChild && log.firstElementChild.tagName === 'P') {
    log.innerHTML = '';
  }

  const details = document.createElement('details');
  details.className = 'log-entry';
  details.dataset.ok = String(entry.ok);

  const summary = document.createElement('summary');
  const statusLabel = entry.status === 0 ? 'ERR' : entry.status;
  const time = entry.ts.toLocaleTimeString();
  summary.innerHTML =
    `<span class="ts">${time}</span>` +
    `<span class="badge ${entry.ok ? 'badge-ok' : 'badge-err'}">${statusLabel}</span>` +
    `<span class="method">${entry.method}</span>` +
    `<span class="path">${escapeHtml(entry.path)}</span>` +
    `<span class="duration">${entry.durationMs} ms</span>`;
  details.appendChild(summary);

  const pre = document.createElement('pre');
  const human = humanMessage(entry);
  const bodyText = entry.networkError
    ? entry.networkError
    : typeof entry.data === 'string' ? entry.data : JSON.stringify(entry.data, null, 2);
  pre.textContent = (human ? human + '\n\n' : '') + bodyText;
  details.appendChild(pre);

  log.appendChild(details);
  log.scrollTop = log.scrollHeight;
}

$('btn-clear-log').addEventListener('click', () => {
  $('request-log').innerHTML = '<p class="muted small">Журнал очищен.</p>';
});

/* ---------- base URL ---------- */

$('base-url').value = state.baseUrl;
$('base-url').addEventListener('change', (e) => {
  state.baseUrl = e.target.value.trim();
  localStorage.setItem('th_base_url', state.baseUrl);
});

/* ---------- active case ---------- */

function setActiveCase(caseId) {
  state.activeCaseId = caseId || '';
  localStorage.setItem('th_case_id', state.activeCaseId);
  $('active-case-label').textContent = state.activeCaseId || 'не выбран';
  $('active-case-input').value = state.activeCaseId;
}

setActiveCase(state.activeCaseId);

$('btn-set-active-case').addEventListener('click', () => {
  setActiveCase($('active-case-input').value.trim());
});

function requireActiveCase() {
  const id = $('active-case-input').value.trim() || state.activeCaseId;
  if (!id) {
    window.alert('Сначала укажите активный кейс (создайте кейс или вставьте его id в поле «Активный кейс»).');
    return null;
  }
  setActiveCase(id);
  return id;
}

/* ---------- health ---------- */

$('btn-health-live').addEventListener('click', async () => {
  const e = await apiRequest('GET', '/health/live');
  renderResult($('top-result'), e);
  $('top-result').hidden = false;
});

$('btn-health-ready').addEventListener('click', async () => {
  const e = await apiRequest('GET', '/health/ready');
  renderResult($('top-result'), e);
  $('top-result').hidden = false;
});

/* ---------- UUID generator buttons ---------- */

document.querySelectorAll('[data-gen-uuid]').forEach((btn) => {
  btn.addEventListener('click', () => {
    $(btn.dataset.genUuid).value = genUuid();
  });
});

/* ---------- session ---------- */

$('btn-create-session').addEventListener('click', async () => {
  const e = await apiRequest('POST', '/api/v0/session');
  renderResult($('result-session'), e);
});

/* ---------- cases: create / list ---------- */

$('btn-create-case').addEventListener('click', async () => {
  const idem = $('create-case-idem').value.trim() || undefined;
  const e = await apiRequest('POST', '/api/v0/cases', { body: {}, idempotencyKey: idem });
  renderResult($('result-create-case'), e);
  if (e.ok && e.data && e.data.case_id) {
    setActiveCase(e.data.case_id);
  }
});

$('btn-list-cases').addEventListener('click', async () => {
  const status = $('list-cases-status').value || undefined;
  const e = await apiRequest('GET', '/api/v0/cases', { query: { status } });
  renderResult($('result-list-cases'), e);
  const select = $('cases-select');
  select.innerHTML = '';
  if (e.ok && Array.isArray(e.data) && e.data.length) {
    for (const item of e.data) {
      const opt = document.createElement('option');
      opt.value = item.case_id;
      opt.textContent = `${item.case_id}  ·  ${item.conversation_status}/${item.resolution_status}  ·  unread=${item.unread_notifications}`;
      select.appendChild(opt);
    }
  } else {
    const opt = document.createElement('option');
    opt.value = '';
    opt.textContent = '— нет кейсов —';
    select.appendChild(opt);
  }
});

$('btn-use-selected-case').addEventListener('click', () => {
  const id = $('cases-select').value;
  if (!id) {
    window.alert('Сначала получите список кейсов и выберите один.');
    return;
  }
  setActiveCase(id);
});

/* ---------- active case: snapshot / messages / events ---------- */

$('btn-get-case').addEventListener('click', async () => {
  const id = requireActiveCase();
  if (!id) return;
  const e = await apiRequest('GET', `/api/v0/cases/${encodeURIComponent(id)}`);
  renderResult($('result-get-case'), e);
});

$('btn-send-message').addEventListener('click', async () => {
  const id = requireActiveCase();
  if (!id) return;
  const text = $('message-text').value;
  if (!text.trim()) {
    window.alert('Введите текст сообщения.');
    return;
  }
  const clientMessageId = $('message-client-id').value.trim() || undefined;
  const e = await apiRequest('POST', `/api/v0/cases/${encodeURIComponent(id)}/messages`, {
    body: { text, client_message_id: clientMessageId },
  });
  renderResult($('result-send-message'), e);
});

$('btn-get-events').addEventListener('click', async () => {
  const id = requireActiveCase();
  if (!id) return;
  const after = $('events-after').value || '0';
  const e = await apiRequest('GET', `/api/v0/cases/${encodeURIComponent(id)}/events`, {
    query: { after },
  });
  renderResult($('result-get-events'), e);
});

/* ---------- active case: SSE stream ---------- */

function appendStreamEntry(container, cls, text) {
  const el = document.createElement('div');
  el.className = `stream-entry ${cls}`;
  const time = new Date().toLocaleTimeString();
  el.innerHTML = `<span class="stream-meta">${time}</span>${escapeHtml(text)}`;
  container.appendChild(el);
  container.scrollTop = container.scrollHeight;
}

$('btn-toggle-case-stream').addEventListener('click', () => {
  const btn = $('btn-toggle-case-stream');
  const statusEl = $('case-stream-status');
  const logEl = $('case-stream-log');

  if (state.caseStream) {
    state.caseStream.close();
    state.caseStream = null;
    statusEl.textContent = 'отключено';
    btn.textContent = 'Подключить .../events/stream';
    appendStreamEntry(logEl, 'stream-info', 'Соединение закрыто вручную.');
    return;
  }

  const id = requireActiveCase();
  if (!id) return;

  const url = (state.baseUrl || '') + `/api/v0/cases/${encodeURIComponent(id)}/events/stream`;
  const es = new EventSource(url, { withCredentials: true });
  state.caseStream = es;
  statusEl.textContent = 'подключение...';
  btn.textContent = 'Отключить поток';

  es.addEventListener('open', () => {
    statusEl.textContent = 'подключено';
    appendStreamEntry(logEl, 'stream-info', `Открыто соединение: ${url}`);
  });

  es.addEventListener('case_event', (ev) => {
    let data = ev.data;
    try { data = JSON.stringify(JSON.parse(ev.data)); } catch { /* leave raw */ }
    appendStreamEntry(logEl, 'stream-data', `[id=${ev.lastEventId}] ${data}`);
  });

  es.addEventListener('error', () => {
    statusEl.textContent = 'ошибка / переподключение';
    appendStreamEntry(
      logEl,
      'stream-error',
      'Ошибка соединения SSE — браузер попробует переподключиться автоматически. Если это повторяется, проверьте, что активный кейс существует и принадлежит текущей сессии.'
    );
  });
});

/* ---------- active case: complete / feedback ---------- */

function selectValue(id) {
  const v = $(id).value;
  return v === '' ? undefined : v;
}

function selectBool(id) {
  const v = $(id).value;
  return v === '' ? undefined : v === 'true';
}

$('btn-complete-case').addEventListener('click', async () => {
  const id = requireActiveCase();
  if (!id) return;
  const e = await apiRequest('POST', `/api/v0/cases/${encodeURIComponent(id)}/complete`, {
    body: { solved: selectBool('complete-solved') },
  });
  renderResult($('result-complete-case'), e);
});

$('btn-submit-feedback').addEventListener('click', async () => {
  const id = requireActiveCase();
  if (!id) return;
  const idem = $('fb-idem').value.trim() || undefined;
  const e = await apiRequest('POST', `/api/v0/cases/${encodeURIComponent(id)}/feedback`, {
    body: {
      specialist_rating: selectValue('fb-specialist'),
      information_quality_rating: selectValue('fb-quality'),
      solved: selectBool('fb-solved'),
      comment_text: $('fb-comment').value.trim() || undefined,
    },
    idempotencyKey: idem,
  });
  renderResult($('result-feedback'), e);
});

/* ---------- handoff ---------- */

$('btn-handoff-prepare').addEventListener('click', async () => {
  const id = requireActiveCase();
  if (!id) return;
  const e = await apiRequest('POST', `/api/v0/cases/${encodeURIComponent(id)}/handoff/prepare`);
  renderResult($('result-handoff-prepare'), e);
});

$('btn-handoff-confirm').addEventListener('click', async () => {
  const id = requireActiveCase();
  if (!id) return;
  const summary = $('handoff-confirm-summary').value;
  if (!summary.trim()) {
    window.alert('Введите summary.');
    return;
  }
  const idem = $('handoff-confirm-idem').value.trim() || undefined;
  const e = await apiRequest('POST', `/api/v0/cases/${encodeURIComponent(id)}/handoff/confirm`, {
    body: { summary },
    idempotencyKey: idem,
  });
  renderResult($('result-handoff-confirm'), e);
});

$('btn-handoff-retry').addEventListener('click', async () => {
  const id = requireActiveCase();
  if (!id) return;
  const summary = $('handoff-retry-summary').value;
  if (!summary.trim()) {
    window.alert('Введите summary.');
    return;
  }
  const idem = $('handoff-retry-idem').value.trim() || undefined;
  const e = await apiRequest('POST', `/api/v0/cases/${encodeURIComponent(id)}/handoff/retry`, {
    body: { summary },
    idempotencyKey: idem,
  });
  renderResult($('result-handoff-retry'), e);
});

/* ---------- notifications ---------- */

$('btn-list-notifications').addEventListener('click', async () => {
  const after = $('notif-after').value || '0';
  const unread = $('notif-unread').checked ? 'true' : undefined;
  const e = await apiRequest('GET', '/api/v0/notifications', { query: { after, unread } });
  renderResult($('result-list-notifications'), e);
});

$('btn-ack-notifications').addEventListener('click', async () => {
  const raw = $('notif-ack-ids').value;
  const ids = raw.split(/[\s,]+/).map((s) => s.trim()).filter(Boolean);
  if (!ids.length) {
    window.alert('Укажите хотя бы один id.');
    return;
  }
  const e = await apiRequest('POST', '/api/v0/notifications/ack', { body: { ids } });
  renderResult($('result-ack-notifications'), e);
});

$('btn-toggle-notif-stream').addEventListener('click', () => {
  const btn = $('btn-toggle-notif-stream');
  const statusEl = $('notif-stream-status');
  const logEl = $('notif-stream-log');

  if (state.notifStream) {
    state.notifStream.close();
    state.notifStream = null;
    statusEl.textContent = 'отключено';
    btn.textContent = 'Подключить /notifications/stream';
    appendStreamEntry(logEl, 'stream-info', 'Соединение закрыто вручную.');
    return;
  }

  const url = (state.baseUrl || '') + '/api/v0/notifications/stream';
  const es = new EventSource(url, { withCredentials: true });
  state.notifStream = es;
  statusEl.textContent = 'подключение...';
  btn.textContent = 'Отключить поток';

  es.addEventListener('open', () => {
    statusEl.textContent = 'подключено';
    appendStreamEntry(logEl, 'stream-info', `Открыто соединение: ${url}`);
  });

  es.addEventListener('notification', (ev) => {
    let data = ev.data;
    try { data = JSON.stringify(JSON.parse(ev.data)); } catch { /* leave raw */ }
    appendStreamEntry(logEl, 'stream-data', `[id=${ev.lastEventId}] ${data}`);
  });

  es.addEventListener('error', () => {
    statusEl.textContent = 'ошибка / переподключение';
    appendStreamEntry(
      logEl,
      'stream-error',
      'Ошибка соединения SSE — если сессия ещё не создана (нет owner_id cookie), сервер вернёт 401 и поток не откроется. Сначала создайте сессию или кейс.'
    );
  });
});

/* ---------- sources ---------- */

$('btn-get-source').addEventListener('click', async () => {
  const fragmentId = $('source-fragment-id').value.trim();
  if (!fragmentId) {
    window.alert('Введите fragment_id.');
    return;
  }
  const e = await apiRequest('GET', `/api/v0/sources/${encodeURIComponent(fragmentId)}`);
  renderResult($('result-source'), e);
});

/* ---------- analytics ---------- */

$('btn-get-evaluations').addEventListener('click', async () => {
  const caseId = $('eval-case-id').value.trim() || state.activeCaseId;
  if (!caseId) {
    window.alert('Укажите case_id или установите активный кейс.');
    return;
  }
  const e = await apiRequest('GET', '/api/v0/analytics/evaluations', { query: { case_id: caseId } });
  renderResult($('result-evaluations'), e);
});

$('btn-get-issue-groups').addEventListener('click', async () => {
  const e = await apiRequest('GET', '/api/v0/analytics/issue-groups');
  renderResult($('result-issue-groups'), e);
});
