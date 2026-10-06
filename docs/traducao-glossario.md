# Brief: translate the mod's UI table (Humankind mod)

Game: **Humankind** (Amplitude/SEGA 4X). The mod adds: per-empire currencies with inflation/interest/exchange (a "Central Bank" window), trade-post tolls/blockades, and "AI Diplomacy" where every AI nation is an LLM character writing letters (a diplomatic mail screen, a letters tab in diplomacy, intercepted letters in espionage, a player council of ministers).

Your file: `C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\src\CurrencyMod\Lang\<LANG>.json` — a flat JSON object `{"Portuguese source": ""}` with 988 entries, all values empty. Fill every value with the translation. **Never change a key** (keys are the exact Portuguese literals from the code, including spaces and punctuation).

Context for any key: `_Modding\src\CurrencyMod\Lang\_chaves.tsv` (tab-separated: source file, key). Grep the key in `_Modding\src\CurrencyMod` to see how it is used (button? tooltip? sentence with arguments?). Do look at the code for ambiguous short keys and for keys with `{0}`.

## Hard rules
1. Keep every placeholder exactly: `{0}`, `{1:0.0}`, `{2:N0}`… (you may reorder them within the sentence). Keep `{{` / `}}` literally.
2. Keep rich-text tags and game icons exactly, same count: `<c=E8685E>`, `</c>`, `<b>`, `</b>`, `<color=#...>`, `[money]`, `[influence]`… Translate the words between them.
3. Keep leading/trailing spaces and the final punctuation style (a key ending with "." → value ending with "."; "…" stays "…").
4. Same register as the source: short UI labels stay short (buttons/chips must fit — prefer the shortest natural wording), tooltips are 1–2 plain sentences, second person to the player (Portuguese "você" → EN "you"; ES "tú"; FR "vous"; DE informal "du", consistently).
5. Prepositions with names: some keys receive a name whose Portuguese form already carries the preposition/article ("dos Chou", "os Chou"). In your language the argument is the **bare name** ("the Chou"/"Chou"), so you add the preposition yourself. These keys are:
   - `Para {0}` — {0}=recipient name → "To {0}"
   - `Em resposta à carta {0} do turno {1}{2}.` — {0}=sender name (pt: "dos Chou"), {2}=optional quoted subject → "In reply to the letter from {0} of turn {1}{2}."
   - `Correspondência com {0}` — {0}=culture name → "Correspondence with {0}"
   - `{0} em {1}, terra {2}: {3}{4}.` — {2}=nation name (pt: "dos Chou") → see the code in InterceptedLettersTab.cs (~line 424) before translating.
   - `Estamos bloqueando ou cobrando pedágio das rotas comerciais {0} nos nossos postos comerciais.` — {0}=nation (pt: "dos Polinésios").
6. Singular/plural pairs exist as separate keys ("1 carta" / "{0} cartas"): translate each as written.
7. Ministers: the council data (portfolio names, titles per era, traits, voices) are flavour text; translate naturally and keep titles historically flavoured (e.g. "Chanceler" → Chancellor, "Grão-Vizir"-style titles keep their flavour). Feminine title keys exist too — use the feminine form in languages that have it.
8. Don't translate proper nouns of the game (empire/culture names are not in this table anyway), nor dev codes like E3, T31.

## Glossary (Portuguese → English; use the official Humankind term in ES/FR/DE when you know it, otherwise the natural one, and stay consistent)
- Banco Central → Central Bank; Câmbio → Exchange; cotação → exchange rate; moeda → currency; força da moeda → currency strength; Política (monetária) → Policy; juros → interest (rate); juros automáticos → automatic interest; inflação → inflation; deflação → deflation; desemprego → unemployment; crédito → credit; saldo → balance (treasury); dívida → debt; Ciclo → Cycle; diagnóstico → diagnosis; estabilidade → Stability (game term); dinheiro → Money (game term); influência → Influence (game term); indústria → Industry; renda → income.
- Posto Comercial → Trading Post; posto(s) → post(s); rota comercial → trade route; Livre → Open; Pedágio → Toll; Taxar → Toll (verb, button); Bloquear/Bloqueado → Block/Blocked; Liberar → Open (button); desviar/desvio → reroute/detour; Bloqueio comercial → Trade Blockade (grievance name).
- Correio Diplomático → Diplomatic Mail; carta → letter; carta privada → private letter; ultimato → ultimatum; declaração/comunicado público → public declaration / public communiqué; A responder → To answer; Respondidas → Answered; Enviadas → Sent; Lidas → Read; Novas → New; pede resposta → awaits reply; mensageiro → messenger; prazo → deadline.
- Conselho → Council; ministro(s) → minister(s); pasta → portfolio; a Mão → the Hand (as in "Hand of the King"); credibilidade → credibility; apreço → esteem; demitir → dismiss; Reunião do turno → This turn's meeting.
- espião/espiões → spy/spies; interceptar → intercept; Espionagem → Espionage; inteligência → Intelligence.
- reclamação → grievance; exigência → demand; crise → crisis; apoio à guerra → war support; rendição → surrender; tratado → treaty; acordo → agreement; aliança → alliance; povos independentes → independent peoples; patrocínio → patronage; Congresso → Congress; era → era; turno → turn; império → empire; nação → nation; território → territory; cidade → city.

## How to work
- Translate in batches (e.g. 150 keys at a time). Read the JSON, write the translated JSON back (UTF-8, valid JSON — escape `"` and `\` inside values). Keep keys verbatim — the safest way is to copy each key line and only fill the value.
- After writing, normalize and validate (both scripts only touch your language):
  `powershell -ExecutionPolicy Bypass -File "C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\tools\dev\extrair-textos.ps1" -Idioma <LANG>`
  `powershell -ExecutionPolicy Bypass -File "C:\Program Files (x86)\Steam\steamapps\common\Humankind\_Modding\tools\dev\validar-traducao.ps1" -Idioma <LANG>`
  The extractor keeps your values and re-sorts the file; if it reports "traduções de textos que saíram do código", you changed a key — fix it. The validator must end with `0 vazios, 0 com problema`.
- Do not edit any other file. Do not build.

## Report (short)
Final validator line, plus any keys whose meaning you were unsure of (key + what you chose).
