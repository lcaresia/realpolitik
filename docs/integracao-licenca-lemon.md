> **OBSOLETO (2026-10-06):** a Lemon Squeezy foi descartada (não ativa loja de vendedor no Brasil). Use `docs\integracao-licenca.md` (Stripe + Cloudflare Worker).

# Integrar a chave de licença da Lemon Squeezy no mod

Briefing para uma sessão nova. Leia antes: `_Modding\README.md` (mapa do projeto), `docs\pesquisa-instalador-exe.md`
seção 8 (decisões sobre a chave) e `docs\guia-telas-nativas.md` (como fazer telas nativas e testar com hot-reload).

## O que já está decidido

- **Produto:** "Realpolitik: Living Nations for HUMANKIND", US$ 10, pagamento único, reembolso em 7 dias.
- **O que a chave libera:** só a **Diplomacia IA** (cartas, diários, conselho, Congresso, ações das nações LLM).
  Sem chave, todo o resto funciona normal: moeda própria, Banco Central (F8), pedágio/bloqueio, 16 impérios.
  Sem chave, as nações usam a IA nativa do jogo, como já acontece quando nenhum provedor responde.
- **Onde o comprador digita a chave:** dentro do jogo, na tela nativa "Diplomacia IA" do menu principal
  (`src\CurrencyMod\Diplomacia\UI\ProvidersScreen.cs`). Um bloco "Licença" no topo com: campo da chave, botão
  Ativar, estado ("Ativado neste PC — 1 de 3"), botão "Liberar este PC" (desativar).
- **Não é DRM.** É prova de compra. Nada de ofuscação pesada, nada que puna quem pagou. Uma trava que sai com Harmony
  em 10 minutos é aceitável.
- **Textos da UI** passam por `L.T(...)` e entram nos 5 idiomas (`src\CurrencyMod\Lang\*.json`); ver memória/README
  sobre a tradução.

## A loja (dados reais)

| Item | Valor |
|---|---|
| Store id | `491678` (`realpolitik.lemonsqueezy.com`) |
| Product id | `1418088` — **modo de teste** |
| Variant id | `2215525` — **modo de teste** |
| Licença | gerada a cada compra, **limite 3 ativações**, **sem expiração** |

**Atenção, modo de teste:** a loja ainda não foi ativada, então o produto existe só em *test mode*. Quando o dono
ativar a loja e copiar o produto para *live mode*, o **product_id e o variant_id mudam**. Por isso:
- aceite uma **lista** de product_ids válidos (teste + live), não um id fixo;
- confira sempre o **store_id** (esse não muda);
- quando o produto live existir, pegue o id novo pela API (abaixo) e acrescente na lista.

Chaves de teste: compras feitas no checkout em modo de teste (cartão `4242 4242 4242 4242`, qualquer data futura e CVC)
geram chaves reais de teste que funcionam na License API. Peça ao dono para fazer uma compra de teste e te passar a
chave, ou crie a compra você mesmo se ele autorizar.

## A License API

Documentação: https://docs.lemonsqueezy.com/api/license-api

- **Pública:** não usa a API key da loja. **Nunca** coloque a `LEMON_API_KEY` (do `_Modding\.env`) dentro da DLL;
  ela é só para scripts do dono.
- Requisições `POST`, corpo `application/x-www-form-urlencoded`, header `Accept: application/json`.
- Limite: 60 chamadas por minuto.

| Chamada | Parâmetros | Resposta principal |
|---|---|---|
| `POST https://api.lemonsqueezy.com/v1/licenses/activate` | `license_key`, `instance_name` | `activated` (bool), `error`, `license_key{status, activation_limit, activation_usage}`, `instance{id}`, `meta{store_id, product_id, variant_id, customer_name, customer_email}` |
| `POST https://api.lemonsqueezy.com/v1/licenses/validate` | `license_key`, `instance_id` | `valid` (bool), `error`, `license_key{status}`, `meta{…}` |
| `POST https://api.lemonsqueezy.com/v1/licenses/deactivate` | `license_key`, `instance_id` | `deactivated` (bool), `error` |

`license_key.status`: `inactive`, `active`, `expired`, `disabled`. Um reembolso deixa a chave `disabled`.

**Obrigatório:** depois do `activate`/`validate`, confira `meta.store_id == 491678` e `meta.product_id` na lista.
Sem isso, qualquer chave de qualquer loja Lemon Squeezy passa.

## Comportamento esperado

1. **Ativar:** `instance_name` = algo legível e sem dado pessoal (ex.: `"Realpolitik PC-" + hash curto do nome da
   máquina`). Guarde `license_key` + `instance_id` + data da última validação boa.
2. **Guardar:** criptografado com DPAPI, igual às chaves de API, em `BepInEx\config\credenciais\` (reuse
   `src\CurrencyMod\Diplomacia\Llm\Providers\Credentials.cs`; ele já chama `CryptProtectData` direto porque o Mono da
   Unity não garante o `ProtectedData`).
3. **Validar:** no início do jogo/carregamento, em segundo plano, sem travar a UI (a rede já é feita fora da thread
   principal nos provedores de IA; siga o mesmo padrão).
4. **Offline:** sem internet ou a API fora do ar → vale o último resultado bom, **sem prazo**. Só desliga se a Lemon
   Squeezy responder explicitamente que a chave é inválida, `disabled` ou `expired`, ou se o store/product não bater.
5. **Limite atingido** (`activation_limit` alcançado): mensagem clara dizendo para liberar um PC antigo, com link para
   "My Orders" (`https://app.lemonsqueezy.com/my-orders`).
6. **Liberar este PC:** chama `deactivate` e apaga a credencial local. Se offline, apaga local mesmo assim e avisa.
7. **Interface trocável:** a Lemon Squeezy está migrando para o Stripe Managed Payments, que não aceita vendedor no
   Brasil. Deixe a licença atrás de uma interface (ex.: `ILicenseProvider` com Activate/Validate/Deactivate) para
   trocar por Gumroad depois sem mexer na UI.
8. **Modo dev:** nesta máquina o dono precisa jogar e testar sem chave. Respeite uma opção de cfg ou a existência de
   `_Modding\dev` para pular a trava (documente a opção). O pacote de venda não leva `_Modding\dev`.
9. **Log:** nunca escreva a chave inteira no log do BepInEx; no máximo os 4 últimos caracteres.

## Onde ligar a trava

A Diplomacia IA já tem caminho "sem provedor → IA nativa assume". A trava deve entrar no mesmo ponto (sem licença =
como se não houvesse provedor), e a tela de provedores mostra o motivo ("Licença necessária para a Diplomacia IA").
Procure onde o mod decide se as nações LLM estão ativas (`IaConfig`, fila de provedores) antes de inventar outro.

## Como testar

- Hot-reload e comandos dev: `_Modding\tools\dev\devcmd.ps1` (ver memória/README). Sempre testar com 16 impérios.
- Casos: chave válida; chave de outra loja; chave inexistente; limite de 3 atingido; desativar e reativar; jogo
  offline (bloquear a rede) com ativação já feita; chave `disabled` (o dono pode desativar uma chave no painel em
  *Licenses*).
- Para conferir ids e chaves pela API administrativa (só scripts, nunca a DLL), a chave está em `_Modding\.env` como
  `LEMON_API_KEY`. Nunca imprima o valor. Exemplo:
  `GET https://api.lemonsqueezy.com/v1/products` e `GET https://api.lemonsqueezy.com/v1/license-keys` com
  `Authorization: Bearer <chave>` e `Accept: application/vnd.api+json`.

## Ao terminar

- Documente em `docs\diplomacia-ia.md` e `docs\instalacao.md` (como ativar, liberar PC, o que funciona sem chave).
- Atualize as perguntas do site se algo mudar (`_Modding\site\live\index.html`, FAQ "What do I get without a license
  key?").
- Faça backup com `tools\dev\backup.ps1`.
