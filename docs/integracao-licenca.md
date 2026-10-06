# Integrar a chave de licença no mod (loja própria: Stripe + Cloudflare Worker)

> **Feito em 2026-10-06.** Como ficou: `docs\diplomacia-ia.md` §16. Mudança combinada com o usuário: a seção ficou
> mínima (campo + Ativar; com licença, só o estado + Liberar este PC). Não há botão "Verificar", porque a conferência é
> automática. Este briefing fica como histórico.

Briefing para uma sessão nova. **Substitui `integracao-licenca-lemon.md`** (a Lemon Squeezy foi descartada: não ativa
loja de vendedor no Brasil). Leia antes: `_Modding\README.md`, `docs\loja.md` (como a loja funciona) e
`docs\guia-telas-nativas.md` (telas nativas e hot-reload).

## O que já está decidido

- **Produto:** "Realpolitik: Living Nations for HUMANKIND", US$ 10, pagamento único, reembolso em 7 dias (uma vez por
  cliente). Pagamento pela Stripe; licenças e downloads no nosso Worker do Cloudflare.
- **O que a chave libera:** só a **Diplomacia IA**. Sem chave, todo o resto funciona (moeda, Banco Central F8,
  pedágio/bloqueio, 16 impérios) e as nações usam a IA nativa, como quando nenhum provedor responde.
- **Onde o comprador digita a chave:** na tela nativa "Diplomacia IA" do menu principal
  (`src\CurrencyMod\Diplomacia\UI\ProvidersScreen.cs`). Bloco "Licença" no topo: campo da chave, botão Ativar, estado
  ("Ativado neste PC — 1 de 3"), botão "Liberar este PC".
- **Não é DRM.** É prova de compra. Nada de ofuscação pesada.
- **Textos** por `L.T(...)` nos 5 idiomas (`src\CurrencyMod\Lang\*.json`).

## A API (Worker)

Base: `https://realpolitik-loja.lcaresia.workers.dev` (deixe numa constante; pode virar domínio próprio).
Tudo `POST` com JSON (`content-type: application/json`), resposta JSON. **Sem segredo nenhum no cliente.**
Limite: 30 chamadas/min por IP nas rotas de licença.

| Rota | Corpo | Sucesso | Erros (campo `error`, status HTTP) |
|---|---|---|---|
| `/api/license/activate` | `key`, `instance_name` | `{activated:true, instance_id, usage, limit}` | `invalid_key` 404 · `refunded`/`disputed`/`revoked` 403 · `limit_reached` 409 (traz `usage`, `limit`) |
| `/api/license/validate` | `key`, `instance_id` | `{valid:true, usage, limit}` | `invalid_key` 404 · `refunded`/`disputed`/`revoked` 403 · `instance_not_found` 404 |
| `/api/license/deactivate` | `key`, `instance_id` | `{deactivated:true, usage, limit}` | `invalid_key` 404 · `instance_not_found` 404 · `deactivation_limit` 429 (máx. 3 a cada 30 dias) |
| `/api/download/code` | `key` | `{code, expires_in:300}` | `invalid_key` 404 · `refunded`/… 403 |
| `GET /api/version` | — | `{latest, testedGame, date, notes, brokenOn:[]}` (`latest` null se não houver versão) | — |

Qualquer 429 com `rate_limited` = esperar e tentar de novo; 5xx ou rede fora = tratar como offline.

- A chave tem o formato `RPLN-XXXXX-XXXXX-XXXXX-XXXXX`. O servidor aceita minúsculas, espaços e sem traços
  (normaliza), e troca I/L por 1 e O por 0.
- `instance_name`: legível e sem dado pessoal, estável por PC (ex.: `"PC-" + 8 hex de um hash do MachineName`).
  Ativar de novo com o mesmo nome **não gasta vaga** (devolve o mesmo `instance_id`).

## Comportamento esperado no jogo

1. **Ativar:** guarde `key` + `instance_id` + data da última validação boa.
2. **Guardar:** criptografado com DPAPI em `BepInEx\config\credenciais\` (reuse
   `src\CurrencyMod\Diplomacia\Llm\Providers\Credentials.cs`).
3. **Validar** a cada partida carregada/iniciada, em segundo plano, sem travar a UI.
4. **Offline / Worker fora do ar:** vale o último resultado bom por até **14 dias**. Depois disso, a Diplomacia IA
   desliga até validar de novo (a IA precisa de internet de qualquer jeito). Só desliga **na hora** se o servidor
   responder `invalid_key`, `refunded`, `disputed`, `revoked` ou `instance_not_found`.
5. **Limite atingido (409):** mensagem dizendo para liberar um PC antigo no próprio jogo daquele PC; se o PC não existe
   mais, falar com o suporte.
6. **Liberar este PC:** chama `deactivate` e apaga a credencial local. Offline: apaga local mesmo assim e avisa que a
   vaga só volta quando o servidor souber. `deactivation_limit` (429): explicar o limite de 3 por 30 dias.
7. **Atualização:** no menu principal, `GET /api/version`; se `latest` > versão do mod, mostrar "Versão X disponível"
   com as `notes` e um botão **Baixar atualização**, que:
   - chama `POST /api/download/code` com a chave;
   - abre no navegador `https://realpolitik-living-nations.pages.dev/download.html#t=<code>` (o `#` é de propósito: o
     código nunca vai para o servidor como parte do endereço; ele vale 5 minutos e uma vez só).
   - Sem chave ativada: abrir `download.html` sem código (o comprador cola a chave lá).
   - Se o `testedGame` for diferente da versão do jogo instalada, avisar "versão do jogo não testada" (não bloqueia).
8. **Interface trocável:** `ILicenseProvider` (Activate/Validate/Deactivate/DownloadCode) para trocar de backend sem
   mexer na UI.
9. **Modo dev:** nesta máquina o dono joga sem chave. Respeite uma opção de cfg ou a existência de `_Modding\dev`
   (documente a opção). O pacote de venda não leva `_Modding\dev`.
10. **Log:** nunca a chave inteira no log do BepInEx; no máximo os 4 últimos caracteres.

## Onde ligar a trava

A Diplomacia IA já tem o caminho "sem provedor → IA nativa assume". A trava entra no mesmo ponto (sem licença = como se
não houvesse provedor), e a tela de provedores mostra o motivo. Procure onde o mod decide se as nações LLM estão
ativas (`IaConfig`, fila de provedores) antes de inventar outro.

## Como testar sem pagar

- `_Modding\loja\tools\testar-loja.ps1` cria pedidos falsos assinados como a Stripe faria, percorre tudo e apaga o que
  criou. Para ter uma chave de teste para o jogo, use o mesmo método (ver `docs\loja.md`, "Pedido de teste manual") e
  apague depois.
- Casos: chave válida; chave inventada; limite de 3; mesmo PC ativando de novo; liberar e reativar; 4ª liberação no
  mês; jogo offline (bloquear o host do Worker) com ativação já feita, antes e depois dos 14 dias; pedido reembolsado
  (evento `charge.refunded` assinado) desligando a IA na próxima validação.
- Hot-reload e comandos dev: `_Modding\tools\dev\devcmd.ps1`. Sempre testar com 16 impérios.

## Ao terminar

- Documente em `docs\diplomacia-ia.md` e `docs\instalacao.md` (ativar, liberar PC, o que funciona sem chave, offline).
- Faça backup com `tools\dev\backup.ps1`.
