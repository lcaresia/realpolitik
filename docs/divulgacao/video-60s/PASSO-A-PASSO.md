# Passo a passo completo: do vídeo final ao lançamento nos 5 canais

Canais: r/humankind, Discord oficial de HUMANKIND, Games2Gether, r/4Xgaming, r/aigamedev (r/LocalLLaMA fica de fora).
Textos: `TEXTOS.md` (vídeo) e `..\kit-lancamento.md` (textos longos e respostas prontas). Quem faz cada passo está entre colchetes.

## FASE 0: fechar o vídeo [Claude]
1. Master vertical 9:16 (Shorts): em andamento (`make_vertical.sh` gera `build_v\`). Conferir os frames-chave e corrigir.
2. Renderizar os dois masters em **60 fps** (16:9 e 9:16), normalizados em −14,5 LUFS, pico ≤ −1 dBFS.
3. Substituir os planos provisórios por **gravações reais do jogo** quando houver (hoje a cena do exército é um mapa ilustrado
   sobre um print real). [Lucas grava no jogo, em 4K com DSR; lista de planos em `ROTEIRO.md`.]
4. Trilha: a atual é gerada (Lyria 3 Pro via OpenRouter, US$ 0,08). Confirmar se a licença serve para vender o mod; se preferir
   uma faixa humana, [Lucas baixa do Pixabay Music, licença comercial sem atribuição] e eu a encaixo.
5. Assistir os dois vídeos inteiros, com fone, antes de aprovar. [Lucas]

## FASE 1: consentimento da Amplitude (bloqueia tudo) [Lucas]
6. Enviar o e-mail do `kit-lancamento.md` §7 pelo formulário de suporte ou e-mail de imprensa/parcerias da Amplitude.
   Anexar o vídeo de 60 s como rascunho (mostra o que será divulgado).
7. Anotar a data de envio. Enquanto não houver resposta: **nada com imagens do jogo vai ao ar** (EULA §10).
8. Se responderem "sim": guardar o e-mail (vira argumento de venda e responde "Is it allowed by Amplitude?").
   Se "não" ou com condições: parar, ajustar o vídeo (ou usar só os clipes recriados do site) e só então publicar.
   Se ficarem dias sem responder: decisão sua (esperar mais, ou publicar só o que não usa imagens do jogo).

## FASE 2: preparar o terreno (pode fazer enquanto espera) [Lucas, com ajuda]
9. **Publicar o site** com os avisos "contato em breve" já removidos: `site\tools\deploy-cloudflare.ps1`.
10. **Ligar o analytics** (token do Cloudflare Web Analytics ainda é placeholder). Sem isso os `?ref=` não dizem qual canal vende.
11. **Conta do Reddit com histórico:** por alguns dias, comentar de verdade em r/humankind e r/4Xgaming (sem link). Conta nova
    postando link cai no filtro de spam.
12. Ler as **regras da barra lateral** de r/humankind, r/4Xgaming e r/aigamedev (flair de mod/fan content, produto pago, vídeo).
13. Criar/conferir o canal do **YouTube** (vídeo 16:9 em "não listado" e o Short). Preencher título, descrição, tags e
    comentário fixado do `TEXTOS.md`. Deixar agendado ou não listado até a liberação.
14. Ter à mão: a página da loja funcionando, a calculadora de custo, o e-mail de suporte e as **respostas prontas** (§6.1).

## FASE 3: publicar, um canal por dia (só após o "sim")
**Regra:** nunca todos no mesmo dia. Depois de cada post, ficar **2 horas** respondendo. Não brigar; agradecer crítica e
responder com fatos.

| Dia | Canal | O que fazer |
|---|---|---|
| D0 | YouTube + Shorts | Tornar públicos. Fixar o comentário. Copiar os links dos dois. |
| D0 | Discord oficial | Primeiro a **DM ao moderador** (kit §3, mais a linha do vídeo). Não postar nada antes da resposta. |
| D0 | Games2Gether | Criar o tópico longo (kit §4) com a linha `▶ 60-second overview` e o link do YouTube no topo. É a página permanente de produto e suporte. |
| D+1 | r/humankind | Postar o vídeo 16:9 com o título e a abertura do `TEXTOS.md`; o texto do kit §2 vai abaixo. **Link só no 1º comentário** (`?ref=r-humankind`). Postar o comentário logo em seguida. |
| D+2 | r/4Xgaming | Post de discussão (kit §5) com o vídeo ou `site_mail.jpg`, as 3 perguntas e o link no 1º comentário (`?ref=r-4x`). |
| D+3 | r/aigamedev | Relato técnico (kit §6), **sem vídeo**. Conferir antes se o sub aceita produto pago; se não, tirar o último parágrafo e dar o link só a quem pedir. |
| quando liberarem | Discord oficial | Post curto no canal indicado, com o vídeo ou 1 imagem e `?ref=discord`. |

Passo a passo de cada post no Reddit:
1. Entrar com a conta que já tem histórico. 2. Criar post → escolher vídeo (ou imagem/texto) → colar o título.
3. Colar o texto → escolher o flair de mod/fan content → publicar. 4. Em seguida, comentar o link (`?ref=...`).
5. Marcar a hora e ficar 2 h respondendo. 6. Registrar o link do post.

## FASE 4: depois dos posts [Lucas]
15. Responder tudo nas primeiras 2 h e depois uma vez por dia por uma semana.
16. Conferir no analytics qual `?ref=` trouxe visitas e vendas. Dobrar a aposta no que funcionou.
17. Se algum comentário trouxer bug ou dúvida repetida, atualizar a FAQ do site e o tópico do Games2Gether.
18. Guardar números (visitas, vendas, upvotes) para decidir o próximo canal (r/LocalLLaMA só quando houver modelo local).

## O que eu não faço
Não envio o e-mail à Amplitude, não posto em nenhuma rede, não pago nada e não baixo música sem a sua aprovação.
