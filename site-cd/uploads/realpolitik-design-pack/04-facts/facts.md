# Realpolitik: Living Nations for HUMANKIND — fatos conferidos

Conferidos contra a documentação e o código do mod em 2026-10-06. Use só o que está aqui.

## Números seguros
- **32** tipos de ação real que as nações executam no jogo (5 deles são do Congresso e exigem a DLC Together We Rule).
- Conselhos de **12** membros (11 pastas + a Mão do Trono), para o jogador e para cada nação da IA. Até 6 falam por turno; o jogador responde até 8 vezes por turno (120 palavras cada); apreço de −100 a 100.
- Correio com **7** seções; Banco Central com **4** abas; **3** modos por posto comercial (Livre, Pedágio, Bloqueado), definidos por império estrangeiro.
- Cartas: até **3** por nação por turno, até **150** palavras; 1 comunicado público a cada 5 turnos; entrega em 2, 1 ou 0 turnos conforme a era.
- Espionagem: cada espião lê no máximo 1 carta por turno; chance por carta limitada a 75%; carta interceptada **nunca chega**.
- Até **16** impérios (MoreEmpires opcional; testado em Tiny, Normal e Huge).
- **5** idiomas (pt, en, es, fr, de) — **1.192** textos de interface em cada um; as cartas também saem no idioma escolhido.
- Teto de gasto padrão: **US$ 5** por partida (ajustável).

## Custo (DeepSeek, `deepseek-flash`, único provedor medido de verdade)
- 10 impérios: ~**US$ 0,03 por turno** (US$ 0,39 por 14 turnos medidos).
- 16 impérios, fora do pico: **US$ 0,12–0,14 por turno** (medido: T77 US$ 0,136; T78 US$ 0,121). No pico (01–04 e 06–10 UTC em dias úteis): ~o dobro.
- Reunião do conselho do jogador: ~US$ 0,002–0,0035 por reunião.
- Estimativas (não medidas) para 16 impérios por turno: gpt-5-nano ~0,064 · gemini-2.5-flash-lite ~0,074 · glm-4.7-flashx ~0,069 · glm-5.3-flash ~0,10 · deepseek via OpenRouter ~0,19 · glm-4.7-flash **grátis**.
- **Falso:** "a partida inteira custa menos que um café" (com 16 impérios, US$ 5 duram ~40 turnos).
- OpenRouter grátis: 50 chamadas/dia; um turno de 16 impérios usa 21–28 chamadas → não serve para uma partida real.

## Requisitos e limites
- Windows · HUMANKIND na Steam (testado na versão 1.31.4836) · Epic: não testado · Steam Deck: experimental · Mac: não · Xbox/Game Pass: não.
- Só single-player (o mod se desliga sozinho em multiplayer).
- Saves abrem sem o mod, **exceto** saves com mais de 10 impérios (precisam do MoreEmpires).
- Desinstalar hoje: apagar duas pastas (instalador em preparação).
- A IA é paga pelo jogador ao provedor escolhido (ou grátis com GLM-4.7-Flash); se todos os provedores falham, a IA nativa do jogo assume.
- Privacidade: só o dossiê de cada nação (o que ela vê no mapa, relações, cartas e memória) vai ao provedor. Nada pessoal, nem nome nem conta Steam. Chaves e logins ficam criptografados no PC.

## Decisões comerciais
- US$ 10, pagamento único, venda já. Sem chave: tudo funciona menos a Diplomacia IA.
- Reembolso: 7 dias. Atualizações grátis (baixar de novo).
- Lemon Squeezy (checkout overlay), `CHECKOUT_URL` placeholder.
- GitHub Pages, sem domínio próprio. Cloudflare Web Analytics (token placeholder). E-mail de contato: placeholder.

## Não anunciar
- O provedor "ChatGPT (Codex)" (existe no código, mas não deve ser divulgado).
- Login real do OpenRouter e turnos pagos por provedores além do DeepSeek: ainda não testados de ponta a ponta.
