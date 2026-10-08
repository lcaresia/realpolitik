# PROMPT de produção (HyperFrames / Remotion)

## Três direções visuais (escolhida: A)

| | Direção | Visual | Por que fica ou sai |
|---|---|---|---|
| **A ✅** | **Sealed Dispatch** | Pergaminho, tinta que se escreve, lacre de cera vermelha, fundo navy com a gravação do jogo desfocada atrás. Carimbos para as viradas (HE'S LYING, I DECLARE WAR, INTERCEPTED). | **Fica.** É a linguagem do site e da própria mecânica (cartas e lacres). O carimbo vermelho é o único acento e marca cada revelação. |
| B ❌ | Intelligence File | Dossiê com tarjas pretas, máquina de escrever, papel de pasta, estética de thriller de espionagem. | Sai. Funciona bem só para o ato 4 e vira clichê no resto. Fica dela só o carimbo INTERCEPTED. |
| C ❌ | War Room | Mesa de mapa tática, linhas de radar, HUD militar moderno. | Sai. Puxa para jogo de guerra moderno e briga com a estética histórica do HUMANKIND. |

## Ordem de produção

1. **Áudio primeiro:** vozes e trilha (ver abaixo). Whisper gera os timestamps por palavra. Medir o BPM e o beat grid
   da trilha e recalcular os tempos do ROTEIRO pelo áudio real (os tempos do roteiro são o alvo, não a lei).
2. Gravar os 10 planos da lista de captura.
3. Storyboard, com aprovação do Lucas antes do build.
4. Build, lint/check, render e contact sheet (grade de frames a cada 1 s). Corrigir com prompts pequenos e pontuais.
5. Mix final a -14 LUFS e exportar os dois masters.

## Áudio

- **Vozes:** 1 narrador e 4 vozes de nação (Solomon, Mu Guiying, Zenobia, Nagamma) e 1 sussurro para o diário.
  Opções: gravar você mesmo, contratar um locutor no Fiverr (~US$ 30–60) ou usar TTS (ElevenLabs, com
  plano pago para uso comercial, ~US$ 5–22/mês). **Isso custa dinheiro, então a decisão é sua.**
- **Trilha:** orquestral híbrida escura a ~100 BPM, com um drop. Fontes: YouTube Audio Library (grátis, conferir a
  licença de cada faixa), Pixabay Music ou Artlist/Epidemic (pagas, licença comercial clara).
- **SFX:** craque de cera, carimbo, whoosh, tick de UI, taiko, sub-bass, papel virando e pena escrevendo.
  Cortar o silêncio do início de cada arquivo.

## Prompt (colar na skill `/product-launch-video` ou `/motion-graphics`)

```
/product-launch-video
Leia antes: _Modding\docs\divulgacao\video-60s\BRIEF.md e ROTEIRO.md. O ROTEIRO é a fonte da verdade
para cenas, textos e falas. Nunca invente falas: todas as cartas/diários são as do ROTEIRO, palavra por palavra.

Formato: 60s, 60fps, DOIS masters da mesma composição:
  - "h": 1920x1080 (YouTube + Reddit)
  - "v": 1080x1920 (Shorts). Zonas seguras: nada importante nos 15% de baixo nem nos 8% de cima.
  Cada cena é um componente com prop format "h"|"v"; textos e cartas se reorganizam, nunca só escalam.
  Gravações do jogo em ./capture/*.mp4 (3840x2160). Recorte vertical sem upscale; se a fonte for menor
  que o destino, use o layout empilhado (faixa 16:9 no meio).

Direção visual: "Sealed Dispatch". Pergaminho, tinta que se escreve, lacre de cera, carimbos.
Mood: escuro, histórico, tenso, com revelações secas.
Paleta: fundo #0C1522, texto #F1EAD9, acento ÚNICO #C4245E (lacre, carimbos, sublinhado da mentira).
  O dourado #E2C172 só existe dentro das gravações do jogo.
Fontes: títulos Cinzel 900; cartas EB Garamond Italic; rótulos Mulish 600.

Áudio:
  - trilha ./audio/track.wav: meça o BPM e gere o beat grid; todo corte e todo slam de texto cai no beat.
  - vozes ./audio/vo_*.wav: gere timestamps por palavra (Whisper); a tinta da carta escreve sincronizada
    com a palavra falada, e cada revelação cai na palavra que a descreve ("lying", "war", "read").
  - silêncios marcados no ROTEIRO (2,4s, 18,6s, 35,4s antes do carimbo, 51,7s) são sagrados:
    corte TODO o som, incluindo a trilha, por 0,3-0,4s.
  - sub-bass em: racha do lacre (2,7s), I DECLARE WAR (19s), INTERCEPTED (35,4s), drop (52s).
  - trilha a -18dB sob a voz com ducking; master -14 LUFS.

Estrutura (detalhe no ROTEIRO):
  Ato 1 (0-6s)   GANCHO: carta da paz -> silêncio -> lacre racha -> diário revela a mentira -> "HE'S LYING."
  Ato 2 (6-14.4s) COMO PENSA: match cut lacre->fronteira; IT SEES ONLY WHAT IT KNOWS / REMEMBERS / HOLDS GRUDGES / LIES
  Ato 3 (14.4-30s) PALAVRAS VIRAM ORDENS: carta de guerra -> carimbo I DECLARE WAR -> carimbo vira ícone
                   de guerra -> exército marcha (gravação real) -> Zenobia entra -> odômetro 0->32 com chips
  Ato 4 (30-40s) CONSPIRAÇÃO: carta Celtas->Sumérios viaja pela rota -> para -> INTERCEPTED -> tela de Inteligência
  Ato 5 (40-50s) MONTAGEM: 6 planos reais de 1,2s com whip e slam de texto, viram mosaico que respira
  Ato 6 (50-60s) CTA: mosaico implode num envelope "TO THE ONE WHO PLAYS" -> drop -> "Come and answer."
                 -> lockup REALPOLITIK + "$10" -> lacre final idêntico ao frame 0 (loop perfeito do Shorts)
  Selo fixo discreto num canto: "Real game output · Unofficial fan mod".

Movimento:
  - nada só com fade; nada de corte seco (match cut ou whip entre todas as cenas);
  - algo acontece em todo beat; pouso com spring (damping 12-15, stiffness 120-180);
    carimbos com spring bouncy (damping 8-10), escala 3->1;
  - stagger de 8-12 frames entre itens; um movimento de câmera por vez; um único camera shake (19s);
  - ambient idle 1-2% (respiração + drift) em toda tela parada;
  - push-in lento 4-8% nas cartas; whip nas trocas do ato 5.
Técnico: tudo em função do frame (sem Math.random, Date.now, CSS transition/@keyframes, mix-blend-mode);
  grain com seed fixa e estável entre frames.

Antes de codar: confirme a direção A com 3 frames-chave (0s, 19s, 57s) nos dois formatos e o storyboard
com os tempos recalculados pelo áudio real. Depois do render: contact sheet a cada 1s, dos dois masters.
```

## Vocabulário de parâmetros (adotado)

Easing: smooth `power2.out`, snappy `power4.out`, bouncy `back.out` (carimbos), dramatic `expo.out` (reveals hero),
dreamy `sine.inOut` (ambient idle). Durações: 0,2 s energia (whips, slams), 0,4 s padrão, 0,6 s e 1-2 s nos holds
das cartas. Câmera: push-in 4-8% nas cartas, parallax 2-3 planos no mosaico, rack focus no diário (carta nítida ->
diário nítido). Áudio reativo: sub-bass -> pulso de escala do lacre/carimbo; agudo -> brilho do lacre.
Skill reutilizável com isso tudo: `~/.claude/skills/motion-director-br`.
