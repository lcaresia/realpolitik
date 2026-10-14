// Realpolitik error reports: receives the gzip bundle the mod sends ("Report a problem" on the Realpolitik screen),
// stores it in R2 and answers with a short code the player quotes on Discord or GitHub.
//   POST /report                  body = gzip of the JSON bundle (application/gzip), at most 8 MB
//   GET  /admin/list?days=7       Authorization: Bearer <ADMIN_TOKEN>  -> recent reports
//   GET  /admin/get/<code>        Authorization: Bearer <ADMIN_TOKEN>  -> the gzip bundle
// Limits per IP (hashed with SALT, never stored in clear): 3 per minute and 20 per day.
// Bindings: REPORTS (R2 bucket), ADMIN_TOKEN and SALT (secrets). Old reports expire by the bucket lifecycle rule.

const MAX_BYTES = 8 * 1024 * 1024;
const PER_MINUTE = 3;
const PER_DAY = 20;
const CODE_ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';

export default {
  async fetch(req, env) {
    const url = new URL(req.url);
    try {
      if (req.method === 'POST' && url.pathname === '/report') return await receive(req, env);
      if (req.method === 'GET' && url.pathname.startsWith('/admin/')) return await admin(req, env, url);
      if (req.method === 'GET' && url.pathname === '/') return json({ ok: true, service: 'realpolitik-reports' });
      return json({ error: 'not found' }, 404);
    } catch (err) {
      return json({ error: 'server error' }, 500);
    }
  },
};

async function receive(req, env) {
  const declared = Number(req.headers.get('content-length') || 0);
  if (declared > MAX_BYTES) return json({ error: 'report too large' }, 413);

  const ip = req.headers.get('cf-connecting-ip') || 'unknown';
  const who = (await sha256(ip + '|' + env.SALT)).slice(0, 16);
  const now = new Date();
  const day = now.toISOString().slice(0, 10);
  const minute = now.toISOString().slice(0, 16).replace(/[-:T]/g, '');
  const minuteKey = `limits/minute/${minute}/${who}`;
  const dayKey = `limits/day/${day}/${who}`;
  const perMinute = await readCount(env, minuteKey);
  const perDay = await readCount(env, dayKey);
  if (perMinute >= PER_MINUTE) return json({ error: 'too many reports, try again in a minute' }, 429);
  if (perDay >= PER_DAY) return json({ error: 'daily limit reached, try again tomorrow' }, 429);

  const body = await req.arrayBuffer();
  if (body.byteLength === 0) return json({ error: 'empty report' }, 400);
  if (body.byteLength > MAX_BYTES) return json({ error: 'report too large' }, 413);
  const bytes = new Uint8Array(body);
  if (bytes[0] !== 0x1f || bytes[1] !== 0x8b) return json({ error: 'expected a gzip body' }, 400);

  const code = 'R-' + randomCode(6);
  const agent = (req.headers.get('user-agent') || '').slice(0, 60);
  await env.REPORTS.put(`reports/${day}/${code}.json.gz`, body, {
    httpMetadata: { contentType: 'application/gzip' },
    customMetadata: { who, agent, country: (req.cf && req.cf.country) || '' },
  });
  await env.REPORTS.put(minuteKey, String(perMinute + 1));
  await env.REPORTS.put(dayKey, String(perDay + 1));
  return json({ code });
}

async function admin(req, env, url) {
  const auth = req.headers.get('authorization') || '';
  if (!env.ADMIN_TOKEN || !(await sameSecret(auth, 'Bearer ' + env.ADMIN_TOKEN))) return json({ error: 'unauthorized' }, 401);

  if (url.pathname === '/admin/list') {
    const days = Math.min(90, Math.max(1, Number(url.searchParams.get('days') || 7)));
    const items = [];
    for (let i = 0; i < days; i++) {
      const day = new Date(Date.now() - i * 86400000).toISOString().slice(0, 10);
      let cursor;
      do {
        const page = await env.REPORTS.list({ prefix: `reports/${day}/`, cursor, include: ['customMetadata'] });
        for (const obj of page.objects) {
          items.push({
            code: obj.key.split('/').pop().replace('.json.gz', ''),
            day,
            uploaded: obj.uploaded,
            size: obj.size,
            agent: obj.customMetadata && obj.customMetadata.agent,
            country: obj.customMetadata && obj.customMetadata.country,
          });
        }
        cursor = page.truncated ? page.cursor : undefined;
      } while (cursor);
    }
    return json({ reports: items });
  }

  const match = url.pathname.match(/^\/admin\/get\/(R-[A-Z0-9]{6})$/);
  if (match) {
    for (let i = 0; i < 90; i++) {
      const day = new Date(Date.now() - i * 86400000).toISOString().slice(0, 10);
      const obj = await env.REPORTS.get(`reports/${day}/${match[1]}.json.gz`);
      if (obj) {
        return new Response(obj.body, { headers: { 'content-type': 'application/gzip', 'cache-control': 'no-store' } });
      }
    }
    return json({ error: 'report not found' }, 404);
  }
  return json({ error: 'not found' }, 404);
}

async function readCount(env, key) {
  const obj = await env.REPORTS.get(key);
  return obj ? Number(await obj.text()) || 0 : 0;
}

function randomCode(length) {
  const random = new Uint8Array(length);
  crypto.getRandomValues(random);
  let out = '';
  for (const value of random) out += CODE_ALPHABET[value % CODE_ALPHABET.length];
  return out;
}

async function sha256(text) {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('');
}

async function sameSecret(a, b) {
  const [x, y] = await Promise.all([sha256(a), sha256(b)]);
  let diff = 0;
  for (let i = 0; i < x.length; i++) diff |= x.charCodeAt(i) ^ y.charCodeAt(i);
  return diff === 0;
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' },
  });
}
