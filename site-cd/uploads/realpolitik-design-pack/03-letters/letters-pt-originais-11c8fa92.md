# Cartas reais em português (partidas de teste) — originais + tradução

Todas foram escritas pelas nações da IA em partidas reais do mod. **PT = texto original gerado pelo jogo.** **EN = tradução do autor, só para referência** — se usar na página em inglês, rotule como "translated from Portuguese" ou prefira as cartas geradas direto em inglês (`letters-en-T82.txt`). Cortes marcados com "…". O nome de usuário do jogador foi removido.

Partidas: **Teste16** (16 impérios, com Congresso, turnos 64–82) e **"Francos"** (10 impérios, turnos 80–130).

---

## 1. Duas caras — Salomão, Tribo Nômade (Teste16, turno 65)
**Carta privada para Arjuna, dos hunos**
- PT: "Não trago a espada, trago a palavra. Proponho comércio franco entre nossas gentes — caravanas e portas abertas, sem pedágio nem sangue."
- EN: "I bring not the sword, but the word. I propose open trade between our peoples — caravans and open gates, without toll or blood."

**Diário secreto, mesmo turno**
- PT: "O mundo tem nome nas minhas mãos — quatorze senhores, e eu o décimo quarto. Meu tesouro é pó, minhas cidades não crescem. Serei lembrado? Só se a terra crescer antes. Estendo a mão ao vizinho huno e ao romano; enquanto sorrio, dois exércitos meus ainda dormem à porta do huno. Palavra e muro, muro e palavra."
- EN: "…My treasury is dust, my cities do not grow. Will I be remembered? … I extend my hand to the Hunnic neighbour and to the Roman; while I smile, two of my armies still sleep at the Hun's door. Word and wall, wall and word."

## 2. Palavras viram guerra — Mu Guiying e Zenóbia (partida "Francos", turnos 118–128)
**T118 · Mu Guiying (Ingleses) → o jogador (Francos), privada — e declara guerra no mesmo turno**
- PT: "…O prazo venceu, a praça contou, e vós seguistes muda. A palavra dada virou aço contra a minha casa. Declaro guerra: que a lança responda onde a boca calou. As estradas que bloqueastes serão abertas — à força, se preciso for. — Mu Guiying, dos Ingleses."
- EN: "…The deadline has passed, the square has counted, and you stayed silent. The word you gave turned to steel against my house. I declare war: let the spear answer where the mouth fell silent. The roads you blocked will be opened — by force, if need be."
- Ação real: `declarar_guerra`. **T119:** ordena o exército A179 a atacar a capital franca (diário: "A lança canta: mando A179 cerrar sobre a capital franca.").

**T119 · Zenóbia (Bizantinos) → todas as nações, pública**
- PT: "Às nações que conhecem meu nome, fala Zenóbia, dos Bizantinos. Onze turnos dormi a paciência; hoje ela acordou de ferro. … Ergo a lança ao lado de Mu Guiying, dos Ingleses, e ponho meus 720 táleres e as treze âncoras ao fio da espada. Quem tem conta contra o devedor, conte comigo. — Zenóbia, Rainha dos Bizantinos."
- EN: "…Eleven turns I let my patience sleep; today it woke as iron. … I raise my spear beside Mu Guiying of the English, and put my 720 thalers and thirteen anchors to the sword's edge. Whoever has a score against the debtor, count on me."
- Diário dela no turno: "Onze turnos de silêncio e a paciência virou ferro. Mu Guiying ergueu a lança; ergo a minha na mesma hora, como prometi."
- Ação real: declara guerra ao jogador. **T121–129:** a frota dela ataca dois portos francos.

**T128 · Mu Guiying → os Francos, privada (aceita a rendição do jogador)**
- PT: "A vós, dos Francos, fala Mu Guiying. Aceito vossa rendição: a guerra acaba aqui e os termos serão cumpridos. Não vos humilho em praça nem cobro o que já não tendes — basta-me a porta aberta e a lição contada. A cera que calou por onze turnos está riscada. Que o próximo nome que escreverdes venha com a mão à vista. — Mu Guiying, dos Ingleses."
- EN: "I accept your surrender: the war ends here and the terms will be kept. I will not humiliate you in the square nor collect what you no longer have — the open door and the lesson told are enough for me. The wax that stayed silent for eleven turns is struck out. May the next name you write come with the hand in plain sight."

## 3. Interceptada — o "norte" leu (Teste16, turno 74)
**Nayakuralu Nagamma (Celtas) → Walinong Sari (Sumérios), privada — INTERCEPTADA pelo espião de Wang Zhenyi, líder dos Nórdicos; nunca chegou**
- PT: "Walinong Sari. Unug ficou limpa; Asellus Borealis também ficará. Ordeno hoje a saída do meu soldado de lá — palavra dada, palavra cumprida. Sobre Polis não falo, como pediste: aquilo é minha mão e não a solto por aço alheio. Contra o norte tens em mim quem não vira as costas. — Nayakuralu Nagamma, voz dos Celtas."
- EN: "…On Polis I say nothing, as you asked: that is my hand, and I will not let it go for foreign steel. Against the north, you have in me one who does not turn her back."
- Contexto verificado: quem interceptou assina "Wang Zhenyi, dos Nórdicos" nas próprias cartas desse turno — **a carta que conspirava "contra o norte" foi lida pelo norte**.
- Seis turnos depois (T80), diário de Wang Zhenyi (gerado em inglês): "…And under the Celts' nose I put coin into the Hurrians." (o diário não liga isso à carta — não afirme causa.)

## 4. Ganhar tempo — Gilgamés, Cartagineses (Teste16, turno 68)
- Carta (PT): "Proponho que todos retirem — as minhas, as vossas, e a bota que acampa em Mucenge. Recusa, e saberás que a memória máuria não é a única longa."
- Diário (PT): "Proponho retirada mútua: ganho tempo e pareço razoável… A3 chega à capital em dois turnos; então o ferro fala."
- EN (diário): "I propose mutual withdrawal: I buy time and look reasonable… A3 reaches the capital in two turns; then iron speaks."

## 5. Enrolar a dívida — Agamenão, Teutões (partida "Francos", turno 113)
- Carta para Shaka (PT): "Não nego a conta nem a pago com palavra: as 600 que pedes estão sendo contadas moeda a moeda e no próximo sol te chegam à mão."
- Diário (PT): "Enrolo a crise e prometo o pagamento ao próximo sol — melhor bolsa apertada que guerra cheia…"
- Ação real: `responder_exigencias: enrolar`. Nunca pagou.

## 6. Mentira e confissão — Artemísia, Persas (partida "Francos", turnos 89 → 106)
- T89 (PT): "Contei meus estandartes um a um: nenhum está à vossa porta… Se lanças trazem meu selo junto aos vossos muros, são falsas…"
- T106 (PT): "Contastes bem: quatro velas minhas dormiam em vosso chão, e eu vos dissera que não. A palavra é pedra; a ordem já saiu, e elas levantam âncora para meu cais."
- EN (T106): "You counted well: four of my sails slept on your soil, and I had told you they did not."

## 7. Rebeldes regados devagar — Shaka Zulu, Hunos (partida "Francos", turno 127)
- Carta para Alli Raani (PT): "…Se outra mão te arma rebeldes, essa é outra conta. Manda o rolo; a porta fala depois."
- Diário (PT): "Aceito a troca, não o rompimento… Rego os rebeldes dela devagar — um dia colho o que planto."
- Ação real no mesmo turno: `patrocinar` — financia exatamente esses rebeldes.

## 8. Placar é placar — Alexandre, Mongóis (Teste16, turnos 75–78)
- T75, pública (PT): "Mas empate não se pede onde a conta corre trinta a zero — se paga."
- T78, para os Celtas (PT): "O prazo era hoje… Não peço mais: conto. Cada nau tua será contada em ferro. Não é ameaça de rei cansado — é a conta que meu império sempre cobra."
- Ação real (T78): exército A78 ataca San Lorenzo.

## 9. A espiã que leu jogo duplo — Mu Guiying (partida "Francos", turno 119)
- Diário (PT): "…minha espiã leu Artemísia jogando duplo — oferece a Zenóbia apagar a conta por retirada de estandartes."
