# Tradução do mod (pt, en, es, fr, de)

## Como funciona

- O texto em português do código é a própria chave da tradução (`src\CurrencyMod\L.cs`):
  - `L.T("Banco Central")` traduz;
  - `L.F("Juros de {0}%", taxa)` traduz e formata, com os números na cultura do idioma;
  - `L.N("Ministros")` só marca o texto, para listas estáticas. Traduza com `L.T` na hora de mostrar.
- As tabelas ficam em `src\CurrencyMod\Lang\en.json`, `es.json`, `fr.json` e `de.json`, no formato `{"texto em português": "tradução"}`. Elas vão dentro da DLL.
- **Tradução da comunidade:** um arquivo em `BepInEx\CurrencyModCore\lang\<código>.json`, se existir, passa por cima da tabela embutida. Serve para corrigir ou completar uma tradução sem recompilar.
- **Texto sem tradução** aparece em português, e o comando `idioma faltando` lista quais são.

## Configuração (`BepInEx\config\lucas.humankind.currency.cfg`)

| Chave | Valores | Efeito |
|---|---|---|
| `[Interface] Idioma` | `Auto`, `pt`, `en`, `es`, `fr`, `de` | Idioma das janelas do mod. `Auto` segue o idioma do jogo; um idioma que o mod não tem vira inglês. |
| `[IA] IdiomaDasCartas` | `Auto`, `pt`, `en`, `es`, `fr`, `de` | Idioma em que as nações da IA escrevem cartas, diário e falas do conselho. `Auto` segue o da interface. |

- **Em português,** o prompt da IA é o mesmo de sempre.
- **Nos outros idiomas:**
  - o dossiê e as regras continuam em português;
  - uma regra a mais manda escrever todo texto livre no idioma escolhido e deixar o json igual;
  - a checagem das cartas (`Grounding.cs`) conhece palavras comuns em en/es/fr;
  - em alemão, a regra de "nome próprio inventado" fica desligada, porque todo substantivo alemão tem maiúscula.

## O que é traduzido e o que não é

- **Traduzido:**
  - tudo o que o jogador vê nas janelas do mod: Banco Central, correio, aba Cartas, cartas interceptadas, conselho, Posto Comercial, painéis da diplomacia;
  - as linhas que o mod põe nas dicas nativas do jogo;
  - a reclamação "Bloqueio comercial".
- **Opinião dos ministros** (`CouncilEngine`): sai em duas versões. `Opinion` fica em português e vai para a IA; `OpinionUi` sai no idioma da interface e aparece na tela.
- **Fica em português de propósito:**
  - o dossiê e os prompts (o que a IA lê);
  - logs e comandos de desenvolvimento;
  - as descrições do `.cfg`;
  - o visualizador F10.
- **Ainda em português, pendente:**
  - os nomes padrão das moedas (Denário, Florim…) e o plural automático da moeda, que segue regras do português;
  - os erros técnicos da reunião do conselho (resposta vazia, falha de json);
  - o LEIA-ME.
- **Texto montado só quando a janela é criada** (títulos de abas e dicas fixas) troca de idioma só quando a janela é recriada: recarga do núcleo ou partida nova.

## Mexer em textos

1. Escreva o texto novo em português, já dentro de `L.T`, `L.F` ou `L.N`. O argumento tem que ser **um texto literal só**, sem `+` e sem `$"..."`, porque o extrator lê os literais.
   - **Frases inteiras:** `L.F("Você tem {0} cartas", n)`, nunca pedaços colados.
   - **Nome com artigo do português** ("dos Chou"): na interface use `GameText.OfUi` ou `InlineUi`. Fora do português eles devolvem só o nome, e a preposição fica na tradução.
2. Rode o extrator:
   ```
   powershell -ExecutionPolicy Bypass -File _Modding\tools\dev\extrair-textos.ps1 [-Idioma en] [-Relatorio]
   ```
   - os textos novos entram com tradução vazia;
   - os que saíram do código vão para `Lang\_removidos.txt`;
   - `-Relatorio` grava `Lang\_chaves.tsv`, com o arquivo de origem de cada texto.
3. Traduza os vazios e valide cada idioma:
   ```
   powershell -ExecutionPolicy Bypass -File _Modding\tools\dev\validar-traducao.ps1 -Idioma en
   ```
   O validador confere marcadores `{0}`, tags `<c=...>`, ícones `[money]`, chaves `{{ }}` e espaços nas pontas. Tem que terminar em "0 vazios, 0 com problema".
4. **Teste com o jogo aberto:** `idioma en|es|fr|de|pt|Auto` troca na hora (e grava no `.cfg`), e `idioma faltando` grava `dev\out\idioma_faltando_<código>.txt`. Tire prints das telas: botões e chips não quebram linha, então tradução longa corta. Encurte a tradução, como "Prod. ×{0}" em vez de "Produktion ×{0}".

## Escolhas de tradução

- **Glossário dos tradutores:** `docs\traducao-glossario.md`.
- **Inglês:** moeda de exemplo "Crown/Cr".
- **Alemão:** "du"; Runde, Ära, Zentralbank, Handelsposten, Maut, Rat, Ressort.
- **Francês:** "vous"; Banque centrale, Comptoir commercial, Péage, Conseil, grief.
- **Espanhol:** "tú"; Banco central, Puesto comercial, Peaje, Consejo, agravio.
