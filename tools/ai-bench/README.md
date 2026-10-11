# Bancada da IA (ai-bench)

Mede custo e qualidade das decisões das nações **fora do jogo**, reexecutando decisões reais dos logs. Toda mudança de prompt,
de dossiê ou de modelo passa por aqui antes de ir para o jogo. Não vai no pacote de venda.

## Compilar

```bash
"C:\Program Files\dotnet\dotnet.exe" build -c Release -o bin\out
```

A credencial do DeepSeek é lida pelo mesmo código do mod (`Credentials.cs`, DPAPI). A bancada nunca mostra nem grava a chave.

## Comandos (`bin\out\AiBench.exe ...`)

| Comando | O que faz | Custo |
|---|---|---|
| `simulate "base,order"` | Para cada variante: tamanho da entrada e quanto cairia no cache no jogo (prefixo igual ao do pedido anterior da mesma nação ou ao de outra nação no turno), sobre o corpus inteiro. Mostra também quais seções ficam iguais de um turno para o outro. | zero |
| `run <variante> <reps> [low] [scenarios.txt]` | Roda a variante nos cenários fixos, `reps` vezes. Guarda resposta, raciocínio, uso e métricas em `results\<variante>\`. Não refaz o que já existe. | ~US$ 0,004 por chamada |
| `table "base,short+lean"` | Compara as variantes já rodadas: % de json válido, cortes, entrada, cache simulado, raciocínio, custo no pico, cartas, ações, nomes desconhecidos. | zero |
| `blind <A> <B> "1,3"` | Gera `results\blind_A_vs_B.md` com os pares lado a lado como A/B sorteados. A chave fica em `.key.json`: dê as notas antes de abrir. | zero |
| `probe <log.json,...> "nested:low,top:low"` | Compara formas de mandar o corpo (usada para provar o bug do `reasoning_effort`). | por chamada |
| `score` | Gasto total da bancada (`placar.json`). | zero |

## Variantes (`src\Variants.cs`), combináveis com `+`

- `base`: o prompt do log como foi.
- `order`: o dossiê na ordem do cache (o que muda pouco primeiro).
- `short`: a seção "COMO PENSAR" no sistema.
- `lean`: a correspondência antiga só com cabeçalho e assunto.
- `stimulus:offense|threat|gold`: insere uma carta de teste (`src\Flaws.cs`, cenário T110_E4).
- `flaw:vain/coward/greedy/naive/paranoid/impulsive/stubborn/lazy/spoiled/softhearted`: insere defeitos de líder na persona (protótipo, `src\Flaws.cs`).

## Corpus

`corpus\<partida>\` guarda cópias dos logs do jogo (`BepInEx\DiplomaciaIA\logs`). Copie para cá as partidas boas, porque o
jogo apaga os logs com mais de 30 turnos. Os cenários fixos estão em `scenarios.txt` (16 impérios: `scenarios16.txt`), e os de defeito em
`scenarios-flaws.txt`.

## Cuidados

- O cache do DeepSeek dura dias e depende do nível de raciocínio. Por isso a tabela usa o cache **simulado**, e não o do
  servidor, que depende de quem rodou antes.
- A variação entre chamadas é grande: o raciocínio vai de 500 a 9.000 tokens no mesmo cenário. Use pelo menos 4 repetições
  antes de concluir.
- Se você rodar várias variantes em paralelo (processos separados), o placar pode perder alguma soma: o arquivo não tem trava
  entre processos.
