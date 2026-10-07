# BRIEF: vídeo de 60 s "Words Become Orders"

| Campo | Valor |
|---|---|
| Produto | Realpolitik: Living Nations for HUMANKIND (US$ 10, single-player, Windows) |
| Público | Quem joga ou jogou HUMANKIND/4X e acha a diplomacia da IA morta |
| **A única mensagem** | **A IA agora fala, mente e cumpre o que diz. Sai jogando.** |
| Emoção-alvo | Do "espera, ELE TÁ MENTINDO?" (3 s) até o "quero isso AGORA" (60 s) |
| Canais | YouTube (16:9 e Shorts 9:16), Reddit (16:9) |
| Duração | 60 s cravados (Shorts: o fim emenda no começo, em loop perfeito) |
| Formatos | Master A: 1920×1080 60 fps. Master B: 1080×1920 60 fps. Mesmas cenas, layouts diferentes |
| Idioma | Inglês. Todo texto falado aparece também na tela (o Reddit toca mudo) |
| Paleta | Fundo `#0C1522` (navy do site), texto `#F1EAD9` (pergaminho), **acento único `#C4245E`** (lacre, guerra, mentira). O dourado `#E2C172` só aparece porque está na UI do próprio jogo, nunca como cor de design |
| Tipografia | Títulos: serifa clássica pesada (Cinzel ou Trajan-like). Cartas: serifa itálica (EB Garamond Italic). Rótulos: Mulish (a do site) |
| Trilha | Orquestral híbrida escura, **100 BPM** (1 beat = 0,6 s, 1 compasso = 2,4 s), taiko e sub-bass, com um "drop" em 52 s |

## Regras que não se quebram

1. **Só falas reais.** Toda carta e todo diário vêm de partidas reais (os mesmos textos dos clipes do site, em
   `site\live\clips\clips.js`). Nada de inventar fala de IA. Na tela fica o selo discreto: *"Real game output"*.
2. **Toda imagem mostra o mod.** As telas vêm de gravação real do jogo com o mod. Os cartões de carta são
   recontagens animadas do texto real, como no site.
3. **Nada de upscale.** Ver "Captura" no ROTEIRO: gravar em 4K (DSR) ou usar o layout empilhado no vertical.
4. **EULA §10:** usar imagens do jogo para vender pede consentimento por escrito da Amplitude. O vídeo pode ser
   produzido já, mas **só vai ao ar depois da resposta** (ver `kit-lancamento.md` §0.1).

## Regras de motion (da pesquisa)

- Nada aparece só com fade: as coisas mudam de forma (o lacre racha, o papel vira, a tinta escreve).
- Nada de corte seco: cada cena nasce da anterior (match cut ou whip).
- Algo acontece em todo beat, e pousa com overshoot (spring, damping 12 a 15).
- Um movimento de câmera por vez. Nas pausas, "ambient idle" de 1 a 2% (respiração e drift).
- Silêncio de 0,3 a 0,5 s antes de cada revelação, e sub-bass na revelação.
- Mix: trilha a -18 dB embaixo da voz com ducking. Master em -14 LUFS. SFX com o silêncio inicial cortado.
- Tudo é função do frame: nada de `Math.random`, `Date.now`, CSS `transition`, `@keyframes` ou `mix-blend-mode`. Grain estável entre frames.
