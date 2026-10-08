# VOZES (Gemini TTS via OpenRouter)

Modelo `google/gemini-3.8-flash-tts`, endpoint `/api/v1/audio/speech`, chave `OPENROUTER_API_KEY` no `_Modding\.env`.
Saída só PCM 24 kHz 16-bit mono. Regerar tudo: `set -a; . ../../../.env; set +a; bash voz-teste/falas.sh` (de dentro de `video-60s`).
`voz-teste\wav\` tem os WAV já aparados (silêncio de início/fim cortado). ElevenLabs não existe no OpenRouter.

| Arquivo | Quem | Voz | Estilo | Dur. | Janela |
|---|---|---|---|---|---|
| 01_solomon | Solomon | Fenrir | warm, regal, calm | 2,6 s | 2,4 s |
| 02_diario | Diário | Kore | whispering, sly | 4,3 s | 2,1 s ⚠ |
| 03_n_now | Narrador | Charon (aprovada) | low, ominous | 3,9 s | 1,2 s ⚠ |
| 04_n_fog | Narrador | Charon | idem | 4,4 s | 2,4 s ⚠ |
| 05_n_remembers | Narrador | Charon | idem | 1,2 s | 1,2 s |
| 06_n_grudges | Narrador | Charon | idem | 1,5 s | 1,2 s |
| 07_n_lies | Narrador | Charon | idem | 1,3 s | 2,4 s |
| 08_muguiying_a | Mu Guiying | Gacrux | cold, controlled | 5,7 s | 4,2 s ⚠ |
| 09_muguiying_b | Mu Guiying | Gacrux | quiet, final | 1,0 s | 1,4 s |
| 10_n_doesit | Narrador | Charon | idem | 1,9 s | 3,6 s |
| 11_zenobia | Zenobia | Leda | fierce, proud | 2,6 s | 3,0 s |
| 12_n_nation | Narrador | Charon | idem | 3,6 s | 3,0 s ⚠ |
| 13_nagamma | Nagamma | Despina | conspiratorial, low | 3,4 s | 3,6 s |
| 14_n_north | Narrador | Charon | idem | 1,7 s | 2,4 s |
| 15_n_montage | Narrador | Charon | urgent, fast | 6,4 s | 10 s |
| 16_n_courts | Narrador | Charon | low, slow, intimate | 3,4 s | 4,0 s |
| 17_n_answer | Narrador | Charon | low, slow, final | 1,5 s | 1,6 s |

## Aprendizados
- Sem `brisk, no pauses` a voz fica 2-3x mais lenta (Solomon foi de 6,6 s para 2,6 s). O parâmetro `speed` é ignorado.
- Reticências `...` no texto viram pausas longas; usei vírgula.
- ⚠ = a fala passa da janela do ROTEIRO. Regra "áudio primeiro": os tempos do roteiro cedem. Opções: encurtar a fala
  (ex.: 03 "This is the AI in HUMANKIND." sem "now"), esticar a cena, ou acelerar 10-15% no build.
- Vozes dos líderes (Fenrir, Gacrux, Leda, Despina) e do diário (Kore) ainda não foram ouvidas pelo Lucas; só a Charon foi aprovada.

## Rodada 2 (2026-10-07): tempos reais e retime
Lucas aprovou todas as vozes. A geração varia entre tomadas (o diário foi de 4,1 s a 7,8 s com o mesmo texto): gerar 3-4 tomadas
e ficar com a mais curta (`voz-teste\takes\`). `aparar.ps1` corta o silêncio e imprime as durações.

| Fala | Texto final | Dur. |
|---|---|---|
| 02 diário | texto real completo (sem cortes) | 4,1 s |
| 03 narrador | "This is the AI in HUMANKIND." (sem "now") | 2,6 s |
| 04 narrador | "It sees only what it knows." (igual ao texto da tela) | 2,6 s |
| 08 Mu Guiying | sem reticências | 5,3 s |
| 12 narrador | "Not a chatbot. A nation deciding." | 2,9 s |

### Retime do ato 1 e 2 (o áudio manda)
- Ato 1 passa de 0-6,0 s para **0-9,6 s**: Solomon 0-2,7 | silêncio 0,4 s | lacre racha 3,1 | diário 3,3-7,4 | HE'S LYING 7,4 + narrador 7,6-10,2 sobreposto ao fim.
- Ato 2 encolhe para **9,6-15,6 s** (frases curtas, uma por 1,5 s; "It sees only what it knows" começa antes do match cut).
- O ganho vem do ato 6: o mosaico do ato 5 cai de 2,8 s para 1,6 s e o convite final ganha o resto. Ato 3 em diante só desloca ~+1,2 s e se refina com Whisper no build.
- Tempos finais só valem depois de medir BPM da trilha; a trilha ainda não foi escolhida.
