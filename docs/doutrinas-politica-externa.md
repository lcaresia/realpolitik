# Doutrinas de política externa das nações de IA

Pedido do Lucas (2026-10-12): diversificar as nações com base em pesquisa, e não em sorteio inventado. Existem
isolacionistas, quem joga em grupo, quem joga em grupos regionais, quem se guia pela religião e quem só fala com o
próprio povo.

## Pesquisa (resumo)

**Teoria de relações internacionais:**
- **Holsti (1970), "National Role Conceptions":** cerca de 17 papéis nacionais, como líder regional, protetor,
  isolado, aliado fiel, mediador, defensor da fé, bastião da revolução e independente ativo. Cada país tem 1 ou 2
  papéis dominantes.
- **Mead, *Special Providence*:** quatro escolas de política externa: comercial (hamiltoniana), moral e
  institucional (wilsoniana), "cuidar de casa" (jeffersoniana) e honra guerreira (jacksoniana).
- **Walt, *Origins of Alliances*:** os Estados se aliam contra a **ameaça** (poder, proximidade, capacidade
  ofensiva e intenção). Balancear é a regra; aderir ao mais forte é coisa de fraco. Religião e cultura pesam, mas
  cedem diante da ameaça. Exemplo: a França católica aliada aos otomanos contra os Habsburgo, em 1536.

**História:**
- isolamento esplêndido britânico (1815–1902);
- Sakoku japonês (1639–1854);
- sistema tributário chinês;
- não alinhados (Bandung, 1955);
- balanceamento à distância (*offshore balancing*);
- pan-movimentos: o pan-eslavismo como disfarce imperial; o pan-arabismo de Nasser contra o pan-islamismo saudita.

**Jogos:**
- **Stellaris:** sorteia uma personalidade por pesos, filtrada pelas éticas (Hegemonic Imperialists, Xenophobic
  Isolationists, Federation Builders, Evangelizing Zealots, Honorbound Warriors, Peaceful Traders…), com
  multiplicadores de agressão, comércio e coragem. Os desenvolvedores contam que sem isso "todos os impérios parecem
  iguais".
- **Civ VI:** cada líder tem uma agenda fixa e uma oculta, sorteada, que só aparece com espionagem.
- **Total War:** confiabilidade global; quebrar um acordo derruba a reputação com todos.
- **CK3:** valores ocultos de honra, racionalidade, vingança e zelo.
- **Humankind:** os traços do avatar são eixos (aliado fiel × traidor etc.). A doutrina fica uma camada acima: o
  traço diz **como** a nação age, e a doutrina diz **com quem e para quê**.

**Fontes principais:**
- Holsti, via teses: https://myweb.uiowa.edu/bhlai/workshop/role.pdf
- Stellaris: https://stellaris.paradoxwikis.com/AI_personalities
- Civ VI: https://www.pcgamesn.com/civilization-vi/leaders-agendas
- Total War: https://totalwarwarhammer.fandom.com/wiki/Reliability
- Humankind (avatar AI): https://community.amplitude-studios.com/amplitudestudios/humankind/blogs/751-humankind-feature-focus-09-avatar-ai
- Isolamento esplêndido: https://en.wikipedia.org/wiki/Splendid_isolation
- Sakoku: https://www.britannica.com/topic/sakoku
- Não alinhados: https://en.wikipedia.org/wiki/Non-Aligned_Movement
- Balanceamento à distância: https://en.wikipedia.org/wiki/Offshore_balancing
- Pan-eslavismo: https://encyclopedia.1914-1918-online.net/article/panslavism/

## As 13 doutrinas

Cada doutrina tem uma essência, o apetite por blocos, os parceiros que prefere, a guerra, o comércio, o Congresso,
como trata promessas, o que teme e o que a faz mudar. Os detalhes estão em `Prompts.Doctrines` (o texto vai para a IA).

| Doutrina | Essência | Exemplos | Análogo em jogo |
|---|---|---|---|
| Isolacionista | o mundo é perigoso; basta-se a si mesma | Sakoku, Joseon, Albânia | Xenophobic Isolationists |
| Construtora de coalizões | segurança coletiva; regras valem para todos | Liga das Nações, Concerto Europeu | Federation Builders |
| Potência regional | minha vizinhança, minhas regras | Doutrina Monroe, Índia | "Big Stick" (Civ VI) |
| Defensora da fé | a fé decide quem é amigo | Santa Liga, Irã pós-1979 | Evangelizing Zealots |
| Irmandade de sangue | só confia nos "nossos" | pan-eslavismo, pan-arabismo | opinião por cultura (CK3) |
| Imperialista | expandir é o destino | Roma, Império Britânico | Hegemonic Imperialists |
| Mercantil | lucro acima de bandeiras | Veneza, Holanda, Hansa | Peaceful Traders |
| Oportunista | sem amigos permanentes, só interesses | Inglaterra, Saboia, Itália 1915 | agenda oculta (Civ VI) |
| Satélite do forte | segurança à sombra de um grande | tributários da China, Pacto de Varsóvia | agenda de Cleópatra |
| Neutra / ponte | sobreviver sendo útil a todos | Suíça, Suécia, não alinhados | Peacekeeper |
| Cruzada ideológica | exportar o próprio modelo | França de 1792, Komintern | Democratic Crusaders |
| Honra guerreira | respeita a força; ofensa se paga | Esparta, mongóis | Honorbound Warriors |
| Suserana tributária | quer vassalos que paguem | China imperial, Liga de Delos | vassalos (EU4/Stellaris) |

## Sorteio

**Pesos-base:**

| Doutrina | Peso |
|---|---|
| Oportunista | 11 |
| Mercantil | 10 |
| Potência regional | 10 |
| Construtora | 9 |
| Imperialista | 9 |
| Satélite | 8 |
| Fé | 8 |
| Neutra | 7 |
| Irmandade | 7 |
| Isolacionista | 6 |
| Honra | 6 |
| Suserana | 5 |
| Cruzada | 4 |

**Os traços nativos multiplicam os pesos:**
- religioso fervoroso: Fé ×3;
- militarista, gosta de atacar cedo ou colecionador de territórios: Imperialista ×2,5;
- traiçoeiro ou mercenário: Oportunista ×2,5;
- defensivo ou obcecado por fortificações: Isolacionista e Neutra ×2;
- pacifista ou amante da paz: Neutra e Construtora ×2; Imperialista e Honra ×0,3;
- leal aos amigos ou obstinado nos compromissos: Satélite, Construtora e Honra ×1,8; Oportunista ×0,4;
- voltado ao mar, explorador ou amante do luxo: Mercantil ×2.

**Limites:** cada doutrina aparece no máximo 3 vezes (com 16 nações). Sempre há pelo menos uma Construtora e uma
Imperialista, para haver tensão.

A doutrina é fixa na partida, sorteada de forma estável (pela semente da partida e da nação). A IA pode mudar de
postura quando o gatilho "o que a faz mudar" acontecer; a doutrina original continua sendo a raiz.
