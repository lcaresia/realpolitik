// Loja do Realpolitik: checkout Stripe, licenças e downloads. Cloudflare Worker (ES module), sem dependências.
// Documentação: _Modding\docs\loja.md. Bindings: DB (D1), FILES (R2). Segredos: ver docs.

const LICENSE_LIMIT = 3;           // PCs ativos ao mesmo tempo
const DEACT_LIMIT = 3;             // "liberar este PC" por janela
const DEACT_WINDOW = 30 * 86400;   // 30 dias
const ORDER_VIEW_WINDOW = 2 * 86400; // a página "obrigado" mostra a chave por 48 h após a compra
const CODE_TTL = 5 * 60;           // código de uso único do jogo
const MAIL_CODE_TTL = 30 * 60;     // código mandado por e-mail
const DL_TTL = 10 * 60;            // link de download assinado
const now = () => Math.floor(Date.now() / 1000);

export default {
  async fetch(req, env, ctx) {
    const url = new URL(req.url);
    const cors = corsHeaders(req, env);
    if (req.method === 'OPTIONS') return new Response(null, { status: 204, headers: cors });
    try {
      const r = await route(req, env, url, ctx);
      for (const [k, v] of Object.entries(cors)) r.headers.set(k, v);
      return r;
    } catch (e) {
      console.error('erro', e && e.stack || e);
      return json({ error: 'internal' }, 500, cors);
    }
  },
};

async function route(req, env, url, ctx) {
  const p = url.pathname, m = req.method;
  if (p === '/api/webhook' && m === 'POST') return webhook(req, env);
  if (p === '/api/checkout' && m === 'POST') return limited(req, env, 'checkout', 20, () => checkout(req, env));
  if (p === '/api/order' && m === 'GET') return limited(req, env, 'order', 60, () => order(env, url));
  if (p === '/api/license/activate' && m === 'POST') return limited(req, env, 'lic', 30, () => activate(req, env));
  if (p === '/api/license/validate' && m === 'POST') return limited(req, env, 'lic', 30, () => validate(req, env));
  if (p === '/api/license/deactivate' && m === 'POST') return limited(req, env, 'lic', 30, () => deactivate(req, env));
  if (p === '/api/download/code' && m === 'POST') return limited(req, env, 'lic', 30, () => downloadCode(req, env));
  if (p === '/api/download/list' && m === 'POST') return limited(req, env, 'dl', 30, () => downloadList(req, env));
  if (p === '/api/resend' && m === 'POST') return limited(req, env, 'resend', 5, () => resend(req, env, ctx));
  if (p === '/api/recover' && m === 'POST') return limited(req, env, 'recover', 5, () => recover(req, env));
  if (p === '/api/version' && m === 'GET') return version(env);
  if (p.startsWith('/dl/') && m === 'GET') return serveFile(env, url);
  if (p === '/api/admin/order' && m === 'GET') return adminOrder(req, env, url);
  return json({ error: 'not_found' }, 404);
}

// ---------- Checkout ----------

async function checkout(req, env) {
  const body = await safeJson(req);
  const lang = ['en', 'pt', 'es', 'fr', 'de'].includes(body.lang) ? body.lang : 'en';
  const site = env.SITE_URL.replace(/\/$/, '');
  const form = {
    mode: 'payment',
    'line_items[0][price]': env.STRIPE_PRICE_ID,
    'line_items[0][quantity]': '1',
    success_url: `${site}/obrigado.html?session_id={CHECKOUT_SESSION_ID}&lang=${lang}`,
    cancel_url: `${site}/?lang=${lang}`,
    locale: lang === 'pt' ? 'pt-BR' : lang,
    customer_creation: 'always',
    'payment_intent_data[description]': 'Realpolitik: Living Nations for HUMANKIND',
    allow_promotion_codes: 'true',
  };
  if (env.STRIPE_AUTOMATIC_TAX === 'true') form['automatic_tax[enabled]'] = 'true';
  const s = await stripe(env, 'POST', '/v1/checkout/sessions', form);
  return json({ url: s.url });
}

// ---------- Webhook da Stripe ----------

async function webhook(req, env) {
  const raw = await req.text();
  const ok = await verifyStripeSignature(raw, req.headers.get('stripe-signature') || '', env.STRIPE_WEBHOOK_SECRET);
  if (!ok) return json({ error: 'bad_signature' }, 400);
  const ev = JSON.parse(raw);
  const seen = await env.DB.prepare('SELECT id FROM events WHERE id = ?').bind(ev.id).first();
  if (seen) return json({ ok: true, duplicate: true });

  const o = ev.data && ev.data.object;
  switch (ev.type) {
    case 'checkout.session.completed':
    case 'checkout.session.async_payment_succeeded':
      if (o.payment_status === 'paid') await fulfill(env, o);
      break;
    case 'charge.refunded':
      await setStatusByPI(env, o.payment_intent, 'refunded');
      break;
    case 'charge.dispute.created':
      await setStatusByPI(env, o.payment_intent, 'disputed');
      break;
    case 'charge.dispute.closed':
      // Disputa ganha pelo vendedor: a chave volta a valer.
      if (o.status === 'won') await setStatusByPI(env, o.payment_intent, 'active', 'disputed');
      break;
  }
  await env.DB.prepare('INSERT OR IGNORE INTO events (id, at) VALUES (?, ?)').bind(ev.id, now()).run();
  return json({ ok: true });
}

async function fulfill(env, session) {
  const exists = await env.DB.prepare('SELECT id FROM orders WHERE session_id = ?').bind(session.id).first();
  if (exists) return;
  let chargeId = null, fp = null;
  // Cartão e cobrança só servem para achar reembolsos repetidos: se a Stripe falhar aqui, a chave sai mesmo assim.
  if (session.payment_intent) {
    try {
      const pi = await stripe(env, 'GET', `/v1/payment_intents/${session.payment_intent}?expand[]=latest_charge`);
      const ch = pi.latest_charge;
      if (ch && typeof ch === 'object') {
        chargeId = ch.id;
        fp = ch.payment_method_details && ch.payment_method_details.card ? ch.payment_method_details.card.fingerprint : null;
      }
    } catch (e) { console.error('payment_intent', e.message); }
  }
  const email = ((session.customer_details && session.customer_details.email) || session.customer_email || '').toLowerCase() || null;
  const prior = await env.DB.prepare(
    "SELECT COUNT(*) AS n FROM orders WHERE status IN ('refunded','disputed') AND ((email IS NOT NULL AND email = ?) OR (card_fp IS NOT NULL AND card_fp = ?))"
  ).bind(email, fp).first();

  const key = newLicenseKey();
  const t = now();
  await env.DB.prepare(
    `INSERT INTO orders (session_id, payment_intent, charge_id, email, card_fp, amount, currency, status, key_hash, key_enc, prior_refund, created_at, updated_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, 'active', ?, ?, ?, ?, ?)`
  ).bind(session.id, session.payment_intent || null, chargeId, email, fp, session.amount_total || null, session.currency || null,
    await keyHash(env, key), await encrypt(env, key), prior && prior.n > 0 ? 1 : 0, t, t).run();
  // O e-mail com a chave sai quando houver serviço de e-mail configurado (RESEND_API_KEY).
  if (email && env.RESEND_API_KEY) {
    const o = await env.DB.prepare('SELECT id FROM orders WHERE session_id = ?').bind(session.id).first();
    await sendKeyMail(env, email, key, await newCode(env, o.id, MAIL_CODE_TTL));
  }
}

async function setStatusByPI(env, pi, status, onlyFrom) {
  if (!pi) return;
  const sql = onlyFrom
    ? 'UPDATE orders SET status = ?, updated_at = ? WHERE payment_intent = ? AND status = ?'
    : 'UPDATE orders SET status = ?, updated_at = ? WHERE payment_intent = ?';
  const st = env.DB.prepare(sql);
  await (onlyFrom ? st.bind(status, now(), pi, onlyFrom) : st.bind(status, now(), pi)).run();
}

// ---------- Página "obrigado" ----------

async function order(env, url) {
  const sid = url.searchParams.get('session_id') || '';
  if (!/^cs_(test|live)_[A-Za-z0-9]+$/.test(sid)) return json({ error: 'bad_session' }, 400);
  const o = await env.DB.prepare('SELECT * FROM orders WHERE session_id = ?').bind(sid).first();
  if (!o) return json({ pending: true }, 202); // o webhook ainda não chegou; a página tenta de novo
  if (now() - o.created_at > ORDER_VIEW_WINDOW) return json({ error: 'expired' }, 410);
  if (o.status !== 'active') return json({ error: o.status }, 403);
  return json({ key: await decrypt(env, o.key_enc), code: await newCode(env, o.id, CODE_TTL), prior_refund: !!o.prior_refund });
}

// ---------- Licença (chamado pelo jogo) ----------

async function findOrder(env, key) {
  if (typeof key !== 'string' || key.length < 10 || key.length > 64) return null;
  return env.DB.prepare('SELECT * FROM orders WHERE key_hash = ?').bind(await keyHash(env, normalizeKey(key))).first();
}

async function activate(req, env) {
  const b = await safeJson(req);
  const o = await findOrder(env, b.key);
  if (!o) return json({ activated: false, error: 'invalid_key' }, 404);
  if (o.status !== 'active') return json({ activated: false, error: o.status }, 403);
  const name = String(b.instance_name || 'PC').slice(0, 64);
  const t = now();
  const same = await env.DB.prepare('SELECT id FROM activations WHERE order_id = ? AND name = ? AND active = 1').bind(o.id, name).first();
  if (same) {
    await env.DB.prepare('UPDATE activations SET last_seen = ? WHERE id = ?').bind(t, same.id).run();
    return json({ activated: true, instance_id: same.id, ...(await usage(env, o.id)) });
  }
  const u = await usage(env, o.id);
  if (u.usage >= LICENSE_LIMIT) return json({ activated: false, error: 'limit_reached', ...u }, 409);
  const id = 'ins_' + randomToken(16);
  await env.DB.prepare('INSERT INTO activations (id, order_id, name, active, created_at, last_seen) VALUES (?, ?, ?, 1, ?, ?)')
    .bind(id, o.id, name, t, t).run();
  return json({ activated: true, instance_id: id, ...(await usage(env, o.id)) });
}

async function validate(req, env) {
  const b = await safeJson(req);
  const o = await findOrder(env, b.key);
  if (!o) return json({ valid: false, error: 'invalid_key' }, 404);
  if (o.status !== 'active') return json({ valid: false, error: o.status }, 403);
  const a = await env.DB.prepare('SELECT id FROM activations WHERE id = ? AND order_id = ? AND active = 1').bind(String(b.instance_id || ''), o.id).first();
  if (!a) return json({ valid: false, error: 'instance_not_found' }, 404);
  await env.DB.prepare('UPDATE activations SET last_seen = ? WHERE id = ?').bind(now(), a.id).run();
  return json({ valid: true, ...(await usage(env, o.id)) });
}

async function deactivate(req, env) {
  const b = await safeJson(req);
  const o = await findOrder(env, b.key);
  if (!o) return json({ deactivated: false, error: 'invalid_key' }, 404);
  const a = await env.DB.prepare('SELECT id FROM activations WHERE id = ? AND order_id = ? AND active = 1').bind(String(b.instance_id || ''), o.id).first();
  if (!a) return json({ deactivated: false, error: 'instance_not_found' }, 404);
  const t = now();
  const recent = await env.DB.prepare('SELECT COUNT(*) AS n FROM deactivations WHERE order_id = ? AND at > ?').bind(o.id, t - DEACT_WINDOW).first();
  if (recent.n >= DEACT_LIMIT) return json({ deactivated: false, error: 'deactivation_limit' }, 429);
  await env.DB.batch([
    env.DB.prepare('UPDATE activations SET active = 0 WHERE id = ?').bind(a.id),
    env.DB.prepare('INSERT INTO deactivations (order_id, at) VALUES (?, ?)').bind(o.id, t),
  ]);
  return json({ deactivated: true, ...(await usage(env, o.id)) });
}

async function usage(env, orderId) {
  const r = await env.DB.prepare('SELECT COUNT(*) AS n FROM activations WHERE order_id = ? AND active = 1').bind(orderId).first();
  return { usage: r.n, limit: LICENSE_LIMIT };
}

// ---------- Downloads ----------

// O jogo troca a chave por um código de uso único e abre /download#t=CODIGO.
async function downloadCode(req, env) {
  const b = await safeJson(req);
  const o = await findOrder(env, b.key);
  if (!o) return json({ error: 'invalid_key' }, 404);
  if (o.status !== 'active') return json({ error: o.status }, 403);
  return json({ code: await newCode(env, o.id, CODE_TTL), expires_in: CODE_TTL });
}

// A página de download manda o código (ou a chave colada) e recebe os links assinados.
async function downloadList(req, env) {
  const b = await safeJson(req);
  let o = null;
  if (b.code) {
    const h = await sha256hex(String(b.code));
    const c = await env.DB.prepare('SELECT * FROM codes WHERE code_hash = ?').bind(h).first();
    if (!c || c.used || c.expires_at < now()) return json({ error: 'code_invalid' }, 403);
    await env.DB.prepare('UPDATE codes SET used = 1 WHERE code_hash = ?').bind(h).run();
    o = await env.DB.prepare('SELECT * FROM orders WHERE id = ?').bind(c.order_id).first();
  } else if (b.key) {
    o = await findOrder(env, b.key);
  }
  if (!o) return json({ error: 'invalid_key' }, 404);
  if (o.status !== 'active') return json({ error: o.status }, 403);
  const man = await manifest(env);
  if (!man) return json({ error: 'no_releases' }, 503);
  const exp = now() + DL_TTL;
  const base = new URL(req.url).origin;
  const versions = [];
  for (const v of man.versions || []) {
    const path = `releases/${v.version}/${v.file}`;
    versions.push({ ...v, url: `${base}/dl/${path}?exp=${exp}&sig=${await hmacHex(env.DL_SECRET, path + '|' + exp)}` });
  }
  return json({ latest: man.latest, versions });
}

async function serveFile(env, url) {
  const path = decodeURIComponent(url.pathname.slice(4));
  const exp = parseInt(url.searchParams.get('exp') || '0', 10);
  const sig = url.searchParams.get('sig') || '';
  if (!path.startsWith('releases/') || path.includes('..') || exp < now()) return new Response('Link expired', { status: 403 });
  if (!timingSafeEqual(sig, await hmacHex(env.DL_SECRET, path + '|' + exp))) return new Response('Bad link', { status: 403 });
  const obj = env.FILES ? await env.FILES.get(path) : null;
  if (!obj) return new Response('Not found', { status: 404 });
  const h = new Headers();
  obj.writeHttpMetadata(h);
  h.set('content-disposition', `attachment; filename="${path.split('/').pop()}"`);
  h.set('cache-control', 'private, no-store');
  return new Response(obj.body, { headers: h });
}

async function version(env) {
  const man = await manifest(env);
  if (!man) return json({ latest: null });
  const v = (man.versions || []).find(x => x.version === man.latest) || {};
  return json({ latest: man.latest, testedGame: v.testedGame || null, date: v.date || null, notes: v.notes || null, brokenOn: man.brokenOn || [] },
    200, { 'cache-control': 'public, max-age=300' });
}

async function manifest(env) {
  if (!env.FILES) return null; // R2 ainda não ligado
  const obj = await env.FILES.get('releases/manifest.json');
  return obj ? obj.json() : null;
}

// ---------- Recuperar a chave sem e-mail nosso: e-mail da compra + número do recibo da Stripe ----------
// O recibo (e-mail que a própria Stripe manda) só chega na caixa do comprador, então e-mail + número provam a compra.

async function recover(req, env) {
  const b = await safeJson(req);
  const email = String(b.email || '').trim().toLowerCase();
  const receipt = String(b.receipt || '').toUpperCase().replace(/[^0-9A-Z]/g, '');
  if (!email || receipt.length < 6) return json({ error: 'not_found' }, 404);
  const rows = (await env.DB.prepare('SELECT * FROM orders WHERE email = ? ORDER BY id DESC LIMIT 5').bind(email).all()).results;
  for (const o of rows) {
    let num = null;
    try {
      if (o.charge_id) num = (await stripe(env, 'GET', `/v1/charges/${o.charge_id}`)).receipt_number;
      else if (o.payment_intent) {
        const pi = await stripe(env, 'GET', `/v1/payment_intents/${o.payment_intent}?expand[]=latest_charge`);
        num = pi.latest_charge && pi.latest_charge.receipt_number;
      }
    } catch (e) { console.error('recover', e.message); }
    if (!num || num.toUpperCase().replace(/[^0-9A-Z]/g, '') !== receipt) continue;
    if (o.status !== 'active') return json({ error: o.status }, 403);
    return json({ key: await decrypt(env, o.key_enc), code: await newCode(env, o.id, CODE_TTL) });
  }
  return json({ error: 'not_found' }, 404);
}

// ---------- Reenviar chave por e-mail (só com RESEND_API_KEY; hoje desligado) ----------

async function resend(req, env, ctx) {
  const b = await safeJson(req);
  const email = String(b.email || '').trim().toLowerCase();
  // Resposta sempre igual: não revela se o e-mail comprou ou não.
  if (email && env.RESEND_API_KEY) {
    ctx.waitUntil((async () => {
      const o = await env.DB.prepare("SELECT * FROM orders WHERE email = ? AND status = 'active' ORDER BY id DESC LIMIT 1").bind(email).first();
      if (o) await sendKeyMail(env, email, await decrypt(env, o.key_enc), await newCode(env, o.id, MAIL_CODE_TTL));
    })());
  }
  return json({ ok: true, mail_enabled: !!env.RESEND_API_KEY });
}

async function sendKeyMail(env, to, key, code) {
  const site = env.SITE_URL.replace(/\/$/, '');
  const link = `${site}/download.html#t=${code}`;
  const text = `Thanks for buying Realpolitik: Living Nations for HUMANKIND.\n\nYour license key: ${key}\n\nDownload (link valid for 30 minutes): ${link}\nAfter that, use your key at ${site}/download.html\n\nPaste the key in the game: main menu > AI Diplomacy > License.`;
  await fetch('https://api.resend.com/emails', {
    method: 'POST',
    headers: { authorization: `Bearer ${env.RESEND_API_KEY}`, 'content-type': 'application/json' },
    body: JSON.stringify({ from: env.MAIL_FROM, to: [to], subject: 'Your Realpolitik license key', text }),
  });
}

// ---------- Admin (consulta manual) ----------

async function adminOrder(req, env, url) {
  const auth = req.headers.get('authorization') || '';
  if (!env.ADMIN_TOKEN || !timingSafeEqual(auth, 'Bearer ' + env.ADMIN_TOKEN)) return json({ error: 'unauthorized' }, 401);
  const email = (url.searchParams.get('email') || '').toLowerCase();
  const rows = (await env.DB.prepare('SELECT id, session_id, payment_intent, email, status, prior_refund, created_at FROM orders WHERE email = ?').bind(email).all()).results;
  for (const r of rows) {
    r.activations = (await env.DB.prepare('SELECT id, name, active, created_at, last_seen FROM activations WHERE order_id = ?').bind(r.id).all()).results;
  }
  return json({ orders: rows });
}

// ---------- Utilitários ----------

async function stripe(env, method, path, form) {
  const init = { method, headers: { authorization: `Bearer ${env.STRIPE_SECRET_KEY}` } };
  if (form) {
    init.headers['content-type'] = 'application/x-www-form-urlencoded';
    init.body = new URLSearchParams(form).toString();
  }
  const r = await fetch('https://api.stripe.com' + path, init);
  const j = await r.json();
  if (!r.ok) throw new Error(`stripe ${r.status}: ${j.error && j.error.message}`);
  return j;
}

async function verifyStripeSignature(payload, header, secret) {
  const parts = Object.fromEntries(header.split(',').map(kv => kv.split('=')).filter(x => x.length === 2).map(([k, v]) => [k.trim(), v]));
  const sigs = header.split(',').filter(x => x.startsWith('v1=')).map(x => x.slice(3));
  const t = parseInt(parts.t || '0', 10);
  if (!t || !sigs.length || Math.abs(now() - t) > 300) return false;
  const expected = await hmacHex(secret, `${t}.${payload}`);
  return sigs.some(s => timingSafeEqual(s, expected));
}

async function limited(req, env, bucket, perMinute, fn) {
  const ip = req.headers.get('cf-connecting-ip') || 'unknown';
  const win = Math.floor(now() / 60);
  const k = `${bucket}:${ip}`;
  const row = await env.DB.prepare(
    'INSERT INTO ratelimit (k, n, win) VALUES (?, 1, ?) ON CONFLICT(k) DO UPDATE SET n = CASE WHEN win = excluded.win THEN n + 1 ELSE 1 END, win = excluded.win RETURNING n'
  ).bind(k, win).first();
  if (row && row.n > perMinute) return json({ error: 'rate_limited' }, 429);
  return fn();
}

const ALPHABET = '0123456789ABCDEFGHJKMNPQRSTVWXYZ'; // Crockford base32 (sem I, L, O, U)
function newLicenseKey() {
  const b = crypto.getRandomValues(new Uint8Array(20));
  let s = '';
  for (let i = 0; i < 20; i++) s += ALPHABET[b[i] & 31];
  return 'RPLN-' + s.match(/.{5}/g).join('-');
}
function normalizeKey(k) {
  return String(k).trim().toUpperCase().replace(/[^0-9A-Z]/g, '').replace(/^RPLN/, '').replace(/I|L/g, '1').replace(/O/g, '0')
    .replace(/(.{5})(?=.)/g, '$1-').replace(/^/, 'RPLN-');
}

async function newCode(env, orderId, ttl) {
  const code = randomToken(24);
  await env.DB.prepare('INSERT INTO codes (code_hash, order_id, expires_at, used) VALUES (?, ?, ?, 0)')
    .bind(await sha256hex(code), orderId, now() + ttl).run();
  return code;
}

function randomToken(n) {
  return [...crypto.getRandomValues(new Uint8Array(n))].map(x => x.toString(16).padStart(2, '0')).join('');
}

async function keyHash(env, key) { return hmacHex(env.KEY_HMAC_SECRET, key); }

async function hmacHex(secret, msg) {
  const k = await crypto.subtle.importKey('raw', new TextEncoder().encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  const s = await crypto.subtle.sign('HMAC', k, new TextEncoder().encode(msg));
  return [...new Uint8Array(s)].map(x => x.toString(16).padStart(2, '0')).join('');
}

async function sha256hex(s) {
  const d = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(s));
  return [...new Uint8Array(d)].map(x => x.toString(16).padStart(2, '0')).join('');
}

async function aesKey(env) {
  const raw = Uint8Array.from(atob(env.KEY_ENC_SECRET), c => c.charCodeAt(0));
  return crypto.subtle.importKey('raw', raw, 'AES-GCM', false, ['encrypt', 'decrypt']);
}
async function encrypt(env, text) {
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const ct = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, await aesKey(env), new TextEncoder().encode(text));
  return b64(iv) + '.' + b64(new Uint8Array(ct));
}
async function decrypt(env, blob) {
  const [iv, ct] = blob.split('.').map(unb64);
  return new TextDecoder().decode(await crypto.subtle.decrypt({ name: 'AES-GCM', iv }, await aesKey(env), ct));
}
const b64 = u => btoa(String.fromCharCode(...u));
const unb64 = s => Uint8Array.from(atob(s), c => c.charCodeAt(0));

function timingSafeEqual(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string' || a.length !== b.length) return false;
  let d = 0;
  for (let i = 0; i < a.length; i++) d |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return d === 0;
}

async function safeJson(req) {
  try { return await req.json(); } catch { return {}; }
}

function corsHeaders(req, env) {
  const origin = req.headers.get('origin') || '';
  const allowed = (env.ALLOWED_ORIGINS || '').split(',').map(s => s.trim()).filter(Boolean);
  if (!allowed.includes(origin)) return {};
  return { 'access-control-allow-origin': origin, 'access-control-allow-methods': 'GET, POST, OPTIONS', 'access-control-allow-headers': 'content-type', vary: 'origin' };
}

function json(obj, status = 200, extra = {}) {
  return new Response(JSON.stringify(obj), { status, headers: { 'content-type': 'application/json; charset=utf-8', ...extra } });
}
