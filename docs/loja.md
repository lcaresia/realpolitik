# Loja própria: Stripe + Cloudflare

Como a venda do Realpolitik funciona, onde está cada peça e como operar. Decidido em 2026-10-06 depois que a Lemon
Squeezy recusou ativar a loja (a verificação de identidade dela passa pela Stripe Connect, indisponível no Brasil).

## Visão geral

```
Site (Pages) ── "Comprar" ──► Worker /api/checkout ──► Stripe Checkout (página da Stripe; cartão só lá)
                                                               │ pago
Stripe ── webhook assinado ──► Worker /api/webhook ── gera a chave, grava no D1
Comprador ◄── obrigado.html (chave + download)   ·   download.html (chave colada, link #t= do jogo ou do e-mail)
Jogo ── activate / validate / deactivate / download/code / version ──► Worker
Reembolso ou contestação na Stripe ── webhook ──► chave desligada
```

| Peça | Onde |
|---|---|
| Worker | `realpolitik-loja` → https://realpolitik-loja.lcaresia.workers.dev · código `_Modding\loja\worker\worker.js` |
| Banco D1 | `realpolitik-loja` · esquema `_Modding\loja\worker\schema.sql` |
| Arquivos (R2) | bucket `realpolitik-releases` (`releases/manifest.json` + `releases/<versão>/<arquivo>`) |
| Stripe | produto + preço US$ 10 + webhook (ids em `_Modding\loja\config.json`) |
| Páginas | `_Modding\site\live\obrigado.html`, `download.html`, `store.js`, `store.css` |
| Briefing do jogo | `docs\integracao-licenca.md` |

## Segredos (`_Modding\.env`, nunca no git/backup/site)

| Nome | Para quê |
|---|---|
| `STRIPE_API_KEY` (ou `STRIPE_SECRET_KEY`) | chave secreta da Stripe; o deploy usa `STRIPE_SECRET_KEY` se existir |
| `STRIPE_WEBHOOK_SECRET_LIVE` / `_TEST` | assinatura do webhook (criado pelo deploy) |
| `LOJA_KEY_HMAC_SECRET` | "impressão digital" das chaves (busca) |
| `LOJA_KEY_ENC_SECRET` | AES-GCM da cópia criptografada das chaves (reenvio) |
| `LOJA_DL_SECRET` | assinatura dos links de download |
| `LOJA_ADMIN_TOKEN` | consulta `/api/admin/order` |
| `RESEND_API_KEY`, `LOJA_MAIL_FROM` | e-mail (opcional; sem eles, o reenvio por e-mail fica desligado) |

**Guarde uma cópia do `.env` num gerenciador de senhas.** Se `LOJA_KEY_HMAC_SECRET` ou `LOJA_KEY_ENC_SECRET` se
perderem, nenhuma chave já vendida volta a ser reconhecida. O backup (`tools\dev\backup.ps1`) não leva o `.env` de
propósito.

## Operar

- **Publicar mudanças no Worker / criar o que faltar:** `powershell -ExecutionPolicy Bypass -File _Modding\loja\tools\deploy-loja.ps1`
  (idempotente; cria D1, R2, produto/preço/webhook na Stripe só se faltarem; nunca imprime segredos).
- **Testar tudo sem cobrança:** `_Modding\loja\tools\testar-loja.ps1` (eventos assinados como a Stripe faria; apaga o
  que criou). 26 verificações; última execução 2026-10-06, todas OK.
- **Reembolsar:** pelo painel da Stripe (Pagamentos → reembolsar). O webhook desliga a chave sozinho.
- **Contestação (chargeback):** a chave desliga quando a contestação abre e volta se você ganhar. Para a defesa, a
  consulta admin mostra as datas de ativação e de último uso.
- **Consultar um cliente:** `GET /api/admin/order?email=...` com `Authorization: Bearer <LOJA_ADMIN_TOKEN>`.
- **Pedido de teste** (para ter uma chave no jogo sem pagar): `loja\tools\pedido-teste.ps1`.
  - `-Criar`: a chave vai para `dev\out\chave-teste.txt` e não é impressa.
  - `-Ocupar N`: ativa a chave em N PCs falsos.
  - `-Reembolsar`: manda o reembolso.
  - `-Apagar`: apaga tudo do D1. Sempre rode no fim.

  No jogo: `ia licenca ativar <chave>` (ver `docs\diplomacia-ia.md` §16).
- **Site:** `_Modding\site\tools\deploy-cloudflare.ps1`. O botão "Comprar" chama `/api/checkout`; se a Stripe recusar
  (conta ainda não ativada), mostra "a loja abre em breve". Ou seja: **o botão começa a vender sozinho quando a Stripe
  liberar a conta.**

## Regras anti-abuso (no Worker)

- 3 PCs ativos por chave; mesmo `instance_name` reativando não gasta vaga.
- "Liberar este PC": no máximo 3 a cada 30 dias.
- Reembolso/contestação desligam a chave; quem já teve reembolso (mesmo e-mail ou mesmo cartão) fica marcado
  `prior_refund` na compra seguinte (a página "obrigado" avisa que não há novo reembolso).
- Chaves de 100 bits aleatórios; limite de tentativas por IP em todas as rotas.
- Downloads: link assinado de 10 min; código de uso único de 5 min (jogo) ou 30 min (e-mail), passado depois do `#`
  e apagado do endereço pela página.

## Antes de vender (checklist)

1. **Stripe:** concluir a ativação da conta (hoje `charges_enabled=false`) e ligar cartões em
   Configurações → Métodos de pagamento. Sem isso o checkout responde "No valid payment method types".
2. ~~R2~~ ligado em 2026-10-06 (bucket `realpolitik-releases`).
3. **Primeira versão:** `loja\tools\publicar-versao.ps1 -Versao 1.0.0 -Arquivo <zip ou exe> -Notas "..." -JogoTestado 1.31.4836`.
   Sobe o arquivo, grava sha256/tamanho/data no `releases/manifest.json` e marca como mais recente. O script recusa zip
   com `.env`, `*.key`, `credenciais/` ou `_Modding/dev/`. Também: `-Listar`, `-Remover <versão>`,
   `-NaoMarcarComoUltima` (beta), `-JogoQuebrado <versões do jogo>` (vira `brokenOn` no `/api/version`).
   Testado com uma versão falsa: página, download e sha256 conferem; link adulterado ou com prazo esticado é recusado.
4. **Recibo da Stripe ligado** (Configurações → E-mails de clientes → "Pagamentos bem-sucedidos"). Sem e-mail próprio,
   a chave aparece só na página "obrigado" (com copiar e "salvar em .txt"); quem perder recupera em `download.html`
   com o e-mail da compra + o número do recibo que a Stripe manda (`POST /api/recover`). Grátis e sem domínio.
   E-mail próprio (Resend, precisa de domínio) fica opcional: `RESEND_API_KEY` + `LOJA_MAIL_FROM` no `.env`, deploy.
5. **Contato:** trocar `CONTACT_EMAIL_PLACEHOLDER` no site.
6. **Impostos:** você é o vendedor. VAT da UE e do Reino Unido valem desde a 1ª venda (OSS não-UE); Stripe Tax
   (0,5%/venda) calcula, mas o cadastro e o recolhimento são seus; IR no Brasil. Falar com um contador. Para ligar o
   cálculo no checkout: `STRIPE_AUTOMATIC_TAX=true` no deploy (e cadastrar os registros na Stripe Tax antes).
7. **Integração no jogo** (`docs\integracao-licenca.md`).
