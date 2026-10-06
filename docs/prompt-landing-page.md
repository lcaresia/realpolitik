# Prompt: landing page de venda do "Realpolitik: Living Nations for HUMANKIND"

> Prompt independente para uma sessão nova do Claude Code, aberta na pasta do Humankind. Cole o conteúdo abaixo da linha.

---

## Missão

Construir a **landing page de venda** do mod **Realpolitik: Living Nations for HUMANKIND**, que vai ser vendido por
**US$ 10** (venda única) na Lemon Squeezy para jogadores do mundo todo.

A página tem **um único trabalho: provar, em menos de um minuto de rolagem, que vale a pena pagar**. Quem chega é um
jogador de Humankind que acha a IA do jogo sem graça e a diplomacia vazia. Quem sai tem que pensar: "eu preciso jogar
isso hoje".

O padrão de qualidade é o de uma página de lançamento de jogo indie premiado (pense em Linear, Arc, Vercel, nas páginas de
Hades, Pentiment, Inscryption): **motion com propósito, tipografia forte, interações que demonstram o produto**, rápida e
impecável no celular. Nada de template genérico, nada de "AI slop" (gradiente roxo, ícones aleatórios, blocos de
features iguais). Vá fundo.

O usuário (lucas) fala português (pt-BR, informal). Fale com ele em pt-BR, curto. **A página é em inglês** (público
estrangeiro), preparada para depois ganhar es/fr/de/pt (o mod já fala as 5 línguas).

Leia a memória do projeto (MEMORY.md já vem carregada): `installable-goal.md`, `llm-diplomacy-idea.md`,
`currency-mod-design.md`, `trade-blockade-design.md`, `ai-providers.md`, `translation-status.md`, `feedback-workflow.md`.

## O produto (leia antes de escrever uma linha de texto)

Fontes da verdade, nesta ordem: `_Modding\README.md` (lista de funcionalidades), `docs\diplomacia-ia.md`,
`docs\design-diplomacia-ia.md`, `docs\bloqueio-comercial.md`, `docs\proposta-ciclo-economico.md`,
`docs\pesquisa-30-jogadores.md` (MoreEmpires), `docs\instalacao.md`, `docs\pesquisa-instalador-exe.md` (requisitos,
plataformas, loja), `research\provedores-ia.md` (provedores e custos).

Resumo do que o mod faz, para orientar (confirme cada detalhe nos docs antes de pôr na página):

1. **Living Nations (o coração):** cada nação do computador vira um personagem movido por IA de linguagem. A cada turno
   ela lê um dossiê com **só o que ela sabe** (névoa de guerra), escreve um diário secreto, guarda memória e
   sentimentos, e **escreve cartas** — para você e para as outras nações — na voz da sua cultura. Ela blefa, enrola,
   ameaça, cobra promessas antigas.
2. **As palavras viram ações reais no jogo:** guerra, paz, alianças, acordos, rendição com termos, exigências,
   presentes (dinheiro, cidades, exércitos), cessão de território, patrocínio de povos independentes, **ordens a
   exércitos**, política comercial, e o **Congresso mundial** (DLC Together We Rule: propor leis, votar, subornar,
   abrir crises, desafiar vereditos). Não é chatbot por cima do jogo: é a nação decidindo.
3. **Correio Diplomático** em tela cheia, nativo do jogo (novas, a responder, enviadas, comunicados públicos), aba de
   cartas dentro da diplomacia, e **espionagem: espiões interceptam cartas privadas** (que nunca chegam ao destino).
4. **Seu conselho de ministros:** a cada turno os ministros das pastas mais urgentes falam sobre o seu reino; você
   responde, eles reagem, você demite quem não serve. As nações da IA também têm conselhos de 12 ministros.
5. **Moeda própria por império + Banco Central:** câmbio, inflação, juros (manual ou automático), o ciclo
   produção → inflação → juros → produção, tudo integrado nos números e tooltips nativos do jogo.
6. **Bloqueio comercial:** cada posto comercial seu pode ser Livre, Pedágio (com preço) ou Bloqueado; as rotas
   comparam pagar com desviar, e a prévia mostra na hora quantas rotas desviam. A IA também taxa e bloqueia.
7. **Até 16 impérios** em qualquer tamanho de mapa (MoreEmpires, opcional).
8. **Visual 100% nativo:** todas as telas foram feitas com a interface do próprio jogo. Parece que veio com o jogo.
9. **5 idiomas** (en, pt, es, fr, de) na interface **e nas cartas** das nações.
10. **Você escolhe a IA:** login com OpenRouter (um clique, inclui modelos grátis) ou chave própria (DeepSeek, OpenAI,
    Gemini, GLM, xAI). Se um provedor falha, passa para o próximo; se todos falham, a IA nativa assume. Teto de gasto
    por partida.
11. **Seguro:** os saves continuam abrindo sem o mod; desinstalação limpa; o mod se desliga sozinho em multiplayer.

**Requisitos/limites (tem que estar claros na página):** Windows; Humankind na Steam (Epic quando testado — veja
`pesquisa-instalador-exe.md`); só single-player; Steam Deck experimental; Mac e Xbox/Game Pass não suportados; a IA é
paga à parte pelo próprio jogador ao provedor que ele escolher (ou grátis com modelos gratuitos), com custo medido por
turno. **A honestidade sobre isso vende mais do que esconder.**

## Princípio número 1: provar, não prometer

Tudo que a página afirmar tem que ter **prova real tirada do jogo**. Proibido inventar.

- **Cartas reais.** Extraia cartas verdadeiras escritas pelas nações em partidas reais:
  - `_Modding\cronicas\Cronica-dos-Reinos_T80-93.html` (crônica pronta, em português);
  - os logs `BepInEx\DiplomaciaIA\logs\` e o `DiplomaciaIA.json` dentro dos saves (`.ctr` é um zip);
  - a partida de teste **Teste16** (16 impérios, com Congresso) e as cartas em inglês do T77 (veja os prints
    `loc_en_cartas77.png`).
  Prefira cartas em **inglês** geradas pelo jogo. Se precisar de mais, peça ao lucas para gerar (ou, com permissão
  dele, use o kit de dev para passar turnos na Teste16 com `[IA] IdiomaDasCartas` em inglês). Nunca reescreva uma
  carta e a apresente como gerada; corte só com reticências e diga de que nação/turno ela é.
- **Prints e vídeos reais**, em inglês, da interface nativa. Já existem dezenas em `_Modding\dev\out\` (ex.:
  `loc_en_correio.png`, `loc_en_conselho.png`, `loc_en_ministros.png`, `loc_en_banco.png`, `loc_en_ciclo.png`,
  `rendicao_e7_oferece.png`, `lang_en_openrouter.png`). Faça um inventário, escolha os melhores e liste o que falta.
  Capturas novas: use o kit de dev (`_Modding\tools\dev\devcmd.ps1`, `docs\guia-telas-nativas.md`, comandos
  `jogo camera`, `jogo foco`, `hover`, `screenshot`) **com 16 impérios**, e em alta resolução. Para clipes curtos
  (loops de 3–8 s em MP4/WebM), veja se há `ffmpeg` na máquina (`gdigrab`); se não houver, **pergunte antes de
  instalar qualquer coisa**.
- **Números reais:** custo medido por turno (em `docs\instalacao.md` §6 e `research\provedores-ia.md`), quantidade de
  ações, de ministros, de idiomas, de textos traduzidos. Arredonde para baixo, nunca para cima.
- **Proibido:** depoimentos falsos, "10.000 jogadores", notas inventadas, contadores fake, "oferta acaba em 10:00",
  escassez falsa. Se não há depoimentos, não tem seção de depoimentos (deixe o espaço preparado para quando houver).

## A narrativa da página (proposta — discuta antes de construir)

Ordem pensada para conversão. Cada seção tem um papel; corte o que não ajudar.

1. **Hero — "a carta chega".** Primeira coisa na tela: uma carta real de uma nação sendo escrita/desdobrada (pena,
   tinta, selo de cera rompendo), com o nome da nação e o turno. Título forte, subtítulo de uma linha, CTA
   **"Get it — $10"** e um secundário **"Watch it in action (60 s)"**. Abaixo: "Windows · Steam · Single-player ·
   5 languages". Ideias de título para testar: *"Your rivals finally have something to say."* /
   *"Every empire now thinks, remembers, and writes to you."* / *"Diplomacy with nations that mean it."* Proponha
   outras.
2. **O problema** (curto, em uma tela): a diplomacia do Humankind padrão — a IA declara guerra do nada, nunca explica,
   nunca negocia, o meio do jogo vira planilha. Mostre com contraste, não com reclamação.
3. **Living Nations:** um carrossel/arquivo de **cartas reais**, com filtro por tom (ameaça, aliança, blefe, rendição,
   presente) e a "linha do tempo" da relação entre duas nações (ex.: amizade → exigência → guerra → rendição). Mostre o
   diário secreto ao lado da carta pública ("o que ela escreveu para você" × "o que ela pensava de verdade") — esse
   contraste é o momento "uau".
4. **"Words become orders":** uma animação em que a frase da carta ("Withdraw your army from Thessaly or…") vira a
   ação no mapa (exército se movendo, declaração de guerra nativa). Lista das ações reais como grade viva.
5. **Espionagem:** uma carta privada sendo interceptada no caminho (o envelope muda de rota e cai na aba de
   espionagem).
6. **Seu conselho:** os ministros falando em sequência sobre o seu reino, você responde, o apreço muda (barras
   animadas).
7. **A economia que reage:** diagrama animado do ciclo produção → inflação → juros → produção, guiado pela rolagem; um
   mini-simulador onde o visitante sobe os juros e vê a inflação e a produção reagirem (valores ilustrativos, rotulados
   como tal, coerentes com as regras do mod).
8. **Bloqueio comercial:** mapa estilizado com rotas; um controle de preço do pedágio em que as rotas passam a desviar
   ("1 pays · 0 detour" → "0 pay · 1 detours"), igual à prévia real do mod.
9. **16 empires:** os 16 estandartes entrando em cena; mapa cheio.
10. **"Looks like it shipped with the game":** comparação com cortina (antes/depois) entre a tela nativa original e a
    mesma tela com o mod.
11. **Custo transparente:** calculadora — impérios × turnos × provedor (inclui "free models") → custo estimado da
    partida, usando os números medidos. Mensagem: "a partida inteira custa menos que um café" (só se os números
    confirmarem). Mostre o teto de gasto e o fallback para a IA nativa.
12. **Idiomas:** a mesma carta trocando de idioma (en → pt → es → fr → de) com transição suave.
13. **Comparação** Humankind padrão × com Realpolitik (tabela curta e honesta).
14. **Instalação em 3 passos** (download → Setup → escolher a IA no menu principal), com o print da tela de provedores.
15. **FAQ:** multiplayer, saves, desinstalar, Steam/Epic/Deck/Mac/Xbox, quanto custa a IA, que dados saem do PC
    (privacidade), atualizações (grátis, baixando de novo), reembolso, chave de licença (quantos PCs), suporte.
16. **CTA final** com a carta selada "endereçada ao visitante" e o botão de compra.
17. **Rodapé:** contato, privacidade, termos, changelog, aviso "Unofficial fan-made mod. Not affiliated with or endorsed
    by Amplitude Studios or SEGA. HUMANKIND is a trademark of its respective owners."

## Direção de arte e motion

- **Conceito sugerido:** "o arquivo diplomático" — sala de mapas à noite, papel envelhecido, tinta, selos de cera,
  dourado discreto, combinando com a paleta da interface nativa do Humankind (azul-petróleo escuro, creme, dourado).
  Proponha **2–3 direções** (moodboard em HTML simples ou prints) antes de construir; o lucas escolhe.
- **Tipografia:** serifada expressiva para títulos (ex.: Fraunces, Cormorant, Instrument Serif) + sans limpa para texto
  (ex.: Inter, Geist). Uma "mão" caligráfica só nas cartas, com moderação. Tudo via Google Fonts ou self-hosted.
- **Arte do jogo:** use **só capturas de tela feitas por nós** (gameplay). Não use key art, logos oficiais nem
  material de imprensa da SEGA/Amplitude. Ilustrações próprias (SVG) para selos, envelopes, rotas, ícones.
- **Motion com propósito** — cada animação demonstra o produto ou guia o olho:
  - GSAP + ScrollTrigger (hoje gratuito, inclusive os plugins: SplitText, DrawSVG, MorphSVG, Flip) e Lenis para
    rolagem suave; ou Motion (motion.dev) se for React. Escolha e justifique.
  - Tinta "escrevendo" (SplitText / DrawSVG), selo de cera quebrando, envelope voando, rotas sendo traçadas,
    estandartes entrando, números contando, partículas de poeira na luz da vela (leves).
  - Seções "pinned" com narrativa guiada pela rolagem nos momentos-chave (carta → ordem; ciclo econômico).
  - Microinterações em todos os botões e cartões (hover, foco, clique).
  - **`prefers-reduced-motion`:** tudo continua bonito e legível com animações desligadas.
- **Vídeo:** um trailer de 45–60 s (montado dos clipes reais, legendado, sem áudio obrigatório) em modal ou seção; os
  loops curtos em `<video autoplay muted loop playsinline>` com poster, carregados sob demanda.

## Técnica

- **Site estático**, sem servidor: hospedável grátis no Cloudflare Pages ou GitHub Pages. Pode ser HTML/CSS/JS puro
  com Vite, ou Astro (bom para i18n e performance). Justifique a escolha em uma linha.
- Pasta do projeto: `_Modding\site\` (código-fonte + `README.md` com como rodar, gerar e publicar).
- **Performance é parte do design:** LCP < 2,5 s em 4G, CLS ≈ 0, imagens em AVIF/WebP com tamanhos responsivos,
  vídeos comprimidos e preguiçosos, JS de animação carregado depois do conteúdo, fontes com `font-display: swap`.
  Meta: Lighthouse ≥ 90 em performance e ≥ 95 em acessibilidade, boas práticas e SEO.
- **Celular primeiro:** perfeito em 375 px (sem rolagem lateral), tablet e desktop até 2560 px. As interações viram
  versões de toque no celular.
- **Acessibilidade:** contraste AA, navegação por teclado, foco visível, textos alternativos descritivos, legendas.
- **Compra:** botão da Lemon Squeezy (checkout em overlay com `lemon.js`) com a URL do produto numa constante
  (`CHECKOUT_URL`), por enquanto um placeholder claramente marcado. Preço numa constante também.
- **SEO e compartilhamento:** título, descrição, Open Graph e Twitter card com uma imagem 1200×630 bonita (a carta
  selada + nome), favicon (selo de cera), `sitemap.xml`, `robots.txt`, dados estruturados `Product`/`SoftwareApplication`
  (sem avaliações inventadas).
- **i18n preparado:** textos em um arquivo por idioma (`en` agora; `pt`, `es`, `fr`, `de` depois), sem texto preso no
  HTML.
- **Analytics:** nenhum por padrão. Se o lucas quiser, um sem cookies (Cloudflare Web Analytics ou Plausible); pergunte.
- **Páginas extras:** `/privacy` (use o rascunho da seção 3 de `docs\pedidos-provedores.md`), `/terms` (sem cláusula
  genérica proibindo engenharia reversa — exigência da LGPL do BepInEx, veja `pesquisa-instalador-exe.md` §6),
  `/changelog`, `/install` (guia detalhado, com o print do SmartScreen explicado e a seção do Steam Deck).

## Fases (pare e converse nos pontos marcados)

1. **Leitura e inventário** (sem construir nada): leia os docs, extraia as melhores cartas reais (com nação, turno,
   idioma e de onde saíram), inventarie os prints/clipes que existem e os que faltam, e confira os números.
   → **Converse com o lucas:** mostre as 10 melhores cartas, a lista de provas, a narrativa (seções acima, ajustada) e
   as perguntas:
   - preço final e se há versão de demonstração (o que funciona sem a chave);
   - política de reembolso;
   - domínio (já tem? sugestões);
   - e-mail de contato;
   - se pode rodar a Teste16 para gerar cartas/prints novos em inglês;
   - analytics sim/não;
   - data de lançamento / "em breve" com lista de espera (a Lemon Squeezy tem captura de e-mail) ou venda já.
2. **Direção de arte:** 2–3 direções visuais (hero de cada uma, com o motion principal funcionando). → **O lucas
   escolhe.**
3. **Construção** da página completa, seção por seção, vendo cada uma no navegador embutido (Browser pane) em
   desktop e em 375 px, com prints de conferência.
4. **Capturas que faltam** (prints e clipes reais com 16 impérios), feitas com o kit de dev — só com o jogo livre e
   sem outra sessão mexendo nele.
5. **Polimento:** Lighthouse, reduced-motion, teclado, celular, imagens, OG image, revisão do texto em inglês (natural,
   curto, concreto; sem jargão de marketing vazio).
6. **Entrega:** mostre a página rodando, com o print de cada seção, e escreva `_Modding\site\README.md` e
   `docs\landing-page.md` (decisões, onde trocar textos/preço/link, como publicar). Atualize a memória do projeto.

## Regras que não se quebram

- **Nada sai deste PC sem permissão explícita:** publicar o site, criar conta (Cloudflare, GitHub, Lemon Squeezy),
  comprar domínio, enviar formulário, subir vídeo. Prepare tudo e peça.
  Publicar um preview privado como Artifact também só com o ok do lucas.
- **Nunca** leia, copie, mostre ou coloque na página chaves e credenciais (`BepInEx\config\deepseek.key`, a pasta
  `BepInEx\config\credenciais\`). Logs e dossiês podem ir como prova só depois de conferir que não há nada pessoal.
- **Não mexa no código do mod** nem na instalação do jogo. Captura de tela pelo kit de dev é permitida (é leitura),
  mas nunca rode ao mesmo tempo que outra sessão que esteja instalando ou testando no jogo, e nunca use
  `ia reset`.
- Sem depoimentos, números ou escassez falsos. Tudo verificável.
- Não discuta as regras do jogo (EULA da SEGA) nem a legalidade de vender: o lucas já decidiu. O aviso de "não
  oficial" no rodapé basta.
- Fale com o lucas em pt-BR; a página fica em inglês.
