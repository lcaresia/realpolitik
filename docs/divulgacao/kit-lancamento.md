# Kit de lançamento: primeiros canais

Textos prontos para colar. Os posts estão em inglês porque o público é estrangeiro. As explicações estão em português.
Link da loja: https://realpolitik-living-nations.pages.dev

## 0. Antes de postar (bloqueia tudo)

### 0.1 EULA do HUMANKIND: risco real

Li o EULA da Steam (app 1124300). O licenciador é a AMPLITUDE Studios e vale a lei francesa. Isto não é parecer jurídico.

- **§9 Modding:** mods são permitidos, e nenhuma linha proíbe vendê-los. Mas:
  - o texto diz que a licença que você dá à Amplitude é gratuita "porque o modder quer compartilhar com a comunidade";
  - publicar um mod dá à Amplitude o direito de usar e distribuir esse mod;
  - o espírito da cláusula é não comercial.
- **§10 Conteúdo do usuário:** vídeos, prints e lives do jogo **não podem ser explorados comercialmente sem consentimento
  prévio por escrito** da Amplitude. Prints e vídeos usados para vender o mod (site, posts, live 24/7) caem aqui.
- **§2:** proíbe incorporar o jogo em outro produto comercial. O mod não inclui arquivos do jogo, mas um advogado
  da Amplitude pode ler isso de outra forma.

**O que eu recomendo:** mandar o e-mail da seção 7 pedindo consentimento **antes** de postar nos canais oficiais (Discord e
Games2Gether). Se responderem "sim", o risco some e isso vira argumento de venda. Se responderem "não", é melhor saber
antes de investir em divulgação. Os posts no Reddit podem esperar alguns dias pela resposta.

### 0.2 Checklist

- [x] Avisos de "contato em breve" removidos do site (5 idiomas). **Falta publicar** (`site\tools\deploy-cloudflare.ps1`).
- [ ] Analytics: o site não mede visitas (o token do Cloudflare Web Analytics ainda é placeholder). Sem ele, não dá para saber qual canal vende.
      Ligar é grátis. Os links abaixo já levam `?ref=` para identificar a origem.
- [ ] Gravar o vídeo curto (seção 8). No Reddit, post com vídeo de gameplay rende várias vezes mais que post com link.
- [ ] Conta do Reddit com algum histórico: comentar em r/humankind e r/4Xgaming por alguns dias antes. Conta nova postando link
      cai no filtro de spam.
- [ ] Postar um canal por dia, nunca todos no mesmo dia. Ficar 2 horas respondendo depois de cada post.

## 1. Estratégia por canal (o porquê)

| Canal | Quem está lá | O que vende ali | Formato |
|---|---|---|---|
| r/humankind | Fãs que ainda jogam e reclamam da IA diplomática "muda" | Mostrar a IA conversando de verdade e deixar claro que a parte econômica é grátis | Galeria de imagens ou vídeo; link no 1º comentário |
| Discord oficial | O núcleo mais fiel, e moderadores da Amplitude | Pedir permissão aos mods antes. Um post curto com 1 imagem | Canal de mods/showcase |
| Games2Gether | Jogadores antigos e o Google | Página completa e duradoura que também serve de suporte | Tópico longo |
| r/4Xgaming | Jogadores de 4X em geral, muitos nem têm HUMANKIND | Discussão de design: "e se a IA de 4X tivesse que cumprir a palavra?" O mod aparece como prova | Post de texto com imagem |
| r/aigamedev | Desenvolvedores curiosos com LLM em jogos | Relato técnico honesto: custo por turno, alucinação, fog of war. Vende pouco direto, mas gera credibilidade e compartilhamento | Post técnico |
| r/LocalLLaMA | Quem roda modelo local e detesta nuvem paga | **Não postar agora.** O mod não roda modelo local (Ollama/LM Studio). Um produto pago que exige API de nuvem é rejeitado ali e queima o nome | Fica para quando houver suporte a modelo local |

Regras de ouro, que valem para todos:

1. **O preço vai no próprio post.** Esconder que é pago é o que mais gera ódio no Reddit. "$10 one-time, AI paid to your own
   provider, economy features free" dito de cara desarma a crítica.
2. **Escreva como o desenvolvedor, em 1ª pessoa.** "I built", nunca "Check out this amazing mod".
3. **A prova vem antes da promessa:** uma carta real com o diário secreto do mesmo turno.
4. **Não brigar nos comentários.** Agradecer a crítica e responder com fatos (seção 6).

## 2. r/humankind

**Formato:** galeria (`site_mail.jpg`, `site_council.jpg`, `site_spy.jpg`, `site_toll.jpg`, em `_Modding\site\live\assets\`), ou
o vídeo da seção 8. Flair: o de mod/fan content, se existir. Leia as regras da barra lateral antes.

**Título (escolha 1):**
- `I made the AI nations in HUMANKIND write letters, keep grudges, and actually act on what they say`
- `The AI in HUMANKIND now writes you letters, and a secret diary where it admits it's bluffing`

**Texto:**

```
I've been working on this mod for months: every AI empire is run by a language model that reads only what its
nation knows (fog of war holds), writes you letters in the voice of its culture, keeps a secret diary, and then
acts through the game itself: declares war, demands tribute, moves armies, votes in the World Congress.
32 real actions, not a chatbot on top of the game.

The part I like most: they lie. In a 16-empire test game, Alexandre of the Mongols told every court "my
honor keeps my ledger". His secret diary the same turn: "Victor's eight hundred stays unpaid; let him weigh
iron if he dares."

What else is in it:
- Your own currency and a Central Bank with interest rates, inflation and an economic cycle
- Trading posts you can set to Free / Toll / Blocked per empire (the AI tolls you back)
- A council of ministers who argue with you each turn
- Spies that intercept other nations' letters
- Works with 16 empires through the MoreEmpires plugin

Being upfront about cost: the AI diplomacy is a paid mod ($10, one time, 7-day refund). You bring your own AI
key (DeepSeek, OpenRouter, OpenAI, Gemini...), roughly $0.03 a turn with 10 empires, with a spending cap.
The economy, Central Bank, tolls and 16-empire support work free without a key.

Single-player, Windows (Steam/Epic). Happy to answer anything about how it works.
```

**1º comentário (logo após postar):**

```
Link for anyone curious: https://realpolitik-living-nations.pages.dev/?ref=r-humankind
It has three short clips of real games (a letter turning into a war, a betrayal, an intercepted letter) and a
cost calculator so you know the AI bill before you start.
```

## 3. Discord oficial de HUMANKIND

**Passo 1: mensagem privada para um moderador** (antes de postar qualquer coisa):

```
Hi! I'm the developer of Realpolitik, a single-player HUMANKIND mod where AI nations are driven by language
models (they write letters, keep memories and act through real game actions). It's a paid mod ($10, BepInEx,
not on mod.io). Before posting anything I wanted to ask: is sharing it in the modding channel OK, and is there
a specific channel or format you'd prefer? Totally fine if not.
```

**Passo 2, se permitirem: post no canal indicado** (1 imagem: `site_mail.jpg`):

```
**Realpolitik: Living Nations**: AI empires that write you letters and mean (or don't mean) them

Each AI nation is run by a language model: it only knows what its fog of war shows, writes in its culture's
voice, keeps a secret diary, and acts through 32 real game actions: war, tribute, army orders, Congress votes.
Plus your own currency, a Central Bank and tolls on trading posts (those work free).

$10 one-time · bring your own AI key (~$0.03/turn at 10 empires, spending cap) · single-player · Windows
https://realpolitik-living-nations.pages.dev/?ref=discord
Questions welcome here or in DM.
```

## 4. Fórum Games2Gether (seção de mods do HUMANKIND)

Tópico permanente, que serve de página de produto e de suporte. Ali o texto pode ser longo.

**Título:** `[MOD] Realpolitik: Living Nations: AI empires that negotiate, bluff and keep their word (or don't)`

```
Hi everyone,

Realpolitik is a single-player mod that replaces the silent AI diplomacy with nations driven by language
models, plus a deeper economy. I'll keep this thread updated and answer support questions here.

WHAT THE AI NATIONS DO
- Every turn each nation reads a dossier with only what it knows. Fog of war holds: it can't see your
  armies unless it really could.
- It writes you letters in the voice of its culture, and a secret diary you never see (unless your spies
  get lucky).
- It keeps memory and feelings toward each empire (affection, trust, fear, anger) and collects on old
  promises.
- It acts through the game: 32 kinds of action. Declare war (formal or surprise), peace, surrender with
  terms, alliances and agreements, demands, gifts of money/cities/armies, ceding territory, sponsoring
  independent peoples, army orders (move, attack, defend, besiege), trade policy, and World Congress laws,
  votes and bribes. Five Congress actions need the Together We Rule DLC.
- Letters only mention resources, cities, leaders and events that exist in your game.
- Your spies can intercept letters; AI nations spy on each other too.
- You get a council of 11 ministries plus the Hand of the Throne who argue with you every turn.

ECONOMY (free, no key needed)
- Every empire prints its own currency, with exchange rates.
- Central Bank: set interest by hand or let the Taylor rule do it; production, inflation and interest
  form a real cycle.
- Trading posts: Free / Toll / Blocked per foreign empire. Routes weigh paying vs. detouring, and blocking
  files a diplomatic grievance.
- 16 empires through the optional MoreEmpires plugin (tested on Tiny, Normal and Huge).

PRICE AND AI COST, UPFRONT
- $10 one time, 7-day refund, free updates, no DRM.
- AI diplomacy needs your own key: DeepSeek, OpenRouter, OpenAI, Gemini, Z.ai or xAI. Measured with DeepSeek:
  about $0.03 per turn with 10 empires, $0.12–0.14 with 16. A per-game spending cap (default $5) stops the bill.
  If every provider fails, the game's native AI takes over.

REQUIREMENTS
Windows, HUMANKIND on Steam or Epic, single-player (it turns itself off in multiplayer). Steam Deck through the
manual zip. Not Mac or Xbox/Game Pass. Saves keep opening if you remove it (except saves with more than 10 empires).
Interface in English, Portuguese, Spanish, French and German.

Store, clips and cost calculator: https://realpolitik-living-nations.pages.dev/?ref=g2g
Support: lucascarezia@gmail.com, or reply here.

Unofficial fan-made mod, not affiliated with or endorsed by Amplitude Studios or SEGA.
```

## 5. r/4Xgaming

**Ângulo:** discussão de design. O sub gosta de conversar sobre IA de 4X, e "comprem meu mod" afunda. A pergunta no fim
puxa comentários, e comentários sobem o post.

**Título:** `What if 4X AI had to put its diplomacy in writing, and could lie about it? I tried it in HUMANKIND`

**Texto (com 1 imagem: `site_mail.jpg`):**

```
The thing that always broke diplomacy in 4X games for me: the AI's "personality" is a number. It likes you
or it doesn't, and nothing it says means anything.

So I built a mod for HUMANKIND where every AI empire is run by a language model with strict limits:
- it only sees what its nation can see (fog of war holds);
- it has to act through the game's real actions (32 of them: war, tribute, army orders, Congress votes),
  so words have consequences;
- it keeps memory and a secret diary, so promises and grudges last.

The emergent part surprised me. A Mongol king told every court "my honor keeps my ledger", and the same
turn wrote in his secret diary that a debt of 800 gold "stays unpaid; let him weigh iron if he dares."
Nations stall, bluff, and collect on old promises.

Hard problems I'm still chewing on, and curious how you'd solve them:
1. How much should an AI nation "remember"? Too little feels shallow, too much and it holds a grudge forever.
2. Should AI nations be allowed to ignore the player completely? I let them, and it feels more real but
   can be frustrating.
3. Cost: each nation calls an AI model every turn. Is ~$0.03/turn (10 empires) acceptable to you as a player,
   or a dealbreaker?

(It's a paid mod, $10 one-time, AI calls billed by your own provider. Link in the comments if anyone wants
to see it; mainly I'd love the design discussion.)
```

**1º comentário:**

```
Mod page with three short clips from real games and a cost calculator:
https://realpolitik-living-nations.pages.dev/?ref=r-4x
```

## 6. r/aigamedev

**Ângulo:** relato técnico com números reais. Desenvolvedores valorizam honestidade sobre o que deu errado.

**Título:** `Running 16 LLM-driven nations in a shipped 4X game: what it costs and what broke`

```
I shipped a mod for HUMANKIND (Amplitude's 4X) where every AI empire is driven by an LLM. Some notes from
making it work inside a real game instead of a demo:

ARCHITECTURE
- Each turn, each nation gets a dossier built only from what it can see (fog of war enforced in code, not
  in the prompt): its cities, armies, relations, letters received, memory notes, feelings.
- It answers in JSON: letters, a private diary, and actions from a fixed set of 32 game actions (war, peace,
  pacts, demands, gifts, territory, army orders, Congress votes). The game executes them; the model never
  touches game state directly.
- Grounding: letters may only name cities, leaders, resources and events that exist. A name that only
  appeared in a letter received counts as rumor; anything else is rejected and retried.

COST (the part nobody talks about)
- First measurement: ~$0.12/turn with 16 empires on DeepSeek. Too much.
- One bug was half of it: reasoning effort sent in the wrong field, so the model always thought at "high".
- Dossier reordered for prefix caching: stable stuff first (rules, persona, old letters in 5-turn windows),
  the current turn last. About half the input now hits cache.
- Result on a replay bench of real decisions: -52% per decision, with no quality loss in a blind comparison.
- Provider failover queue; if every provider fails, the game's native AI takes over mid-game.

WHAT STILL HURTS
- Truncated first-turn answers with 16 empires (every nation introduces itself at length).
- Peak-hour pricing roughly doubles the bill.

Players bring their own key and set a per-game spending cap. Happy to go deeper on any part: the
action schema, memory windows, or the replay bench for testing prompt changes.

(Mod page, if you want to see the output: https://realpolitik-living-nations.pages.dev/?ref=r-aigamedev,
$10, the AI part is BYOK.)
```

Antes de postar, confira se a regra do sub permite produto pago. Se não permitir, tire o último parágrafo e responda
o link só a quem pedir.

## 6.1 Respostas prontas para os comentários

| Comentário | Resposta |
|---|---|
| "Paid mods? No thanks." | `Fair. That's why the economy, Central Bank, tolls and 16-empire support are free without a key. The $10 is for the AI diplomacy, which took months, and there's a 7-day refund if it isn't for you.` |
| "AI slop" | `The model doesn't write content for its own sake: it decides as a nation with limited information, and the game executes the decision. Letters can only mention things that exist in your game. The clips on the page are unedited output.` |
| "How much does the AI cost?" | `Measured with DeepSeek: ~$0.03/turn with 10 empires, $0.12–0.14 with 16. There's a per-game cap (default $5), and the page has a calculator.` |
| "Does it work with local models?" | `Not yet. Right now it needs an API key (DeepSeek, OpenRouter, OpenAI, Gemini, Z.ai, xAI). Local support is something I'm looking at.` |
| "Multiplayer?" | `Single-player only. It turns itself off in multiplayer.` |
| "Why not on mod.io / Nexus?" | `It's a BepInEx code mod, which mod.io doesn't support, and Nexus doesn't host paid mods.` |
| "Is it allowed by Amplitude?" | Responder só depois da resposta ao e-mail da seção 7. |

## 7. E-mail para a Amplitude (pedido de consentimento)

Para: contato oficial da Amplitude (formulário de suporte ou e-mail de imprensa/parcerias do site da Amplitude).

```
Subject: Permission request: paid single-player mod for HUMANKIND ("Realpolitik: Living Nations")

Hello Amplitude team,

I'm Lucas Carezia, an independent developer from Brazil and a long-time HUMANKIND player. I've built a
single-player mod, "Realpolitik: Living Nations", that lets AI empires negotiate through written letters
driven by language models, and adds a currency/central-bank layer. It's a BepInEx code mod; it doesn't
include or redistribute any game files, and it is clearly labeled as unofficial and not affiliated with
Amplitude or SEGA.

I'm offering it for $10 through my own site, and I'd like to promote it with gameplay screenshots and
short clips. Section 10 of the EULA asks for prior written consent for commercial use of game footage, so
I'd like to ask for that consent, and to hear whether you have any conditions or concerns about the mod
being paid.

Page: https://realpolitik-living-nations.pages.dev
I'm happy to provide a free copy, change anything you'd like, or take it down if you'd rather I didn't.

Thank you for HUMANKIND,
Lucas Carezia
lucascarezia@gmail.com
```

## 8. Vídeo curto (30 a 45 s, gravado no OBS)

Roteiro de cenas, todas com o mod rodando de verdade (regra do site: nada de upscale e nada de tela que não seja do jogo):

1. (0–5 s) Carta chegando na tela de Correio. Legenda: *"The AI nations in HUMANKIND now write to you."*
2. (5–12 s) Abrir a carta e mostrar o texto. Legenda: *"They only know what they can see."*
3. (12–20 s) Diário secreto do mesmo turno contradizendo a carta. Legenda: *"And they lie."*
4. (20–30 s) A consequência no mapa: guerra declarada ou exército se movendo. Legenda: *"Then they act."*
5. (30–38 s) Conselho e Banco Central de relance. Legenda: *"Plus your own currency, central bank and tolls."*
6. (38–45 s) Cartão final: *"Realpolitik: Living Nations · $10 · link in comments"*.

Formato: 1080p, 16:9 para o Reddit. Recortar para 9:16 para X e Shorts depois.
