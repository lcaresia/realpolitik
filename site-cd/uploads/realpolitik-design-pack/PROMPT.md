# Prompt — Landing page de venda: "Realpolitik: Living Nations for HUMANKIND"

> Prompt autocontido. Vem junto com `realpolitik-design-pack.zip` (materiais reais: rascunho atual, clipes animados com som, prints do jogo em inglês, cartas reais, fatos conferidos). Leia este arquivo inteiro e o `README.txt` do zip antes de desenhar.

---

## 1. Missão

Desenhe e construa a **landing page de venda** de um mod de PC para o jogo de estratégia **HUMANKIND** (Amplitude/SEGA). O mod se chama **Realpolitik: Living Nations for HUMANKIND** e vai ser vendido por **US$ 10, pagamento único**, para jogadores do mundo todo.

A página tem **um único trabalho: provar, em menos de um minuto de rolagem, que vale a pena pagar.** Quem chega é um jogador de Humankind que acha a IA do jogo sem graça e a diplomacia vazia. Quem sai tem que pensar: "eu preciso jogar isso hoje".

**Padrão de qualidade:** página de lançamento de jogo indie premiado (Hades, Pentiment, Inscryption, Against the Storm) com o acabamento de Linear/Arc/Vercel. Motion com propósito, tipografia forte, interações que demonstram o produto, impecável no celular. **Tem que parecer página de jogo, com imagens do jogo** — nada de template SaaS, nada de "AI slop" (gradiente roxo genérico, ícones aleatórios, grade de features iguais).

**Idioma da página:** inglês primeiro, com versão em português pronta para revisão. Arquitetura preparada para **5 idiomas** (en, pt, es, fr, de) — o mod já fala os 5.

---

## 2. O produto (fatos conferidos — não invente nada além disso)

O mod transforma cada nação controlada pelo computador em um **personagem movido por IA de linguagem** (LLM):

1. **Living Nations (o coração).** A cada turno, cada nação lê um dossiê com **só o que ela sabe** (respeita a névoa de guerra do jogo), escreve um **diário secreto**, guarda memória e sentimentos (afeto, confiança, medo, raiva) e **escreve cartas** — para o jogador e para as outras nações — na voz da sua cultura. Ela blefa, enrola, ameaça, cobra promessas antigas.
2. **As palavras viram ações reais no jogo:** **32 tipos de ação** — guerra (formal ou surpresa), paz, alianças e acordos (econômico, informação, cultural, militar), exigências e crises, rendição com termos, presentes (dinheiro, influência, cidades, exércitos), cessão de território, patrocínio de povos independentes (inclui vassalagem/anexação), **ordens a exércitos** (mover, atacar, defender), política comercial, bloquear correspondência, demitir ministros, e o **Congresso mundial** (DLC Together We Rule: propor leis, votar, subornar, abrir crises, desafiar vereditos — 5 desses tipos exigem a DLC). Não é chatbot por cima do jogo: é a nação decidindo.
3. **Correio Diplomático** em tela cheia, nativo do jogo, com 7 seções (Novas, A responder, Lidas, Respondidas, Enviadas, Comunicados públicos, Nações); aba **Cartas** dentro da tela de diplomacia de cada nação; cartas chegam com atraso conforme a era (2, 1 ou 0 turnos); até 3 cartas por nação por turno, até 150 palavras cada.
4. **Espionagem:** espiões **interceptam cartas privadas** (que então **nunca chegam** ao destino). Aba "Letters" dentro da tela nativa de Inteligência. As nações da IA também espionam umas às outras.
5. **Seu conselho de ministros:** conselho de **12 membros** (11 pastas + "a Mão do Trono"). A cada turno até 6 ministros falam sobre o reino; você responde (até 8 vezes por turno), eles reagem, o apreço muda, você pode demitir. As nações da IA também têm conselhos de 12.
6. **Moeda própria por império + Banco Central** (tela nativa, 4 abas: Câmbio, Política, Ciclo, Sua moeda): câmbio, inflação, juros (manual ou automático pela "regra de Taylor"), o ciclo produção → inflação → juros → produção, integrado nos números e tooltips nativos.
7. **Bloqueio comercial:** cada posto comercial seu, por império estrangeiro, pode ser **Livre, Pedágio (com preço) ou Bloqueado**; as rotas comparam pagar com desviar; a prévia mostra na hora quantas rotas pagam e quantas desviam. A IA também taxa e bloqueia.
8. **Até 16 impérios** (plugin opcional MoreEmpires; testado em mapas Tiny, Normal e Huge).
9. **Visual 100% nativo:** as telas foram feitas com a interface do próprio jogo. Parece que veio com o jogo.
10. **5 idiomas** (en, pt, es, fr, de) na interface (1.192 textos traduzidos em cada) **e nas cartas** das nações.
11. **Você escolhe a IA:** login com OpenRouter ou chave própria (DeepSeek, OpenAI, Gemini, GLM, xAI). Se um provedor falha, passa para o próximo; se todos falham, a **IA nativa do jogo assume**. **Teto de gasto por partida** (padrão US$ 5, ajustável).
12. **Seguro:** saves continuam abrindo sem o mod (exceto saves com mais de 10 impérios, que precisam do MoreEmpires); o mod se desliga sozinho em multiplayer.

### Números verificáveis (use só estes; arredonde para baixo)
- 32 tipos de ação real; conselhos de 12 membros; 7 seções no correio; 4 abas no Banco Central; 3 modos por posto comercial; até 16 impérios; 5 idiomas; 1.192 textos por idioma.
- **Custo medido (DeepSeek, único provedor medido de verdade):** ~US$ 0,03 por turno com 10 impérios; ~US$ 0,12–0,14 por turno com 16 impérios fora do horário de pico (dobra no pico). Teto padrão US$ 5 por partida.
- Outros provedores: só **estimativas** (rotule como estimativa). Ex.: gpt-5-nano ~US$ 0,064/turno, gemini-2.5-flash-lite ~US$ 0,074, glm-4.7-flash grátis, glm-4.7-flashx ~US$ 0,069 (16 impérios).
- **NÃO afirme** "a partida inteira custa menos que um café" (falso com 16 impérios). **NÃO venda "modelos grátis"** como solução: o OpenRouter grátis tem limite de 50 chamadas/dia e um turno de 16 impérios usa 21–28 chamadas.

### Requisitos e limites (têm que estar claros na página)
Windows · HUMANKIND na **Steam** (Epic ainda não testado) · **só single-player** · Steam Deck experimental · **Mac e Xbox/Game Pass não suportados** · a IA é paga à parte pelo jogador ao provedor que ele escolher, com custo por turno e teto de gasto · desinstalar hoje = apagar duas pastas (instalador em preparação). **Honestidade sobre isso vende mais do que esconder.**

### Decisões comerciais (já tomadas)
- **US$ 10**, pagamento único. **Venda já** (sem lista de espera).
- **Sem a chave de licença, tudo funciona menos a Diplomacia IA** (moeda, Banco Central, pedágio e 16 impérios ficam grátis = demonstração).
- **Reembolso: 7 dias.**
- Loja: **Lemon Squeezy**, checkout em **overlay** (`lemon.js`); URL do produto numa constante `CHECKOUT_URL` (placeholder claramente marcado). Preço numa constante.
- Hospedagem: **GitHub Pages, sem domínio próprio**. Site **estático** (não há Node/Python na máquina do autor: HTML/CSS/JS puro funciona melhor; se usar build, entregue também a saída estática).
- Analytics: **Cloudflare Web Analytics** (sem cookies), token como placeholder.
- E-mail de contato: ainda não existe — deixe placeholder.
- Rodapé obrigatório: *"Unofficial fan-made mod. Not affiliated with or endorsed by Amplitude Studios or SEGA. HUMANKIND is a trademark of its respective owners."*

---

## 3. Princípio número 1: provar, não prometer

- **Só cartas reais** escritas pelas nações em partidas reais — estão em `03-letters/` do zip, com nação, turno e fonte. Nunca reescreva uma carta e a apresente como gerada pelo jogo; corte só com reticências. Traduções do português estão marcadas como tradução; **prefira as cartas que o jogo gerou direto em inglês** (`letters-en-T82.txt`).
- **Só prints reais** feitos por nós (pasta `02-screenshots-en/`). **Não use** key art, logos oficiais ou material de imprensa da SEGA/Amplitude.
- **Proibido:** depoimentos falsos, contadores fake, "10.000 jogadores", notas inventadas, "oferta acaba em 10:00", escassez falsa. Sem depoimentos reais → sem seção de depoimentos (deixe o espaço preparado).
- O nome de usuário do autor nunca aparece (já foi limpo de tudo no pacote; não reintroduza).

---

## 4. O que o autor já disse (feedback real — siga à risca)

1. Das direções testadas, **gostou da A ("The Archive": carta em pergaminho, selo de cera rompendo, diário secreto deslizando por baixo) e da C ("Statecraft": linguagem visual nativa do jogo — painéis azul-ardósia, versalete dourado, ações em magenta; "words become orders" no mapa)**. Descartou a B (dossiê preto/vermelho estilo espionagem). → Combine A + C.
2. **"Precisa parecer mais uma página de jogo, com imagens do jogo."**
3. **Toda imagem tem que mostrar algo do mod.** Rejeitou um print que era tela padrão do jogo ("essa print não mostra nada do mod").
4. **Nada de print ampliado/borrado** como fundo ("a imagem de fundo ficou horrível"). Use as imagens no tamanho nativo ou menor (os mapas limpos têm 1520×855; os prints de UI, 1920×1080 ou recortes).
5. **Texto forte.** Rejeitou títulos literais e sem graça (ex.: "Your spies read their mail." / "Seus espiões leem a correspondência deles." → "que frase horrível"). Quer copy de verdade: concreta, curta, com tensão de jogo de estratégia. Sem jargão de marketing vazio.
6. **Adorou os clipes animados** (3 histórias reais de ~30 s em motion) e pediu: **som** (trilha + efeitos), **trilha com cara de jogo de estratégia** (orquestral, tipo Civilization/Humankind — a primeira versão "nada a ver" foi rejeitada), **ícone de som sempre visível no vídeo para mutar/desmutar**, **bolinha arrastável no player**, e **a música tem que parar quando pausa**.
7. Pediu **mais botões de comprar** — hoje há 5 pontos de compra (nav fixa, hero, 2 faixas no meio, final).

---

## 5. O que já existe (no zip, pasta `01-current-draft/`) — use como ponto de partida, não como teto

- `directions/ac-game.html` — rascunho atual (A + C) com seletor EN | PT (`ac-i18n.js`): hero com mapa real + carta de Salomão escrita à pena + selo de cera + diário secreto; seção "Two faces" (clipe 1); "Words become orders" (história real de Zenóbia, T119→T128, + clipe 2); faixa de compra; espionagem (clipe 3 + print real); faixa de compra; telas nativas; CTA final; rodapé.
- `directions/clips.html` + `directions/clips/` — **os 3 clipes cinematográficos** (GSAP 3.13 + MotionPath + DrawSVG, palco 1280×720 escalado), com **trilha orquestral e efeitos gerados por Web Audio** (`sound.js`, sem arquivos de áudio), ícone de som fixo, barra com bolinha arrastável, pausa fora da tela/aba oculta, versão sem animação para `prefers-reduced-motion`:
  1. **Two faces** (T65): a névoa de guerra se abre; diário secreto ("enquanto sorrio, dois exércitos meus ainda dormem à porta do huno") → carta pública ("Não trago a espada, trago a palavra") → selo → envelope voa → lado a lado.
  2. **Words become war** (T118→T128): carta de Mu Guiying vira "War declared" → exército marcha sobre a capital → Zenóbia entra na guerra por carta pública → frota ataca dois portos (contador de turnos) → "Aceito vossa rendição… basta-me a porta aberta e a lição contada".
  3. **Intercepted** (T74): carta privada dos celtas aos sumérios ("Contra o norte tens em mim quem não vira as costas") é interceptada pelo espião de **Wang Zhenyi, líder dos nórdicos** — o próprio "norte" leu a carta — e nunca chega.
- `directions/a-archive.html`, `directions/c-statecraft.html` — os heros das direções A e C originais.
- Para rodar local: qualquer servidor estático na pasta `01-current-draft/` (ex.: `npx serve`, ou abrir via servidor do seu ambiente); abra `directions/index.html`.

Pode refazer tudo, mas **mantenha o que o autor aprovou**: o conceito A+C, os clipes com som orquestral e controles, a honestidade, e os 5 pontos de compra.

---

## 6. Materiais do zip

- `02-screenshots-en/` — **prints reais em inglês (turno 80–82, partida de teste com 16 impérios)**, todos mostrando o mod:
  - `site_mail.png` — Correio Diplomático em tela cheia com cartas reais em inglês. **Redação:** onde as cartas citavam o nome de usuário do jogador, a imagem foi editada para "Shah" (privacidade). O resto do texto é o original.
  - `site_16.png` — Correio, seção Nações: 14 nações conhecidas com líderes (alguns nomes de persona vêm em português do save: "Tarquínio, o Antigo").
  - `site_letters_tab.png` — aba Letters na diplomacia com os Nórdicos (Wang Zhenyi), troca de cartas real. **Redação:** nome do jogador trocado por "Shah"; o painel esquerdo foi cortado e uma carta pública em português no topo foi coberta.
  - `site_spy.png` — Inteligência › Letters: carta real Mongóis → Nórdicos lida pelo espião do jogador. **Atenção à honestidade:** para a captura, o espião foi criado e a interceptação forçada com um comando de desenvolvedor. O mecanismo é real e acontece sozinho no jogo, mas *esta* interceptação foi encenada — não a apresente como "aconteceu na partida". (A interceptação natural documentada é a do T74, em `letters-pt-originais.md` §3.)
  - `site_council.png` — Conselho: 12 conselheiros, 6 falaram no turno 82 (mostra "Meeting held US$ 0.0020" — custo real da reunião).
  - `site_bank.png` (Política), `site_cycle.png` (Ciclo), `site_bank_exchange.png` (Câmbio — os nomes padrão das moedas ainda aparecem em português: Florim, Rublo, Iene, Ducado) — Banco Central.
  - `site_toll.png` — Posto comercial: pedágio por império, bloquear, aplicar a todos os postos. **Não mostra** a prévia "X pays · Y detour" (nenhuma rota estrangeira cruzava o território no momento).
  - `site_map_1..4.png` — **mapas limpos sem HUD** (1520×855) para fundos/atmosfera.
  - `context-vanilla/` — telas padrão do jogo (Congresso, notificação de aliança, proposta de tratado). **Só contexto; não usar como prova do mod.**
- `03-letters/letters-en-T82.txt` — 5 cartas reais **geradas em inglês** + diário secreto do mesmo turno + **a ação que a nação executou no jogo**. (A nota de que a carta da Celta foi interceptada se refere à interceptação **forçada** para a captura — ver `site_spy.png`; não use como história natural.)
- `03-letters/letters-pt-originais.md` — as melhores histórias das partidas em português (originais + tradução marcada), com turno, nação e o que aconteceu.
- `04-facts/facts.md` — fatos, números, requisitos, ressalvas (resumo da seção 2).
- `01-current-draft/assets/shots/` — recortes usados no rascunho (prints antigos em PT; substitua pelos em inglês).

**Faltam (deixe espaço marcado):** vídeo/trailer gravado do jogo, exército se movendo/atacando por ordem de uma carta, declaração de guerra nativa, prévia de desvio do pedágio ("X pays · Y detour"), comparação antes/depois de tela nativa, print do Congresso com ação do mod, print da tela de provedores de IA (a captura atual não pode ser usada).

---

## 7. A narrativa sugerida (ajuste à vontade, mas justifique)

1. **Hero — "a carta chega".** Carta real sendo escrita, selo de cera rompendo, diário secreto revelando o oposto. Título forte, subtítulo de uma linha, CTA **"Get it — $10"** + secundário **"Watch it in action"**. Abaixo: *Windows · Steam · Single-player · 5 languages*.
2. **O problema** em uma tela: a diplomacia padrão — IA declara guerra do nada, nunca explica, nunca negocia. Contraste, não reclamação.
3. **Living Nations:** cartas reais + diário secreto ("what they wrote you" × "what they actually think") — o momento "uau". Clipe 1.
4. **Words become orders:** carta → ação no mapa. Grade viva das ações reais. Clipe 2.
5. **Espionagem:** clipe 3 + print real `site_spy.png`.
6. **Seu conselho:** ministros falando (print `site_council.png`), você responde, o apreço muda.
7. **A economia que reage:** ciclo produção → inflação → juros → produção guiado pela rolagem + mini-simulador de juros (valores ilustrativos rotulados; regras: juro neutro 1%/turno; cada ponto acima do neutro tira ~5% da produção, de ×0,75 a ×1,05; inflação alvo 0,5%).
8. **Bloqueio comercial:** controle de preço do pedágio em que as rotas passam a desviar ("1 pays · 0 detour" → "0 pay · 1 detours"), como a prévia real.
9. **16 empires:** os estandartes entrando; `site_16.png`.
10. **"Looks like it shipped with the game":** telas nativas (correio, banco, diplomacia).
11. **Custo transparente:** calculadora impérios × turnos × provedor (DeepSeek medido; os outros como estimativa), teto de gasto, fallback para IA nativa.
12. **Idiomas:** a mesma carta trocando de idioma.
13. **Comparação** Humankind padrão × com Realpolitik (curta e honesta).
14. **Instalação em 3 passos** + **FAQ** (multiplayer, saves, desinstalar, Steam/Epic/Deck/Mac/Xbox, quanto custa a IA, que dados saem do PC — só o dossiê de cada nação vai ao provedor; nada pessoal, nem nome nem conta Steam; chaves ficam criptografadas no PC —, atualizações grátis baixando de novo, reembolso 7 dias, chave de licença, suporte).
15. **CTA final** com carta selada "endereçada ao visitante". Mantenha **pelo menos 5 pontos de compra** ao longo da página.
16. **Rodapé** com aviso de mod não oficial, contato (placeholder), privacidade, termos, changelog.

---

## 8. Direção de arte e motion

- Conceito: **sala de mapas à noite × interface nativa do Humankind** — azul-noite, pergaminho, tinta, selos de cera, dourado discreto, painéis azul-ardósia com versalete dourado, magenta para ações.
- Tipografia: serifada expressiva para títulos (o rascunho usa Fraunces + Cinzel), tipografia de época para as cartas (IM Fell English), sans legível para texto (Mulish). Google Fonts ou self-hosted, `font-display: swap`.
- **Motion com propósito** (GSAP + ScrollTrigger, gratuitos, incluindo SplitText/DrawSVG/MorphSVG/MotionPath): tinta escrevendo, selo quebrando, envelope voando, rotas sendo traçadas, estandartes entrando, números contando. Seções "pinned" nos momentos-chave. Microinterações em botões e cartões. **`prefers-reduced-motion`:** tudo bonito e legível sem animação.
- **Som:** só nos clipes, sempre começando mudo (o navegador exige clique), ícone visível para mutar/desmutar, trilha orquestral de estratégia, para quando pausa/sai da tela/aba oculta.

## 9. Técnica

- Estático, hospedável no **GitHub Pages**. Celular primeiro (perfeito em 375 px, sem rolagem lateral) até 2560 px.
- **Lighthouse ≥ 90** em performance, ≥ 95 em acessibilidade/boas práticas/SEO. LCP < 2,5 s em 4G, CLS ≈ 0, imagens AVIF/WebP com tamanhos responsivos (converta os PNG), JS de animação depois do conteúdo.
- Acessibilidade: contraste AA, teclado, foco visível, `alt` descritivo, legendas.
- **i18n:** um arquivo de textos por idioma (en agora, pt pronto; es/fr/de preparados), sem texto preso no HTML.
- SEO/compartilhamento: título, descrição, Open Graph + Twitter card (imagem 1200×630: carta selada + nome), favicon (selo de cera), `sitemap.xml`, `robots.txt`, dados estruturados `SoftwareApplication`/`Product` **sem avaliações inventadas**.
- Páginas extras: `/privacy`, `/terms` (sem cláusula genérica proibindo engenharia reversa — exigência da LGPL do BepInEx que acompanha o mod), `/changelog`, `/install` (com Steam Deck e aviso do SmartScreen do Windows).

## 10. Entrega esperada

1. Primeiro, **2–3 propostas visuais** do hero + uma seção de prova (já com copy forte) para o autor escolher.
2. Depois, a página completa em inglês + versão em português, com os clipes integrados, os 5+ CTAs, FAQ, calculadora de custo e as páginas extras.
3. Um `README` curto: onde trocar textos, preço, `CHECKOUT_URL`, token de analytics; como publicar no GitHub Pages.
