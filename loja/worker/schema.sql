-- Banco D1 da loja (Realpolitik). Aplicado por tools\deploy-loja.ps1.
-- A chave de licença nunca fica em texto puro: key_hash (HMAC, para achar) + key_enc (AES-GCM, para reenviar).

CREATE TABLE IF NOT EXISTS orders (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  session_id    TEXT UNIQUE NOT NULL,       -- Stripe Checkout Session
  payment_intent TEXT,
  charge_id     TEXT,
  email         TEXT,
  card_fp       TEXT,                       -- impressão digital do cartão (Stripe), para achar reembolsos repetidos
  amount        INTEGER,
  currency      TEXT,
  status        TEXT NOT NULL DEFAULT 'active', -- active | refunded | disputed | revoked
  key_hash      TEXT UNIQUE NOT NULL,
  key_enc       TEXT NOT NULL,
  prior_refund  INTEGER NOT NULL DEFAULT 0, -- 1 = este cliente já teve reembolso antes (sem direito a outro)
  created_at    INTEGER NOT NULL,
  updated_at    INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS orders_email ON orders(email);
CREATE INDEX IF NOT EXISTS orders_fp ON orders(card_fp);
CREATE INDEX IF NOT EXISTS orders_pi ON orders(payment_intent);

CREATE TABLE IF NOT EXISTS activations (
  id          TEXT PRIMARY KEY,             -- instance_id devolvido ao jogo
  order_id    INTEGER NOT NULL,
  name        TEXT,                         -- apelido sem dado pessoal
  active      INTEGER NOT NULL DEFAULT 1,
  created_at  INTEGER NOT NULL,
  last_seen   INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS activations_order ON activations(order_id, active);

CREATE TABLE IF NOT EXISTS deactivations (
  order_id INTEGER NOT NULL,
  at       INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS deactivations_order ON deactivations(order_id, at);

-- Códigos de uso único para abrir a página de download (jogo → /download#t=CODIGO, e-mail).
CREATE TABLE IF NOT EXISTS codes (
  code_hash  TEXT PRIMARY KEY,
  order_id   INTEGER NOT NULL,
  expires_at INTEGER NOT NULL,
  used       INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS ratelimit (
  k      TEXT PRIMARY KEY,
  n      INTEGER NOT NULL,
  win    INTEGER NOT NULL
);

-- Eventos da Stripe já processados (o webhook pode chegar repetido).
CREATE TABLE IF NOT EXISTS events (
  id TEXT PRIMARY KEY,
  at INTEGER NOT NULL
);
